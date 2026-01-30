import time
import pika
import json
import os
from dotenv import load_dotenv
import sys

sys.path.append(os.path.dirname(os.path.dirname(__file__)))
import logging

# Import our AI services
from services.ai_service import AIService
from services.qdrant_service import QdrantService
from services.minio_service import MinioService
from consumers.health_check import HealthCheckServer

logger = logging.getLogger(__name__)

# TODO: pip install aiopika for async

class DocumentConsumer:
    def __init__(self):
        load_dotenv()

        # Validate required environment variables
        self._validate_environment()

        # RabbitMQ connection parameters
        self.rabbitmq_username = os.getenv('RABBITMQ_USERNAME')
        self.rabbitmq_password = os.getenv('RABBITMQ_PASSWORD')
        self.rabbitmq_host = os.getenv('RABBITMQ_HOST')
        self.rabbitmq_port = int(os.getenv('RABBITMQ_PORT', 5672))

        # Create connection with retry logic
        self.connection = None
        self.channel = None
        self.connect_with_retry()

        # Setup queues and exchanges
        self._setup_infrastructure()
        
        # Initialize AI services
        logger.info("Initializing AI services...")
        self._init_ai_services()
        
        # Initialize MinIO service
        logger.info("Initializing MinIO service...")
        self.minio_service = MinioService()
        
        # Temporary directory for downloaded files
        self.temp_dir = os.path.join(os.getcwd(), 'temp_downloads')
        os.makedirs(self.temp_dir, exist_ok=True)
        logger.info(f"Temp directory: {self.temp_dir}")
        
        # Start health check server
        self.health_server = None
        self._start_health_check_server()

    def _validate_environment(self):
        """Validate required environment variables are set"""
        required_vars = [
            'RABBITMQ_USERNAME',
            'RABBITMQ_PASSWORD',
            'RABBITMQ_HOST'
        ]
        
        missing_vars = [var for var in required_vars if not os.getenv(var)]
        
        if missing_vars:
            error_msg = f"Missing required environment variables: {', '.join(missing_vars)}"
            logger.error(error_msg)
            logger.error("Please set these variables in your .env file or environment")
            raise ValueError(error_msg)

    def connect_with_retry(self, max_retries=5):
        """Connect to RabbitMQ with automatic retry and exponential backoff"""
        retry_count = 0
        wait_time = 1
        
        while retry_count < max_retries:
            try:
                logger.info(f"Connecting to RabbitMQ (attempt {retry_count + 1}/{max_retries})...")
                
                credentials = pika.PlainCredentials(
                    self.rabbitmq_username,
                    self.rabbitmq_password
                )

                parameters = pika.ConnectionParameters(
                    host=self.rabbitmq_host,
                    port=self.rabbitmq_port,
                    credentials=credentials,
                    heartbeat=600,
                    blocked_connection_timeout=300
                )

                self.connection = pika.BlockingConnection(parameters)
                self.channel = self.connection.channel()
                
                logger.info("Successfully connected to RabbitMQ")
                return True
                
            except Exception as e:
                logger.error(f"Connection failed: {e}")
                retry_count += 1
                
                if retry_count < max_retries:
                    logger.info(f"Retrying in {wait_time} seconds...")
                    time.sleep(wait_time)
                    wait_time *= 2  # Exponential backoff: 1, 2, 4, 8, 16
                else:
                    logger.error("Max retries reached. Could not connect to RabbitMQ.")
                    raise

    def _setup_infrastructure(self):
        """Setup queues and exchanges - matches C# RabbitMqService"""

        # Declare queues (idempotent)
        self.channel.queue_declare(queue="documents.uploaded", durable=True)
        self.channel.queue_declare(queue="documents.results", durable=True)

        # Set QoS - process multiple messages for better throughput
        self.channel.basic_qos(prefetch_count=10)
    
    def _init_ai_services(self):
        """
        Initialize AI and Qdrant services
        Collections are created dynamically per chat when needed
        """
        try:
            # Get configuration from environment
            embedding_model = os.getenv('EMBEDDING_MODEL', 'BAAI/bge-small-en-v1.5')
            qdrant_host = os.getenv('QDRANT_HOST', 'localhost')
            qdrant_port = int(os.getenv('QDRANT_PORT', 6333))
            
            # Initialize AI service (loads embedding model)
            logger.info(f"Loading AI service with model: {embedding_model}")
            self.ai_service = AIService(embedding_model_name=embedding_model)
            
            # Initialize Qdrant service (connects to database)
            logger.info(f"Connecting to Qdrant at {qdrant_host}:{qdrant_port}")
            self.qdrant_service = QdrantService(host=qdrant_host, port=qdrant_port)
            
            logger.info("AI services initialized successfully")
            logger.info("Collections will be created per chat as needed")
            
        except Exception as e:
            logger.error(f"Failed to initialize AI services: {e}", exc_info=True)
            raise
    
    def _get_collection_name(self, chat_id: str) -> str:
        """
        Generate collection name from chat_id
        Format: chat_{chat_id}
        """
        return f"chat_{chat_id}"
    
    def _ensure_collection_exists(self, collection_name: str):
        """
        Ensure Qdrant collection exists for this chat
        Creates it if missing (idempotent operation)
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
            health_port = int(os.getenv('HEALTH_CHECK_PORT'))
            self.health_server = HealthCheckServer(consumer=self, port=health_port)
            self.health_server.start()
        except Exception as e:
            logger.warning(f"Failed to start health check server: {e}")
            logger.warning("Service will continue without health checks")
            
    def callback(self, ch, method, properties, body):
        """Process incoming document upload messages"""
        try:
            # Decode and parse the JSON message
            json_str = body.decode('utf-8')
            logger.debug(f"Raw message received: {json_str[:200]}...")

            # Parse JSON - MassTransit wraps the message in an envelope
            envelope = json.loads(json_str)
            
            # Extract the actual message from the envelope
            message = envelope.get('message', {})
            
            if not message:
                raise ValueError("Message envelope is empty or missing 'message' property")

            # Extract fields using snake_case (matches C# JsonPropertyName attributes)
            document_id = message.get('document_id')
            file_path = message.get('file_path')
            file_size = message.get('file_size')
            content_type = message.get('content_type')
            chat_id = message.get('chat_id')

            if not document_id:
                raise ValueError("document_id is missing from message")
            if not file_path:
                raise ValueError("file_path is missing from message")
            if not chat_id:
                raise ValueError("chat_id is missing from message")

            logger.info(f"Processing document: {document_id}")
            logger.info(f"   Chat ID: {chat_id}")
            logger.info(f"   MinIO path: {file_path}")
            logger.info(f"   Size: {file_size} bytes")
            logger.info(f"   Type: {content_type}")

            # Download file from MinIO to temporary location
            local_file_path = os.path.join(self.temp_dir, f"{document_id}.pdf")
            
            logger.info(f"   Downloading from MinIO...")
            download_success = self.minio_service.download_file(file_path, local_file_path)
            
            if not download_success:
                error_msg = f"Failed to download file from MinIO: {file_path}"
                logger.error(error_msg)
                self.send_result(document_id, False, error_message=error_msg)
                ch.basic_ack(delivery_tag=method.delivery_tag)
                return

            try:
                # Process the document with chat_id
                success = self.process_document(document_id, local_file_path, chat_id)
                error_msg = None if success else "Processing logic failed inside AI Service"

                # Send result back to Gateway
                self.send_result(document_id, success, error_message=error_msg)

            finally:
                # Clean up temporary file
                if os.path.exists(local_file_path):
                    try:
                        os.remove(local_file_path)
                        logger.info(f"   Cleaned up temp file: {local_file_path}")
                    except Exception as e:
                        logger.warning(f"   Failed to delete temp file: {e}")

            # Acknowledge message (remove from queue)
            ch.basic_ack(delivery_tag=method.delivery_tag)

            logger.info(f"Successfully processed: {document_id}")

        except json.JSONDecodeError as e:
            logger.error(f"JSON decode error: {e}")
            logger.error(f"   Raw body: {body[:200]}...")
            ch.basic_nack(delivery_tag=method.delivery_tag, requeue=False)

        except ValueError as e:
            logger.error(f"Validation error: {e}")
            logger.error(f"   Message content: {json.dumps(message if 'message' in locals() else {}, indent=2)}")
            ch.basic_nack(delivery_tag=method.delivery_tag, requeue=False)

        except Exception as e:
            logger.error(f"Unexpected error: {e}", exc_info=True)
            ch.basic_nack(delivery_tag=method.delivery_tag, requeue=False)
    
    def _retry_operation(self, operation, operation_name: str, max_retries: int = 3):
        """
        Retry an operation with exponential backoff
        
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
        """
        Validate file before processing
        
        Checks:
        1. File exists
        2. File has .pdf extension
        3. File size is within limits
        4. File is a PDF
        
        Args:
            file_path: Path to file
            
        Raises:
            ValueError: If validation fails
        """
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

    def send_result(self, document_id: str, success: bool, error_message: str = None):
        """Send processing result back to C# Gateway using snake_case to match DocumentProcessedEvent"""
        result = {
            'document_id': document_id,
            'success': success,
            'error_message': error_message
        }

        result_json = json.dumps(result)
        logger.info(f"Sending result to C# Gateway...")

        # Publish to documents.results queue
        self.channel.basic_publish(
            exchange='',
            routing_key='documents.results',
            body=result_json,
            properties=pika.BasicProperties(
                delivery_mode=2,  # Persistent
                content_type='application/json'
            )
        )

        logger.info(f"Result sent: {'Success' if success else 'Failed'}")

    def start_consuming(self):
        """Start listening for messages"""
        logger.info("=" * 60)
        logger.info("Document Consumer Started")
        logger.info("=" * 60)
        logger.info(f"   RabbitMQ Host: {self.rabbitmq_host}:{self.rabbitmq_port}")
        logger.info(f"   Listening Queue: documents.uploaded")
        logger.info(f"   Result Queue: documents.results")
        logger.info(f"\n   Press CTRL+C to stop\n")
        logger.info("=" * 60)

        self.channel.basic_consume(
            queue='documents.uploaded',
            on_message_callback=self.callback,
            auto_ack=False  # Manual acknowledgment
        )

        try:
            self.channel.start_consuming()
        except KeyboardInterrupt:
            logger.info("\n" + "=" * 60)
            logger.info("Stopping consumer...")
            logger.info("=" * 60)
            
            # Stop health check server
            if self.health_server:
                self.health_server.stop()
            
            self.channel.stop_consuming()
            self.connection.close()
        except Exception as e:
            logger.error(f"Connection lost: {e}")
            logger.info("Attempting to reconnect...")
            try:
                if self.connection and not self.connection.is_closed:
                    self.connection.close()
            except:
                pass
            
            # Reconnect and restart consuming
            self.connect_with_retry()
            self._setup_infrastructure()
            self.start_consuming()
