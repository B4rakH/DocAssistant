from typing import List, Dict, Optional
from qdrant_client import QdrantClient
from qdrant_client.models import Distance, VectorParams, PointStruct
import uuid
import logging

logger = logging.getLogger(__name__)


class QdrantService:
    
    def __init__(self, host: str = "localhost", port: int = 6333):
        
        logger.info("Initializing Qdrant Service...")
        
        self.host = host
        self.port = port
        self.client = None
        
        # Connect to Qdrant
        self._connect()
        
        logger.info("Qdrant Service initialized")
    
    def _connect(self):

        try:
            logger.info(f"Connecting to Qdrant at {self.host}:{self.port}")
            
            # Create Qdrant client
            self.client = QdrantClient(
                host=self.host,
                port=self.port
            )
            
            # Test connection by getting server info
            info = self.client.get_collections()
            logger.info(f"Connected successfully")
            logger.info(f"Existing collections: {len(info.collections)}")
            
        except Exception as e:
            logger.error(f"Failed to connect to Qdrant: {e}")
            logger.error(f"Make sure Qdrant is running: docker ps")
            raise
    
    def health_check(self) -> bool:
        
        try:
            self.client.get_collections()
            return True
        except Exception as e:
            logger.error(f"Health check failed: {e}")
            return False
    
    def create_collection(self, collection_name: str, vector_size: int = 384):
        
        try:
            # Check if collection already exists
            collections = self.client.get_collections().collections
            existing_names = [col.name for col in collections]
            
            if collection_name in existing_names:
                logger.info(f"Collection '{collection_name}' already exists")
                return
            
            logger.info(f"Creating collection: {collection_name}")
            logger.info(f"Vector size: {vector_size}")
            
            # Create collection with configuration
            self.client.create_collection(
                collection_name=collection_name,
                vectors_config=VectorParams(
                    size=vector_size,
                    distance=Distance.COSINE  # How to measure similarity
                )
            )
            
            logger.info(f"Collection created successfully")
            
        except Exception as e:
            logger.error(f"Failed to create collection: {e}")
            raise
    
    def collection_exists(self, collection_name: str) -> bool:

        try:
            collections = self.client.get_collections().collections
            existing_names = [col.name for col in collections]
            return collection_name in existing_names
        except Exception as e:
            logger.error(f"Error checking collection: {e}")
            return False
    
    def delete_collection(self, collection_name: str):
        
        try:
            if not self.collection_exists(collection_name):
                logger.warning(f"Collection '{collection_name}' does not exist")
                return
            
            logger.info(f"Deleting collection: {collection_name}")
            self.client.delete_collection(collection_name)
            logger.info(f"Collection deleted")
            
        except Exception as e:
            logger.error(f"Failed to delete collection: {e}")
            raise
    
    def store_document_chunks(self, collection_name: str, document_id: str, chunks: List[Dict]):
        """
        Args:
            collection_name: Collection to store in
            document_id: Unique ID of the document
            chunks: List of chunks with embeddings from AI service
        """
        try:
            if not self.collection_exists(collection_name):
                logger.error(f"Collection '{collection_name}' does not exist")
                logger.error(f"Create it first with create_collection()")
                raise ValueError(f"Collection {collection_name} not found")
            
            logger.info(f"Storing {len(chunks)} chunks for document {document_id}")
            
            # Prepare points for Qdrant
            points = []
            
            for chunk in chunks:
                # Generate unique ID for this chunk
                point_id = str(uuid.uuid4())
                
                # Create point with vector and metadata
                point = PointStruct(
                    id=point_id,
                    vector=chunk['embedding'],  # The 384 numbers
                    payload={
                        # Metadata we can search and filter by
                        'document_id': document_id,
                        'chunk_index': chunk['index'],
                        'text': chunk['text'],
                        'start_pos': chunk['start_pos'],
                        'end_pos': chunk['end_pos'],
                        'length': chunk['length']
                    }
                )
                points.append(point)
            
            # Upload all points to Qdrant in one batch
            self.client.upsert(
                collection_name=collection_name,
                points=points
            )
            
            logger.info(f"Stored {len(points)} chunks successfully")
            
        except Exception as e:
            logger.error(f"Failed to store chunks: {e}")
            raise
    
    def get_collection_info(self, collection_name: str) -> Dict:
        """
        Args:
            collection_name: Collection to inspect
            
        Returns:
            Dict with collection stats
        """
        try:
            if not self.collection_exists(collection_name):
                return None
            
            info = self.client.get_collection(collection_name)
            
            return {
                'name': collection_name,
                'vectors_count': info.vectors_count,
                'points_count': info.points_count,
                'status': info.status
            }
            
        except Exception as e:
            logger.error(f"Error getting collection info: {e}")
            return None
    
    def search_similar_chunks(self, collection_name: str, query_vector: List[float], 
                             limit: int = 5, document_id: Optional[str] = None) -> List[Dict]:

        try:
            if not self.collection_exists(collection_name):
                logger.error(f"Collection '{collection_name}' does not exist")
                return []
            
            logger.info(f"Searching for top {limit} similar chunks...")
            
            # Build filter if searching specific document
            search_filter = None
            if document_id:
                from qdrant_client.models import Filter, FieldCondition, MatchValue
                search_filter = Filter(
                    must=[
                        FieldCondition(
                            key="document_id",
                            match=MatchValue(value=document_id)
                        )
                    ]
                )
            
            # Perform vector search
            results = self.client.search(
                collection_name=collection_name,
                query_vector=query_vector,
                limit=limit,
                query_filter=search_filter
            )
            
            # Format results
            formatted_results = []
            for result in results:
                formatted_results.append({
                    'id': result.id,
                    'score': result.score,  # Similarity score (0-1, higher = more similar)
                    'document_id': result.payload.get('document_id'),
                    'chunk_index': result.payload.get('chunk_index'),
                    'text': result.payload.get('text'),
                    'start_pos': result.payload.get('start_pos'),
                    'end_pos': result.payload.get('end_pos')
                })
            
            logger.info(f"Found {len(formatted_results)} results")
            return formatted_results
            
        except Exception as e:
            logger.error(f"Search failed: {e}")
            raise

