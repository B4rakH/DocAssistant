using DocAssistant.Gateway.Common.Enums;
using DocAssistant.Gateway.Data.Models;
using DocAssistant.Gateway.Dtos.Events;

namespace DocAssistant.Gateway.Mappers
{
    public static class DocumentMapper
    {
        public static Document ToModelFromFile(this IFormFile file, Guid chatId, string filePath, DocumentStatus status)
        {
            return new Document
            {
                ChatId = chatId,
                FileName = file.FileName,
                FilePath = filePath,
                FileSize = file.Length,
                ContentType = file.ContentType,
                Status = status  // Use the passed status parameter
            };
        }

        public static DocumentUploadedEvent ToUploadedEventFromModel(this Document document)
        {
            return new DocumentUploadedEvent
            {
                ChatId = document.ChatId,
                DocumentId = document.Id,
                FilePath = document.FilePath,
                FileSize = document.FileSize,
                ContentType = document.ContentType,
                CorrelationId = Guid.NewGuid()
            };
        }
    }
}
