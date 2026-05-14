"""
Shared message contracts for RabbitMQ communication.
These dataclasses mirror the C# event DTOs in DocAssistant.Gateway/Dtos/Events/
and the JSON schemas in /contracts/*.schema.json

Any field changes MUST be reflected in all three places:
  1. This file (Python dataclasses)
  2. C# event DTOs (Gateway/Dtos/Events/)
  3. JSON schemas (contracts/*.schema.json)
"""

from dataclasses import dataclass, field, asdict
from datetime import datetime
from typing import Optional


# ──────────────────────────────────────────────
# Gateway → AIService
# ──────────────────────────────────────────────

@dataclass
class DocumentUploadedEvent:
    """Received when Gateway uploads a document for AI processing."""
    document_id: str
    chat_id: str
    file_path: str
    file_size: int = 0
    content_type: Optional[str] = None
    correlation_id: Optional[str] = None

    @classmethod
    def from_message(cls, message: dict) -> 'DocumentUploadedEvent':
        """Parse from MassTransit message body."""
        event = cls(
            document_id=message.get('document_id'),
            chat_id=message.get('chat_id'),
            file_path=message.get('file_path'),
            file_size=message.get('file_size', 0),
            content_type=message.get('content_type'),
            correlation_id=message.get('correlation_id'),
        )
        event.validate()
        return event

    def validate(self):
        if not self.document_id:
            raise ValueError("document_id is missing from message")
        if not self.chat_id:
            raise ValueError("chat_id is missing from message")
        if not self.file_path:
            raise ValueError("file_path is missing from message")


@dataclass
class ChatMessageSentEvent:
    """Received when a user sends a chat message for AI processing."""
    chat_id: str
    message_id: str
    content: str
    timestamp: Optional[str] = None
    correlation_id: Optional[str] = None

    @classmethod
    def from_message(cls, message: dict) -> 'ChatMessageSentEvent':
        """Parse from MassTransit message body."""
        event = cls(
            chat_id=message.get('chat_id'),
            message_id=message.get('message_id'),
            content=message.get('content'),
            timestamp=message.get('timestamp'),
            correlation_id=message.get('correlation_id'),
        )
        event.validate()
        return event

    def validate(self):
        if not self.chat_id:
            raise ValueError("chat_id is missing from message")
        if not self.message_id:
            raise ValueError("message_id is missing from message")
        if not self.content:
            raise ValueError("content is missing from message")


@dataclass
class ChatDeletedEvent:
    """Received when Gateway deletes a chat and its associated data."""
    chat_id: str
    correlation_id: Optional[str] = None

    @classmethod
    def from_message(cls, message: dict) -> 'ChatDeletedEvent':
        """Parse from MassTransit message body."""
        event = cls(
            chat_id=message.get('chat_id'),
            correlation_id=message.get('correlation_id'),
        )
        event.validate()
        return event

    def validate(self):
        if not self.chat_id:
            raise ValueError("chat_id is missing from message")


# ──────────────────────────────────────────────
# AIService → Gateway
# ──────────────────────────────────────────────

@dataclass
class DocumentProcessedEvent:
    """Sent back to Gateway after document processing completes."""
    document_id: str
    success: bool
    error_message: Optional[str] = None
    correlation_id: Optional[str] = None

    def to_dict(self) -> dict:
        return asdict(self)


@dataclass
class ChatMessageResponseEvent:
    """Sent back to Gateway with the AI-generated response."""
    chat_id: str
    message_id: str
    response: str
    is_success: bool = True
    confidence_score: float = 0.0
    error_message: Optional[str] = None
    timestamp: str = field(default_factory=lambda: datetime.utcnow().isoformat())
    correlation_id: Optional[str] = None

    def to_dict(self) -> dict:
        return asdict(self)
