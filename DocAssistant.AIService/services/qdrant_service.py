import os
from typing import List, Dict, Optional
from qdrant_client import QdrantClient
from qdrant_client.models import Distance, VectorParams, PointStruct
import uuid


class QdrantService:
    
    def __init__(self, host: str = "localhost", port: int = 6333):
        
        print("Initializing Qdrant Service...")
        
        self.host = host
        self.port = port
        self.client = None
        
        # Connect to Qdrant
        self._connect()
        
        print("Qdrant Service initialized\n")
    
    def _connect(self):

        try:
            print(f"   Connecting to Qdrant at {self.host}:{self.port}")
            
            # Create Qdrant client
            self.client = QdrantClient(
                host=self.host,
                port=self.port
            )
            
            # Test connection by getting server info
            info = self.client.get_collections()
            print(f"   Connected successfully")
            print(f"   Existing collections: {len(info.collections)}")
            
        except Exception as e:
            print(f"   ERROR: Failed to connect to Qdrant: {e}")
            print(f"   Make sure Qdrant is running: docker ps")
            raise
    
    def health_check(self) -> bool:
        
        try:
            self.client.get_collections()
            return True
        except Exception as e:
            print(f"Health check failed: {e}")
            return False
    
    def create_collection(self, collection_name: str, vector_size: int = 384):
        
        try:
            # Check if collection already exists
            collections = self.client.get_collections().collections
            existing_names = [col.name for col in collections]
            
            if collection_name in existing_names:
                print(f"   Collection '{collection_name}' already exists")
                return
            
            print(f"Creating collection: {collection_name}")
            print(f"   Vector size: {vector_size}")
            
            # Create collection with configuration
            self.client.create_collection(
                collection_name=collection_name,
                vectors_config=VectorParams(
                    size=vector_size,
                    distance=Distance.COSINE  # How to measure similarity
                )
            )
            
            print(f"   Collection created successfully")
            
        except Exception as e:
            print(f"   ERROR: Failed to create collection: {e}")
            raise
    
    def collection_exists(self, collection_name: str) -> bool:

        try:
            collections = self.client.get_collections().collections
            existing_names = [col.name for col in collections]
            return collection_name in existing_names
        except Exception as e:
            print(f"Error checking collection: {e}")
            return False
    
    def delete_collection(self, collection_name: str):
        
        try:
            if not self.collection_exists(collection_name):
                print(f"   Collection '{collection_name}' does not exist")
                return
            
            print(f"Deleting collection: {collection_name}")
            self.client.delete_collection(collection_name)
            print(f"   Collection deleted")
            
        except Exception as e:
            print(f"   ERROR: Failed to delete collection: {e}")
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
                print(f"   ERROR: Collection '{collection_name}' does not exist")
                print(f"   Create it first with create_collection()")
                raise ValueError(f"Collection {collection_name} not found")
            
            print(f"Storing {len(chunks)} chunks for document {document_id}")
            
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
            
            print(f"   Stored {len(points)} chunks successfully")
            
        except Exception as e:
            print(f"   ERROR: Failed to store chunks: {e}")
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
            print(f"Error getting collection info: {e}")
            return None
    
    def search_similar_chunks(self, collection_name: str, query_vector: List[float], 
                             limit: int = 5, document_id: Optional[str] = None) -> List[Dict]:

        try:
            if not self.collection_exists(collection_name):
                print(f"   ERROR: Collection '{collection_name}' does not exist")
                return []
            
            print(f"Searching for top {limit} similar chunks...")
            
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
            
            print(f"   Found {len(formatted_results)} results")
            return formatted_results
            
        except Exception as e:
            print(f"   ERROR: Search failed: {e}")
            raise


# Test the connection if run directly
if __name__ == "__main__":
    # Test connection to Qdrant
    try:
        service = QdrantService()
        
        print("\n" + "="*60)
        print("Testing connection...")
        
        if service.health_check():
            print("SUCCESS: Qdrant is running and accessible")
        else:
            print("FAILED: Cannot connect to Qdrant")
        
        # Test collection creation
        print("\n" + "="*60)
        print("Testing collection management...")
        
        collection_name = "test_documents"
        
        # Create collection
        service.create_collection(collection_name, vector_size=384)
        
        # Check if exists
        if service.collection_exists(collection_name):
            print(f"SUCCESS: Collection '{collection_name}' exists")
        
        # Test storing vectors (mock data)
        print("\n" + "="*60)
        print("Testing vector storage...")
        
        # Create mock chunks with embeddings (like from AI service)
        mock_chunks = [
            {
                'index': 0,
                'text': 'This is the first chunk of text',
                'start_pos': 0,
                'end_pos': 100,
                'length': 100,
                'embedding': [0.1] * 384,  # Mock 384-dimensional vector
                'embedding_dim': 384
            },
            {
                'index': 1,
                'text': 'This is the second chunk of text',
                'start_pos': 100,
                'end_pos': 200,
                'length': 100,
                'embedding': [0.2] * 384,  # Mock 384-dimensional vector
                'embedding_dim': 384
            }
        ]
        
        # Store chunks
        service.store_document_chunks(
            collection_name=collection_name,
            document_id="test-doc-001",
            chunks=mock_chunks
        )
        
        # Get collection info
        info = service.get_collection_info(collection_name)
        if info:
            print(f"\nCollection info:")
            print(f"  Name: {info['name']}")
            print(f"  Points: {info['points_count']}")
            print(f"  Vectors: {info['vectors_count']}")
            print(f"  Status: {info['status']}")
        
        # Test search
        print("\n" + "="*60)
        print("Testing vector search...")
        
        # Create a query vector (similar to first chunk)
        query_vector = [0.15] * 384  # Between 0.1 and 0.2
        
        results = service.search_similar_chunks(
            collection_name=collection_name,
            query_vector=query_vector,
            limit=2
        )
        
        print(f"\nSearch results:")
        for i, result in enumerate(results, 1):
            print(f"  Result {i}:")
            print(f"    Score: {result['score']:.4f}")
            print(f"    Document: {result['document_id']}")
            print(f"    Text: {result['text'][:50]}...")
        
        # Clean up - delete test collection
        print("\n" + "="*60)
        print("Cleaning up test collection...")
        service.delete_collection(collection_name)
        print("Test completed successfully")
            
    except Exception as e:
        print(f"ERROR: {e}")
        print("\nMake sure Qdrant is running:")
        print("  docker ps")
        print("  docker-compose up -d")
