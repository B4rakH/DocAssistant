"""
Centralized configuration - single source of truth for all environment variables.
All other modules should import Settings instead of calling os.getenv() directly.
"""

import os
from dotenv import load_dotenv


class Settings:
    """Loads and validates all environment variables once."""

    def __init__(self):
        load_dotenv()

        # RabbitMQ
        self.rabbitmq_username = os.getenv('RABBITMQ_USERNAME')
        self.rabbitmq_password = os.getenv('RABBITMQ_PASSWORD')
        self.rabbitmq_host = os.getenv('RABBITMQ_HOST')
        self.rabbitmq_port = int(os.getenv('RABBITMQ_PORT', 5672))

        # AI Models
        self.embedding_model = os.getenv('EMBEDDING_MODEL', 'BAAI/bge-small-en-v1.5')

        # Qdrant
        self.qdrant_host = os.getenv('QDRANT_HOST', 'localhost')
        self.qdrant_port = int(os.getenv('QDRANT_PORT', 6333))

        # MinIO
        self.minio_endpoint = os.getenv('MINIO_ENDPOINT', 'localhost:9000')
        self.minio_access_key = os.getenv('MINIO_ACCESS_KEY', 'minioadmin')
        self.minio_secret_key = os.getenv('MINIO_SECRET_KEY', 'minioadmin')
        self.minio_use_ssl = os.getenv('MINIO_USE_SSL', 'false').lower() == 'true'
        self.minio_bucket = os.getenv('MINIO_BUCKET') or os.getenv('MINIO_BUCKET_NAME')

        # Health Check
        self.health_check_port = int(os.getenv('HEALTH_CHECK_PORT', 8080))

        # Validate required variables
        self._validate()

    def _validate(self):
        """Validate that all required environment variables are present."""
        required = {
            'RABBITMQ_USERNAME': self.rabbitmq_username,
            'RABBITMQ_PASSWORD': self.rabbitmq_password,
            'RABBITMQ_HOST': self.rabbitmq_host,
            'MINIO_BUCKET': self.minio_bucket,
        }

        missing = [name for name, value in required.items() if not value]

        if missing:
            raise ValueError(
                f"Missing required environment variables: {', '.join(missing)}. "
                f"Please set these in your .env file or environment."
            )
