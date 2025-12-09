using DocAssistant.Gateway.Common;
using DocAssistant.Gateway.Common.Enums;
using DocAssistant.Gateway.Data;
using DocAssistant.Gateway.Data.Models;
using DocAssistant.Gateway.Dtos.Chat;
using DocAssistant.Gateway.Dtos.Events;

namespace DocAssistant.Gateway.Services
{
    public class ChatService : IChatService
    {
        private readonly AppDbContext _context;
        private readonly IMessageProducer _rabbitMQService;
        private readonly ILogger<ChatService> _logger;

       public ChatService(AppDbContext context,
           IMessageProducer rabbitMQService,
           ILogger<ChatService> logger)
        {
            _context = context;
            _rabbitMQService = rabbitMQService;
            _logger = logger;
        }

        public async Task<Chat> CreateChatWithDocumentsAsync(CreateChatRequest request)
        {
            // Get upload directory path
            var uploadsFolder = FolderPath.GetUploadsFolder();

            // Track files for cleanup on failure
            var createdFilePaths = new List<string>();
            var documentsToProcess = new List<Document>();

            // Start database transaction for atomic operations
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Create new chat session
                var chat = new Chat
                {
                    Name = request.Name
                };

                _context.Chats.Add(chat);
                await _context.SaveChangesAsync();

                // Process each uploaded file
                foreach (var file in request.Files)
                {
                    if (file.Length > 0)
                    {
                        // Save file to disk with unique name
                        var safeFileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
                        var filePath = Path.Combine(uploadsFolder, safeFileName);

                        using (var stream = new FileStream(filePath, FileMode.Create)) 
                            await file.CopyToAsync(stream);

                        // Track for potential rollback
                        createdFilePaths.Add(filePath);

                        // Create document record with Pending status
                        var document = new Document
                        {
                            FileName = file.FileName,
                            FilePath = filePath,
                            FileSize = file.Length,
                            ContentType = file.ContentType,
                            Status = DocumentStatus.Pending
                        };

                        _context.Documents.Add(document);

                        // Link document to chat
                        _context.ChatDocuments.Add(new ChatDocument
                        {
                            ChatId = chat.Id,
                            Document = document
                        });

                        // Queue for RabbitMQ notification
                        documentsToProcess.Add(document);
                    }
                }

                // Commit all database changes
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // Send to RabbitMQ after successful commit
                await NotifyPythonWorker(documentsToProcess);

                return chat;
            }
            catch (Exception ex)
            {
                // Rollback database changes
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Transaction failed in CreateChatWithDocumentsAsync. Rolling back.");

                // Delete files from disk
                CleanupFiles(createdFilePaths);

                throw;
            }
        }

        private async Task NotifyPythonWorker(List<Document> documents)
        {
            var failedDocuments = new List<Guid>();

            try
            {
                // Publish each document to RabbitMQ
                foreach (var doc in documents)
                {
                    var uploadEvent = new DocumentUploadedEvent
                    {
                        DocumentId = doc.Id,
                        FilePath = doc.FilePath,
                        FileSize = doc.FileSize,
                        ContentType = doc.ContentType
                    };

                    var isSuccess = await _rabbitMQService.PublishDocumentUploadedAsync(uploadEvent);

                    // Mark as Failed if publish unsuccessful
                    if (!isSuccess)
                    {
                        doc.Status = DocumentStatus.Failed;
                        failedDocuments.Add(doc.Id);
                        _logger.LogError("Failed to publish DocumentUploadedEvent for DocumentId: {DocumentId}", doc.Id);
                    }
                }

                // Save failed status changes
                if(failedDocuments.Count != 0)
                    await _context.SaveChangesAsync();
                
            }
            catch (Exception ex)
            {
                // Don't throw - chat already created successfully
                _logger.LogError(ex, "Failed to send messages to RabbitMQ. Documents are stuck in Pending.");
            }
        }

        private void CleanupFiles(List<string> filePaths)
        {
            // Delete files during rollback
            foreach (var path in filePaths)
            {
                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch (Exception ex)
                {
                    // Just log, don't crash. We can't do much else.
                    _logger.LogWarning(ex, $"Failed to delete file during rollback: {path}");
                }
            }
        }
    }
}
