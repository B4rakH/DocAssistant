namespace DocAssistant.Gateway.Common.Enums
{
    public enum DocumentStatus
    {
        Pending = 0,    // Uploaded to API, waiting for Python
        Processing = 1, // Python is currently reading/vectorizing
        Completed = 2,  // Vectors are in Qdrant, ready for chat
        Failed = 3      // Something went wrong (e.g., corrupt PDF)
    }
}
