import time
import pika
import json
import os
from dotenv import load_dotenv

# TODO: pip install aiopika for async
# TODO: pip install logging and replace print()

class DocumentConsumer:
    def __init__(self):
        load_dotenv()

        # RabbitMQ connection parameters
        self.rabbitmq_username = os.getenv('RABBITMQ_USERNAME')
        self.rabbitmq_password = os.getenv('RABBITMQ_PASSWORD')
        self.rabbitmq_host = os.getenv('RABBITMQ_HOST')
        self.rabbitmq_port = int(os.getenv('RABBITMQ_PORT', 5672))

        # Create connection
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

        # Setup queues and exchanges
        self._setup_infrastructure()

    def _setup_infrastructure(self):
        """Setup queues and exchanges - matches C# RabbitMqService"""

        # Declare queues (idempotent)
        self.channel.queue_declare(queue="documents.uploaded", durable=True)
        self.channel.queue_declare(queue="documents.results", durable=True)

        # Set QoS - process one message at a time
        self.channel.basic_qos(prefetch_count=1)
            
    def callback(self, ch, method, properties, body):
        """Process incoming document upload messages"""
        try:
            # Decode and parse the JSON message
            json_str = body.decode('utf-8')
            print(f"Raw message received: {json_str[:200]}...")

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

            if not document_id:
                raise ValueError("document_id is missing from message")
            if not file_path:
                raise ValueError("file_path is missing from message")

            print(f"Processing document: {document_id}")
            print(f"   File: {file_path}")
            print(f"   Size: {file_size} bytes")
            print(f"   Type: {content_type}")

            # Process the document
            success = self.process_document(document_id, file_path)
            error_msg = None if success else "Processing logic failed inside AI Service"

            # Send result back to Gateway
            self.send_result(document_id, success, error_message=error_msg)

            # Acknowledge message (remove from queue)
            ch.basic_ack(delivery_tag=method.delivery_tag)

            print(f"Successfully processed: {document_id}\n")

        except json.JSONDecodeError as e:
            print(f"JSON decode error: {e}")
            print(f"   Raw body: {body[:200]}...")  # Print first 200 chars
            ch.basic_nack(delivery_tag=method.delivery_tag, requeue=False)

        except ValueError as e:
            print(f"Validation error: {e}")
            print(f"   Message content: {json.dumps(message if 'message' in locals() else {}, indent=2)}")
            ch.basic_nack(delivery_tag=method.delivery_tag, requeue=False)

        except Exception as e:
            print(f"Unexpected error: {e}")
            import traceback
            traceback.print_exc()
            ch.basic_nack(delivery_tag=method.delivery_tag, requeue=False)

    def process_document(self, document_id: str, file_path: str) -> bool:
        """
        TODO:
        AI processing logic:
        1. Read PDF file
        2. Extract text
        3. Generate embeddings
        4. Store vectors in database
        """
        try:
            print(f"Processing started...")

            # Check if file exists
            if not os.path.exists(file_path):
                print(f"File not found: {file_path}")
                return False

            # Get file info
            file_size = os.path.getsize(file_path)
            print(f"   File exists: {file_size} bytes")

            # Simulate AI processing
            print(f"   Extracting text...")
            time.sleep(1)
            print(f"   Generating embeddings...")
            time.sleep(1)
            print(f"   Storing vectors...")
            time.sleep(1)

            print(f"Processing completed for {document_id}")
            return True

        except Exception as e:
            print(f"Processing error: {e}")
            import traceback
            traceback.print_exc()
            return False

    def send_result(self, document_id: str, success: bool, error_message: str = None):
        """Send processing result back to C# Gateway using snake_case to match DocumentProcessedEvent"""
        # Create result message using snake_case (matches C# JsonPropertyName)
        result = {
            'document_id': document_id,  # ← snake_case to match C#
            'success': success,
            'error_message': error_message
        }

        result_json = json.dumps(result)
        print(f"Sending result to C# Gateway...")

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

        print(f"Result sent: {'Success' if success else 'Failed'}")

    def start_consuming(self):
        """Start listening for messages"""
        print("=" * 60)
        print("Document Consumer Started")
        print("=" * 60)
        print(f"   RabbitMQ Host: {self.rabbitmq_host}:{self.rabbitmq_port}")
        print(f"   Listening Queue: documents.uploaded")
        print(f"   Result Queue: documents.results")
        print(f"\n   Press CTRL+C to stop\n")
        print("=" * 60)

        self.channel.basic_consume(
            queue='documents.uploaded',
            on_message_callback=self.callback,
            auto_ack=False  # Manual acknowledgment
        )

        try:
            self.channel.start_consuming()
        except KeyboardInterrupt:
            print("\n" + "=" * 60)
            print("Stopping consumer...")
            print("=" * 60)
            self.channel.stop_consuming()
            self.connection.close()


# Usage
if __name__ == '__main__':
    consumer = DocumentConsumer()
    consumer.start_consuming()
