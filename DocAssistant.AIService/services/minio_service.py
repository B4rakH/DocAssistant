"""
MinIO Service for file storage operations.
Handles downloading, checking, and getting info for files in MinIO.
"""

import os
import logging
from minio import Minio
from minio.error import S3Error
from typing import Optional

from config.settings import Settings

logger = logging.getLogger(__name__)


class MinioService:

    def __init__(self, settings: Settings):
        """
        Initialize MinIO client using centralized settings.

        Args:
            settings: Application settings instance
        """
        self.endpoint = settings.minio_endpoint
        self.access_key = settings.minio_access_key
        self.secret_key = settings.minio_secret_key
        self.use_ssl = settings.minio_use_ssl
        self.bucket = settings.minio_bucket

        logger.info(f"Initializing MinIO client: {self.endpoint} (SSL: {self.use_ssl})")

        try:
            self.client = Minio(
                self.endpoint,
                access_key=self.access_key,
                secret_key=self.secret_key,
                secure=self.use_ssl
            )

            # Verify connection by checking if bucket exists
            if not self.client.bucket_exists(self.bucket):
                logger.warning(f"Bucket '{self.bucket}' does not exist, creating it...")
                self.client.make_bucket(self.bucket)
                logger.info(f"Bucket '{self.bucket}' created successfully")
            else:
                logger.info(f"Connected to MinIO bucket: {self.bucket}")

        except Exception as e:
            logger.error(f"Failed to initialize MinIO client: {e}")
            raise

    def download_file(self, object_name: str, destination_path: str) -> bool:
        """
        Download file from MinIO to local filesystem

        Args:
            object_name: Path/name of object in MinIO (e.g., 'chat-123/doc-456.pdf')
            destination_path: Local file path to save to

        Returns:
            True if successful, False otherwise
        """
        try:
            logger.info(f"Downloading from MinIO: {object_name}")
            logger.info(f"   Destination: {destination_path}")

            # Ensure destination directory exists
            os.makedirs(os.path.dirname(destination_path), exist_ok=True)

            # Download file from MinIO
            self.client.fget_object(
                bucket_name=self.bucket,
                object_name=object_name,
                file_path=destination_path
            )

            # Verify file was downloaded
            if not os.path.exists(destination_path):
                logger.error(f"File was not created at {destination_path}")
                return False

            file_size = os.path.getsize(destination_path)
            logger.info(f"   Downloaded successfully ({file_size / 1024:.1f} KB)")
            return True

        except S3Error as e:
            logger.error(f"MinIO S3 error downloading {object_name}: {e}")
            return False
        except Exception as e:
            logger.error(f"Failed to download {object_name}: {e}", exc_info=True)
            return False

    def file_exists(self, object_name: str) -> bool:
        """
        Check if file exists in MinIO

        Args:
            object_name: Path/name of object in MinIO

        Returns:
            True if file exists, False otherwise
        """
        try:
            self.client.stat_object(self.bucket, object_name)
            return True
        except S3Error:
            return False
        except Exception as e:
            logger.error(f"Error checking file existence: {e}")
            return False

    def get_file_info(self, object_name: str) -> Optional[dict]:
        """
        Get metadata about a file in MinIO

        Args:
            object_name: Path/name of object in MinIO

        Returns:
            Dict with file info or None if not found
        """
        try:
            stat = self.client.stat_object(self.bucket, object_name)
            return {
                'size': stat.size,
                'content_type': stat.content_type,
                'last_modified': stat.last_modified,
                'etag': stat.etag
            }
        except S3Error:
            logger.warning(f"File not found in MinIO: {object_name}")
            return None
        except Exception as e:
            logger.error(f"Error getting file info: {e}")
            return None

    def health_check(self) -> bool:
        """
        Check if MinIO is accessible

        Returns:
            True if connected and can list buckets, False otherwise
        """
        try:
            # Try to list buckets as a health check
            buckets = self.client.list_buckets()
            logger.debug(f"MinIO health check passed - {len(buckets)} buckets found")
            return True
        except Exception as e:
            logger.error(f"MinIO health check failed: {e}")
            return False
