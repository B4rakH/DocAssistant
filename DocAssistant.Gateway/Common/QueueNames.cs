namespace DocAssistant.Gateway.Common
{
    public static class QueueNames
    {
        // Document processing queues
        public const string uploadFileQueue = "documents.uploaded";
        public const string fileUploadResultQueue = "documents.results";

        // Chat message queues
        public const string chatMessageQueue = "chat.messages.sent";
        public const string chatMessageResponseQueue = "chat.messages.responses";

        // Chat lifecycle queues
        public const string chatDeletedQueue = "chat.deleted";
    }
}
