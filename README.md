# DocAssistant

DocAssistant is a modern, distributed chat-with-your-documents application. It allows users to create chat sessions, upload documents (PDFs, text, etc.), and interactively ask questions about the contents of those documents using state-of-the-art AI.

The application is built using a microservices architecture, heavily utilizing asynchronous event-driven communication to ensure a responsive, highly-scalable user experience.

## Architecture Overview

The system consists of two primary microservices communicating via **RabbitMQ**:

### 1. Gateway Service (`DocAssistant.Gateway`)
A **.NET 8 Web API** that acts as the entry point for the frontend clients.
* **Responsibilities**: 
  * Exposes RESTful endpoints for managing Chats and Messages.
  * Handles document uploads and stores the raw files securely in **MinIO**.
  * Stores application state (Chats, Messages, Document Metadata) in **PostgreSQL**.
  * Pushes real-time updates (document processing status, AI chat responses) to the frontend via **SignalR**.
  * Publishes events to RabbitMQ when actions occur (e.g., `DocumentUploadedEvent`, `ChatMessageSentEvent`, `ChatDeletedEvent`).

### 2. AI Service (`DocAssistant.AIService`)
A **Python** worker service dedicated to heavy AI operations.
* **Responsibilities**:
  * Consumes events from RabbitMQ.
  * **Document Processing**: Downloads uploaded files from MinIO, extracts text, generates vector embeddings, and stores them in **Qdrant**.
  * **Retrieval-Augmented Generation (RAG)**: Listens for chat messages, performs vector searches in Qdrant to find relevant document context, and queries **Ollama** (local LLMs) to generate an answer.
  * Publishes completion events back to the Gateway (`DocumentProcessedEvent`, `ChatMessageResponseEvent`).

---

## Infrastructure Stack

All infrastructure dependencies are orchestrated via a unified `docker-compose.yaml`:

* **RabbitMQ**: Event bus for asynchronous service-to-service messaging.
* **PostgreSQL**: Relational database for the Gateway.
* **MinIO**: S3-compatible object storage for uploaded user documents.
* **Qdrant**: High-performance vector database for semantic search and RAG.
* **Ollama**: Local AI model execution (GPU accelerated) for embeddings and LLM inference.

---

## Getting Started

### Prerequisites
* [Docker Desktop](https://www.docker.com/products/docker-desktop/) or Docker Engine + Docker Compose
* [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
* [Python 3.10+](https://www.python.org/downloads/)
* NVIDIA GPU with container toolkit installed (optional, but highly recommended for Ollama inference)

### 1. Start Infrastructure
Navigate to the root directory of the project and spin up the dependency containers:
```bash
docker-compose up -d
```
*Wait a moment for all services to become healthy.*

### 2. Run the Gateway Service
Navigate into the Gateway directory, apply database migrations, and run the project:
```bash
cd DocAssistant.Gateway
dotnet run
```
The Gateway API will be available (by default) at `https://localhost:7157` or `http://localhost:5157`.
Swagger UI is available at `/swagger`.

### 3. Run the AI Service
Navigate into the AIService directory, install dependencies, and start the python worker:
```bash
cd DocAssistant.AIService
python -m venv .venv

# Windows
.venv\Scripts\activate
# Linux/macOS
# source .venv/bin/activate

pip install -r requirements.txt
python main.py
```

---

## Shared Contracts

To maintain clear boundaries and type-safety across different languages (.NET and Python), the project uses language-agnostic JSON schemas for its message contracts. These can be found in the `/contracts` directory:

* `document_uploaded.schema.json`
* `document_processed.schema.json`
* `chat_message_sent.schema.json`
* `chat_message_response.schema.json`
* `queues.json` (Queue routing definitions)

---

## Real-Time Capabilities (SignalR)

DocAssistant uses SignalR to stream real-time updates directly to the client without polling.
Clients should connect to the hub at `/chatHub` and join a group using their specific `chatId`. 

The Gateway will push the following events:
* `DocumentStatusChanged`: Sent when a document finishes indexing or fails.
* `ReceiveMessage`: Sent when the AI Service finishes generating a response to a user query.
