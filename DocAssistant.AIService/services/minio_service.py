import os
import logging
from minio import Minio
from minio.error import S3Error
from typing import Optional

logger = logging.getLogger(__name__)


class MinioService:
    """
    Service for downloading files from MinIO (S3-compatible object storage)
    """
    
    def __init__(self, endpoint: str = None, access_key: str = None, 
                 secret_key: str = None, use_ssl: bool = False):
        """
        Initialize MinIO client
        
        Args:
            endpoint: MinIO server endpoint (e.g., 'localhost:9000')
            access_key: MinIO access key
            secret_key: MinIO secret key
            use_ssl: Whether to use HTTPS
        """
        self.endpoint = endpoint or os.getenv('MINIO_ENDPOINT', 'localhost:9000')
        self.access_key = access_key or os.getenv('MINIO_ACCESS_KEY', 'minioadmin')
        self.secret_key = secret_key or os.getenv('MINIO_SECRET_KEY', 'minioadmin')
        self.use_ssl = use_ssl if use_ssl is not None else os.getenv('MINIO_USE_SSL', 'false').lower() == 'true'
        self.bucket = os.getenv('MINIO_BUCKET', 'documents')
        
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


# Test the connection if run directly
if __name__ == "__main__":
    import sys
    from dotenv import load_dotenv
    
    # Setup logging
    logging.basicConfig(
        level=logging.INFO,
        format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
    )
    
    load_dotenv()
    
    print("\n" + "="*60)
    print("Testing MinIO Connection...")
    print("="*60)
    
    try:
        service = MinioService()
        
        if service.health_check():
            print("\n✓ SUCCESS: Connected to MinIO")
            print(f"  Endpoint: {service.endpoint}")
            print(f"  Bucket: {service.bucket}")
            print(f"  SSL: {service.use_ssl}")
        else:
            print("\n✗ FAILED: Cannot connect to MinIO")
            sys.exit(1)
            
    except Exception as e:
        print(f"\n✗ ERROR: {e}")
        sys.exit(1)
