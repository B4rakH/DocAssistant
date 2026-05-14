"""
Document and Chat Consumer.
Listens to RabbitMQ queues and orchestrates document processing and chat Q&A.
"""

import time
import json
import os
from datetime import datetime
from concurrent.futures import ThreadPoolExecutor
import logging

from config.settings import Settings
from consumers.rabbitmq import RabbitMQConnection
from consumers.health_check import HealthCheckServer
from services.ai_service import AIService
from services.qdrant_service import QdrantService
from services.minio_service import MinioService
from contracts.message_contracts import (
    DocumentUploadedEvent, DocumentProcessedEvent,
    ChatMessageSentEvent, ChatMessageResponseEvent,
    ChatDeletedEvent
)
from contracts.queue_names import QueueNames

logger = logging.getLogger(__name__)

# Max number of concurrent processing tasks
MAX_WORKERS = 3


class DocumentConsumer:
    def __init__(self, settings: Settings):
        self.settings = settings

        # Thread pool for offloading heavy processing (PDF, embeddings, etc.)
        self.executor = ThreadPoolExecutor(max_workers=MAX_WORKERS)

        # RabbitMQ connection
        logger.info("Connecting to RabbitMQ...")
        self.rabbitmq = RabbitMQConnection(settings)

        # Expose connection for health check compatibility
        self.connection = self.rabbitmq.connection

        # Initialize AI services
        logger.info("Initializing AI services...")
        self._init_ai_services()

        # Initialize MinIO service
        logger.info("Initializing MinIO service...")
        self.minio_service = MinioService(settings)

        # Temporary directory for downloaded files
        self.temp_dir = os.path.join(os.getcwd(), 'temp_downloads')
        os.makedirs(self.temp_dir, exist_ok=True)
        logger.info(f"Temp directory: {self.temp_dir}")

        # Start health check server
        self.health_server = None
        self._start_health_check_server()

    def _init_ai_services(self):
        """
        Initialize AI and Qdrant services.
        Collections are created dynamically per chat when needed.
        """
        try:
            # Initialize AI service (loads embedding model)
            logger.info(f"Loading AI service with model: {self.settings.embedding_model}")
            self.ai_service = AIService(embedding_model_name=self.settings.embedding_model)

            # Initialize Qdrant service (connects to database)
            logger.info(f"Connecting to Qdrant at {self.settings.qdrant_host}:{self.settings.qdrant_port}")
            self.qdrant_service = QdrantService(
                host=self.settings.qdrant_host,
                port=self.settings.qdrant_port
            )

            logger.info("AI services initialized successfully")
            logger.info("Collections will be created per chat as needed")

        except Exception as e:
            logger.error(f"Failed to initialize AI services: {e}", exc_info=True)
            raise

    def _get_collection_name(self, chat_id: str) -> str:
        """
        Generate collection name from chat_id.
        Format: chat_{chat_id}
        """
        return f"chat_{chat_id}"

    def _ensure_collection_exists(self, collection_name: str):
        """
        Ensure Qdrant collection exists for this chat.
        Creates it if missing (idempotent operation).
        """
        if not self.qdrant_service.collection_exists(collection_name):
            logger.info(f"Creating new collection: {collection_name}")
            self.qdrant_service.create_collection(
                collection_name=collection_name,
                vector_size=384  # Must match BAAI/bge-small-en-v1.5 embedding dimension
            )
        else:
            logger.debug(f"Collection already exists: {collection_name}")

    def _start_health_check_server(self):
        """Start HTTP server for health checks"""
        try:
            self.health_server = HealthCheckServer(
                consumer=self,
                port=self.settings.health_check_port
            )
            self.health_server.start()
        except Exception as e:
            logger.warning(f"Failed to start health check server: {e}")
            logger.warning("Service will continue without health checks")

    def _thread_safe_ack(self, ch, delivery_tag):
        """Schedule ACK on the connection's I/O thread (thread-safe)."""
        self.rabbitmq.connection.add_callback_threadsafe(
            lambda: ch.basic_ack(delivery_tag=delivery_tag)
        )

    def _thread_safe_nack(self, ch, delivery_tag, requeue=False):
        """Schedule NACK on the connection's I/O thread (thread-safe)."""
        self.rabbitmq.connection.add_callback_threadsafe(
            lambda: ch.basic_nack(delivery_tag=delivery_tag, requeue=requeue)
        )

    def _thread_safe_publish(self, routing_key: str, message: dict):
        """Schedule publish on the connection's I/O thread (thread-safe)."""
        self.rabbitmq.connection.add_callback_threadsafe(
            lambda: self.rabbitmq.publish_message(routing_key, message)
        )

    def callback(self, ch, method, properties, body):
        """Parse message on main thread, then offload heavy work to thread pool."""
        try:
            # Lightweight parsing on main thread (no I/O)
            json_str = body.decode('utf-8')
            logger.debug(f"Raw message received: {json_str[:200]}...")

            envelope = json.loads(json_str)
            message_dict = envelope.get('message', {})

            if not message_dict:
                raise ValueError("Message envelope is empty or missing 'message' property")

            event = DocumentUploadedEvent.from_message(message_dict)

            logger.info(f"Processing document: {event.document_id}" + (f" [CorrelationId: {event.correlation_id}]" if event.correlation_id else ""))
            logger.info(f"   Chat ID: {event.chat_id}")
            logger.info(f"   MinIO path: {event.file_path}")
            logger.info(f"   Size: {event.file_size} bytes")
            logger.info(f"   Type: {event.content_type}")

            # Submit heavy work to thread pool
            self.executor.submit(
                self._process_document_task,
                ch, method.delivery_tag, event.document_id, event.file_path, event.chat_id, event.correlation_id
            )

        except json.JSONDecodeError as e:
            logger.error(f"JSON decode error: {e}")
            logger.error(f"   Raw body: {body[:200]}...")
            ch.basic_nack(delivery_tag=method.delivery_tag, requeue=False)

        except ValueError as e:
            logger.error(f"Validation error: {e}")
            ch.basic_nack(delivery_tag=method.delivery_tag, requeue=False)

        except Exception as e:
            logger.error(f"Unexpected error dispatching document task: {e}", exc_info=True)
            ch.basic_nack(delivery_tag=method.delivery_tag, requeue=False)

    def _process_document_task(self, ch, delivery_tag, document_id, file_path, chat_id, correlation_id):
        """Heavy document processing — runs in thread pool worker."""
        try:
            # Download file from MinIO
            local_file_path = os.path.join(self.temp_dir, f"{document_id}.pdf")

            logger.info(f"   Downloading from MinIO...")
            download_success = self.minio_service.download_file(file_path, local_file_path)

            if not download_success:
                error_msg = f"Failed to download file from MinIO: {file_path}"
                logger.error(error_msg)
                self.send_result(document_id, False, error_message=error_msg, correlation_id=correlation_id)
                self._thread_safe_ack(ch, delivery_tag)
                return

            try:
                # Process the document
                success = self.process_document(document_id, local_file_path, chat_id)
                error_msg = None if success else "Processing logic failed inside AI Service"

                # Send result back to Gateway
                self.send_result(document_id, success, error_message=error_msg, correlation_id=correlation_id)

            finally:
                # Clean up temporary file
                if os.path.exists(local_file_path):
                    try:
                        os.remove(local_file_path)
                        logger.info(f"   Cleaned up temp file: {local_file_path}")
                    except Exception as e:
                        logger.warning(f"   Failed to delete temp file: {e}")

            # ACK via main thread
            self._thread_safe_ack(ch, delivery_tag)
            logger.info(f"Successfully processed: {document_id}")

        except Exception as e:
            logger.error(f"Unexpected error processing document {document_id}: {e}", exc_info=True)
            self._thread_safe_nack(ch, delivery_tag, requeue=False)

    def _retry_operation(self, operation, operation_name: str, max_retries: int = 3):
        """
        Retry an operation with exponential backoff.

        Args:
            operation: Function to execute
            operation_name: Name for logging
            max_retries: Maximum number of attempts

        Returns:
            Result of the operation
        """
        for attempt in range(max_retries):
            try:
                result = operation()
                if attempt > 0:
                    logger.info(f"   {operation_name} succeeded on attempt {attempt + 1}")
                return result

            except Exception as e:
                if attempt < max_retries - 1:
                    wait_time = 2 ** attempt  # 1s, 2s, 4s
                    logger.warning(f"   {operation_name} failed (attempt {attempt + 1}/{max_retries}): {e}")
                    logger.warning(f"   Retrying in {wait_time} seconds...")
                    time.sleep(wait_time)
                else:
                    logger.error(f"   {operation_name} failed after {max_retries} attempts")
                    raise

    def _validate_file(self, file_path: str) -> None:

        # Check 1: File exists
        if not os.path.exists(file_path):
            raise ValueError(f"File not found: {file_path}")

        # Check 2: Has .pdf extension
        if not file_path.lower().endswith('.pdf'):
            raise ValueError(f"File must be a PDF (got: {os.path.splitext(file_path)[1]})")

        # Check 3: File size validation
        file_size = os.path.getsize(file_path)

        # Minimum size (empty files)
        if file_size < 100:  # PDFs are at least a few hundred bytes
            raise ValueError(f"File is too small ({file_size} bytes) - may be empty or corrupted")

        # Maximum size (prevent memory issues)
        max_size = 50 * 1024 * 1024  # 50 MB
        if file_size > max_size:
            max_mb = max_size / (1024 * 1024)
            actual_mb = file_size / (1024 * 1024)
            raise ValueError(f"File too large ({actual_mb:.1f} MB). Maximum allowed: {max_mb:.0f} MB")

        # Check 4: Verify it's actually a PDF (check magic bytes)
        try:
            with open(file_path, 'rb') as f:
                header = f.read(4)
                if header != b'%PDF':
                    raise ValueError("File is not a valid PDF (invalid file header)")
        except Exception as e:
            if isinstance(e, ValueError):
                raise
            raise ValueError(f"Cannot read file: {e}")

        logger.info(f"   File validation passed ({file_size / 1024:.1f} KB)")

    def process_document(self, document_id: str, file_path: str, chat_id: str) -> bool:
        """
        AI processing pipeline:
        1. Validate file (type, size, format)
        2. Extract text from PDF
        3. Chunk text into smaller pieces
        4. Generate embeddings for each chunk
        5. Store vectors in chat-specific Qdrant collection
        """
        try:
            logger.info(f"Processing started for document: {document_id} (chat: {chat_id})")

            # Create collection name for this chat
            collection_name = self._get_collection_name(chat_id)

            # Ensure collection exists for this chat (idempotent operation)
            logger.info(f"   Ensuring collection exists: {collection_name}")
            self._ensure_collection_exists(collection_name)

            # Step 1: Validate file
            logger.info(f"   Validating file...")
            self._validate_file(file_path)

            # Step 2: Extract text from PDF
            logger.info(f"   Extracting text from PDF...")
            text = self.ai_service.extract_text_from_pdf(file_path)

            if not text or not text.strip():
                logger.error(f"   No text extracted from PDF")
                return False

            logger.info(f"   Extracted {len(text)} characters")

            # Step 3: Chunk the text
            logger.info(f"   Chunking text...")
            chunks = self.ai_service.chunk_text(text)

            if not chunks:
                logger.error(f"   No chunks created from text")
                return False

            logger.info(f"   Created {len(chunks)} chunks")

            # Step 4: Generate embeddings (with retry - might fail due to memory/timeout)
            logger.info(f"   Generating embeddings...")
            chunks_with_embeddings = self._retry_operation(
                operation=lambda: self.ai_service.generate_embeddings(chunks),
                operation_name="Embedding generation"
            )
            logger.info(f"   Generated {len(chunks_with_embeddings)} embeddings")

            # Step 5: Store in chat-specific Qdrant collection (with retry - might fail due to network)

            logger.info(f"   Storing vectors in Qdrant collection: {collection_name}...")
            self._retry_operation(
                operation=lambda: self.qdrant_service.store_document_chunks(
                    collection_name=collection_name,
                    document_id=document_id,
                    chunks=chunks_with_embeddings
                ),
                operation_name="Qdrant storage"
            )

            logger.info(f"Processing completed successfully for {document_id} in collection {collection_name}")
            return True

        except Exception as e:
            logger.error(f"Processing error for {document_id}: {e}", exc_info=True)
            return False

    def send_result(self, document_id: str, success: bool, error_message: str = None, correlation_id: str = None):
        event = DocumentProcessedEvent(
            document_id=document_id,
            success=success,
            error_message=error_message,
            correlation_id=correlation_id
        )

        logger.info(f"Sending result to Gateway...")

        # Thread-safe publish to documents.results queue
        self._thread_safe_publish(QueueNames.DOCUMENT_RESULTS, event.to_dict())

        logger.info(f"Result sent: {'Success' if success else 'Failed'}")

    # ──────────────────────────────────────────────
    # Chat Processing
    # ──────────────────────────────────────────────

    def chat_callback(self, ch, method, properties, body):
        """Parse chat message on main thread, then offload heavy work to thread pool."""
        try:
            # Lightweight parsing on main thread (no I/O)
            json_str = body.decode('utf-8')
            logger.debug(f"Raw chat message received: {json_str[:200]}...")

            envelope = json.loads(json_str)
            message_dict = envelope.get('message', {})

            if not message_dict:
                raise ValueError("Message envelope is empty or missing 'message' property")

            event = ChatMessageSentEvent.from_message(message_dict)

            logger.info(f"Processing chat message: {event.message_id}" + (f" [CorrelationId: {event.correlation_id}]" if event.correlation_id else ""))
            logger.info(f"   Chat ID: {event.chat_id}")
            logger.info(f"   Question: {event.content[:100]}...")

            # Submit heavy work to thread pool
            self.executor.submit(
                self._process_chat_task,
                ch, method.delivery_tag, event.message_id, event.chat_id, event.content, event.correlation_id
            )

        except json.JSONDecodeError as e:
            logger.error(f"JSON decode error: {e}")
            logger.error(f"   Raw body: {body[:200]}...")
            ch.basic_nack(delivery_tag=method.delivery_tag, requeue=False)

        except ValueError as e:
            logger.error(f"Validation error: {e}")
            ch.basic_nack(delivery_tag=method.delivery_tag, requeue=False)

        except Exception as e:
            logger.error(f"Unexpected error dispatching chat task: {e}", exc_info=True)
            ch.basic_nack(delivery_tag=method.delivery_tag, requeue=False)

    def _process_chat_task(self, ch, delivery_tag, message_id, chat_id, content, correlation_id):
        """Heavy chat processing — runs in thread pool worker."""
        try:
            # Process the question and get answer
            answer = self.process_chat_question(chat_id, content)

            # Send response back to Gateway
            self.send_chat_response(
                message_id=message_id,
                chat_id=chat_id,
                ai_response=answer,
                is_success=True,
                error_message=None,
                correlation_id=correlation_id
            )

        except Exception as e:
            error_msg = str(e)
            logger.error(f"Failed to process chat message {message_id}: {error_msg}", exc_info=True)
            
            # Send failure response back to Gateway
            self.send_chat_response(
                message_id=message_id,
                chat_id=chat_id,
                ai_response="An error occurred while processing your question.",
                is_success=False,
                error_message=error_msg,
                correlation_id=correlation_id
            )

        finally:
            # Always ACK so business-logic failures aren't infinitely retried
            self._thread_safe_ack(ch, delivery_tag)

    def send_chat_response(self, message_id: str, chat_id: str, ai_response: str, is_success: bool = True, error_message: str = None, correlation_id: str = None):
        """Send chat response back to Gateway"""

        event = ChatMessageResponseEvent(
            message_id=message_id,
            chat_id=chat_id,
            response=ai_response,
            is_success=is_success,
            confidence_score=0.0,
            error_message=error_message,
            correlation_id=correlation_id
        )

        logger.info(f"Sending chat response to Gateway...")

        # Thread-safe publish to chat.messages.responses queue
        self._thread_safe_publish(QueueNames.CHAT_MESSAGE_RESPONSE, event.to_dict())

        logger.info(f"Chat response sent")

    def process_chat_question(self, chat_id: str, question: str) -> str:
        """
        Process user question and return answer based on chat documents.

        Steps:
        1. Convert question to embedding
        2. Search chat collection for relevant chunks
        3. Return the relevant chunks (we'll add LLM later)
        """
        try:
            logger.info(f"Processing chat question for chat: {chat_id}")
            logger.info(f"   Question: {question[:100]}...")

            # Get collection name for this chat
            collection_name = self._get_collection_name(chat_id)

            # Check if collection exists (chat has documents)
            if not self.qdrant_service.collection_exists(collection_name):
                logger.warning(f"Collection '{collection_name}' does not exist")
                raise ValueError("No documents found for this chat. Please upload documents first.")

            # Step 1: Convert question to embedding
            logger.info(f"   Converting question to embedding...")
            question_chunks = [{'text': question}]
            embedded_question = self.ai_service.generate_embeddings(question_chunks)

            if not embedded_question or len(embedded_question) == 0:
                logger.error("Failed to generate embedding for question")
                raise RuntimeError("Failed to generate embedding for the question.")

            query_vector = embedded_question[0]['embedding']

            # Step 2: Search for similar chunks
            logger.info(f"   Searching for relevant documents...")
            similar_chunks = self.qdrant_service.search_similar_chunks(
                collection_name=collection_name,
                query_vector=query_vector,
                limit=5
            )

            if not similar_chunks:
                logger.warning("No relevant documents found")
                raise ValueError("I couldn't find relevant information in your documents.")

            logger.info(f"   Found {len(similar_chunks)} relevant chunks")

            # For now, just return the text from chunks
            # (We'll add LLM later to generate better answers)
            answer_parts = []
            for i, chunk in enumerate(similar_chunks, 1):
                score = chunk.get('score', 0)
                text = chunk.get('text', '')
                answer_parts.append(f"[Match {i} - Score: {score:.2f}]\n{text}\n")

            answer = "\n".join(answer_parts)
            logger.info(f"Answer generated successfully")

            return answer

        except Exception as e:
            logger.error(f"Error processing chat question: {e}", exc_info=True)
            raise

    # Chat Deletion
    def delete_chat_callback(self, ch, method, properties, body):
        """Parse chat deleted message on main thread, then offload cleanup to thread pool."""
        try:
            json_str = body.decode('utf-8')
            logger.debug(f"Raw chat deleted message received: {json_str[:200]}...")

            envelope = json.loads(json_str)
            message_dict = envelope.get('message', {})

            if not message_dict:
                raise ValueError("Message envelope is empty or missing 'message' property")

            event = ChatDeletedEvent.from_message(message_dict)

            logger.info(f"Processing chat deletion: {event.chat_id}" + (f" [CorrelationId: {event.correlation_id}]" if event.correlation_id else ""))

            # Submit cleanup to thread pool
            self.executor.submit(
                self._process_chat_deletion,
                ch, method.delivery_tag, event.chat_id, event.correlation_id
            )

        except json.JSONDecodeError as e:
            logger.error(f"JSON decode error: {e}")
            ch.basic_nack(delivery_tag=method.delivery_tag, requeue=False)

        except ValueError as e:
            logger.error(f"Validation error: {e}")
            ch.basic_nack(delivery_tag=method.delivery_tag, requeue=False)

        except Exception as e:
            logger.error(f"Unexpected error dispatching chat deletion task: {e}", exc_info=True)
            ch.basic_nack(delivery_tag=method.delivery_tag, requeue=False)

    def _process_chat_deletion(self, ch, delivery_tag, chat_id, correlation_id):
        """Delete Qdrant collection for the chat — runs in thread pool worker."""
        try:
            collection_name = self._get_collection_name(chat_id)

            logger.info(f"Deleting Qdrant collection: {collection_name}")
            self.qdrant_service.delete_collection(collection_name)

            logger.info(f"Chat deletion completed for chat {chat_id}")

        except Exception as e:
            logger.error(f"Failed to delete Qdrant collection for chat {chat_id}: {e}", exc_info=True)

        finally:
            # Always ACK — even if deletion fails, we don't want infinite retries
            self._thread_safe_ack(ch, delivery_tag)

    # ──────────────────────────────────────────────
    # Consumer Lifecycle
    # ──────────────────────────────────────────────

    def start_consuming(self):
        """Start listening for messages with reconnection loop."""
        logger.info("=" * 60)
        logger.info("Document Consumer Started")
        logger.info("=" * 60)
        logger.info(f"   RabbitMQ Host: {self.settings.rabbitmq_host}:{self.settings.rabbitmq_port}")
        logger.info(f"   Listening Queues: documents.uploaded, chat.messages.sent, chat.deleted")
        logger.info(f"   Thread Pool Workers: {MAX_WORKERS}")
        logger.info(f"\n   Press CTRL+C to stop\n")
        logger.info("=" * 60)

        while True:
            try:
                # Register consumers
                self.rabbitmq.channel.basic_consume(
                    queue=QueueNames.DOCUMENT_UPLOADED,
                    on_message_callback=self.callback,
                    auto_ack=False
                )

                self.rabbitmq.channel.basic_consume(
                    queue=QueueNames.CHAT_MESSAGE_SENT,
                    on_message_callback=self.chat_callback,
                    auto_ack=False
                )

                self.rabbitmq.channel.basic_consume(
                    queue=QueueNames.CHAT_DELETED,
                    on_message_callback=self.delete_chat_callback,
                    auto_ack=False
                )

                self.rabbitmq.channel.start_consuming()

            except KeyboardInterrupt:
                logger.info("\n" + "=" * 60)
                logger.info("Stopping consumer...")
                logger.info("=" * 60)

                self.rabbitmq.channel.stop_consuming()

                # Wait for in-flight tasks to complete
                logger.info("Waiting for in-flight tasks to complete...")
                self.executor.shutdown(wait=True, cancel_futures=False)
                logger.info("All tasks completed.")

                # Stop health check server
                if self.health_server:
                    self.health_server.stop()

                self.rabbitmq.connection.close()
                logger.info("Consumer stopped gracefully.")
                break  # Exit the while loop

            except Exception as e:
                logger.error(f"Connection lost: {e}")
                logger.info("Attempting to reconnect in 5 seconds...")
                time.sleep(5)

                try:
                    self.rabbitmq.reconnect()
                    self.connection = self.rabbitmq.connection
                    logger.info("Reconnected successfully. Resuming consumption...")
                except Exception as reconnect_error:
                    logger.error(f"Reconnection failed: {reconnect_error}")
                    logger.info("Will retry in 10 seconds...")
                    time.sleep(10)
