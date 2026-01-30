# Chat-Based Document Isolation

## Overview

Documents are isolated per chat using separate Qdrant collections. Each chat has its own dedicated collection, ensuring complete data separation.

## Architecture

### Collection Naming Strategy

- **Format**: `chat_{chat_id}`
- **Examples**:
  - Chat ID "123" → Collection "chat_123"
  - Chat ID "abc-def-456" → Collection "chat_abc-def-456"

### Collection Lifecycle

1. **Creation**: Collections are created dynamically when first document is uploaded to a chat
2. **Reuse**: Subsequent documents for the same chat use the existing collection
3. **Isolation**: Each chat's vectors are completely separated from other chats

## Implementation Flow

### 1. Message Reception

```json
{
  "message": {
    "document_id": "doc-123",
    "file_path": "uploads/file.pdf",
    "chat_id": "abc-456",
    "file_size": 1024,
    "content_type": "application/pdf"
  }
}
```

### 2. Collection Management

```python
# Generate collection name from chat_id
collection_name = f"chat_{chat_id}"  # e.g., "chat_abc-456"

# Create collection if it doesn't exist (idempotent)
if not qdrant_service.collection_exists(collection_name):
    qdrant_service.create_collection(
        collection_name=collection_name,
        vector_size=384  # BAAI/bge-small-en-v1.5 dimension
    )
```

### 3. Vector Storage

```python
# Store document vectors in chat-specific collection
qdrant_service.store_document_chunks(
    collection_name="chat_abc-456",  # Isolated per chat
    document_id="doc-123",
    chunks=chunks_with_embeddings
)
```

## Benefits

### Complete Data Isolation

- ✅ Each chat has its own Qdrant collection
- ✅ No risk of data leakage between chats
- ✅ Search results only include documents from the same chat

### Easy Management

- ✅ Delete all chat documents by dropping the collection
- ✅ Export/backup per-chat data easily
- ✅ Monitor storage per chat

### Scalability

- ✅ Collections created on-demand (no pre-provisioning needed)
- ✅ Independent scaling per chat
- ✅ No shared collection contention

## Example Scenarios

### Scenario 1: First Document in Chat

```
1. User uploads PDF in chat "sales-2024"
2. System receives: chat_id="sales-2024"
3. Collection "chat_sales-2024" doesn't exist
4. System creates collection "chat_sales-2024"
5. Document vectors stored in "chat_sales-2024"
```

### Scenario 2: Additional Document in Same Chat

```
1. User uploads another PDF in chat "sales-2024"
2. System receives: chat_id="sales-2024"
3. Collection "chat_sales-2024" already exists
4. Document vectors added to existing "chat_sales-2024"
```

### Scenario 3: Multiple Chats

```
Chat "sales-2024"   → Collection "chat_sales-2024"   → 5 documents
Chat "support-123"  → Collection "chat_support-123"  → 3 documents
Chat "legal-456"    → Collection "chat_legal-456"    → 8 documents

Total: 3 collections, 16 documents, complete isolation
```

## Code References

### Main Implementation

- **File**: [consumers/massTransit.py](consumers/massTransit.py)
- **Methods**:
  - `_get_collection_name(chat_id)` - Line ~133
  - `_ensure_collection_exists(collection_name)` - Line ~141
  - `process_document(document_id, file_path, chat_id)` - Line ~313

### Vector Storage

- **File**: [services/qdrant_service.py](services/qdrant_service.py)
- **Methods**:
  - `collection_exists(collection_name)` - Line ~37
  - `create_collection(collection_name, vector_size)` - Line ~43
  - `store_document_chunks(collection_name, ...)` - Line ~59

## Future Enhancements

### Potential Improvements

1. **Collection Metadata**: Store chat metadata (created date, document count)
2. **Retention Policies**: Auto-delete old chat collections
3. **Collection Quotas**: Limit documents per chat
4. **Cross-Chat Search**: Optional federated search across multiple chats
5. **Collection Aliases**: Support friendly names for chat collections
