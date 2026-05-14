"""
Shared queue name constants.
Mirrors C# QueueNames in DocAssistant.Gateway/Common/QueueNames.cs
and the definitions in /contracts/queues.json
"""


class QueueNames:
    """RabbitMQ queue names used for communication between Gateway and AIService."""

    # Document processing queues
    DOCUMENT_UPLOADED = "documents.uploaded"        # Gateway → AIService
    DOCUMENT_RESULTS = "documents.results"          # AIService → Gateway

    # Chat message queues
    CHAT_MESSAGE_SENT = "chat.messages.sent"        # Gateway → AIService
    CHAT_MESSAGE_RESPONSE = "chat.messages.responses"  # AIService → Gateway

    # Chat lifecycle queues
    CHAT_DELETED = "chat.deleted"                    # Gateway → AIService
