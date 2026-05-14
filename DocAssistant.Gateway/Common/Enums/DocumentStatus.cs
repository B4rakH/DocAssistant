namespace DocAssistant.Gateway.Common.Enums
{
    public enum DocumentStatus
    {
        Pending = 0,         // Uploaded to MinIO, waiting for AI service processing
        Processing = 1,      // AI Service is currently reading/vectorizing
        Completed = 2,       // Vectors are in Qdrant, ready for chat
        Failed = 3           // Something went wrong (e.g., corrupt PDF, network error)
    }
}
