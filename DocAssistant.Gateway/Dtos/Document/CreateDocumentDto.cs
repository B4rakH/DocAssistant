namespace DocAssistant.Gateway.Dtos.Document
{
    public record CreateDocumentDto
    {
        public required Guid ChatId { get; set; }

        public required IFormFile File { get; set; }

        public required string FilePath { get; set; }

    }
}
