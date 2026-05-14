"""
RabbitMQ connection and infrastructure management.
Extracted from the consumer to separate transport concerns from business logic.
"""

import time
import json
import uuid
from datetime import datetime
import pika
import logging

from config.settings import Settings
from contracts.queue_names import QueueNames

logger = logging.getLogger(__name__)


class RabbitMQConnection:
    """Manages RabbitMQ connection, channel, and queue infrastructure."""

    def __init__(self, settings: Settings):
        self.settings = settings
        self.connection = None
        self.channel = None

        # Connect with retry logic
        self.connect_with_retry()

        # Setup queues
        self._setup_infrastructure()

    def connect_with_retry(self, max_retries=5):
        """Connect to RabbitMQ with automatic retry and exponential backoff"""
        retry_count = 0
        wait_time = 1

        while retry_count < max_retries:
            try:
                logger.info(f"Connecting to RabbitMQ (attempt {retry_count + 1}/{max_retries})...")

                credentials = pika.PlainCredentials(
                    self.settings.rabbitmq_username,
                    self.settings.rabbitmq_password
                )

                parameters = pika.ConnectionParameters(
                    host=self.settings.rabbitmq_host,
                    port=self.settings.rabbitmq_port,
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

    def _setup_infrastructure(self, prefetch_count=3):
        """Setup queues and exchanges - matches C# RabbitMqService"""

        # Message TTL: 5 minutes
        queue_args = {'x-message-ttl': 300000}

        # Declare queues (idempotent)
        self.channel.queue_declare(queue=QueueNames.DOCUMENT_UPLOADED, durable=True, arguments=queue_args)
        self.channel.queue_declare(queue=QueueNames.DOCUMENT_RESULTS, durable=True, arguments=queue_args)
        self.channel.queue_declare(queue=QueueNames.CHAT_MESSAGE_SENT, durable=True, arguments=queue_args)
        self.channel.queue_declare(queue=QueueNames.CHAT_MESSAGE_RESPONSE, durable=True, arguments=queue_args)
        self.channel.queue_declare(queue=QueueNames.CHAT_DELETED, durable=True, arguments=queue_args)

        # Set QoS - match prefetch to thread pool size for optimal throughput
        self.channel.basic_qos(prefetch_count=prefetch_count)

    def publish(self, routing_key: str, body: str):
        """Publish a raw message to the specified queue."""
        self.channel.basic_publish(
            exchange='',
            routing_key=routing_key,
            body=body,
            properties=pika.BasicProperties(
                delivery_mode=2,  # Persistent
                content_type='application/json'
            )
        )

    def publish_message(self, routing_key: str, message: dict):
        """Publish a message wrapped in a MassTransit-compatible envelope."""
        envelope = {
            "messageId": str(uuid.uuid4()),
            "message": message,
            "sentTime": datetime.utcnow().isoformat()
        }

        self.channel.basic_publish(
            exchange='',
            routing_key=routing_key,
            body=json.dumps(envelope),
            properties=pika.BasicProperties(
                delivery_mode=2,  # Persistent
                content_type='application/json'
            )
        )

    def reconnect(self):
        """Close existing connection (if any) and reconnect."""
        try:
            if self.connection and not self.connection.is_closed:
                self.connection.close()
        except Exception:
            pass

        self.connect_with_retry()
        self._setup_infrastructure()
