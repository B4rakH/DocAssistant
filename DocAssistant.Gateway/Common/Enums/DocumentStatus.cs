namespace DocAssistant.Gateway.Common.Enums
{
    public enum DocumentStatus
    {
        Loading = 1,    // Python is currently reading/vectorizing
        Completed = 2,  // Vectors are in Qdrant, ready for chat
        Failed = 3      // Something went wrong (e.g., corrupt PDF)
    }
}
