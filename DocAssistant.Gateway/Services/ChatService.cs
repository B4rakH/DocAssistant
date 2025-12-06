using DocAssistant.Gateway.Common;
using DocAssistant.Gateway.Common.Enums;
using DocAssistant.Gateway.Data;
using DocAssistant.Gateway.Data.Models;
using DocAssistant.Gateway.Dtos.Chat;

namespace DocAssistant.Gateway.Services
{
    public class ChatService : IChatService
    {
        private readonly AppDbContext _context;
        //TODO: Create an IMessageProducer interface and its implementation for RabbitMQ
        //private readonly IMessageProducer _messageProducer;
        private readonly ILogger<ChatService> _logger;

       public ChatService(AppDbContext context,
           //IMessageProducer messageProducer,
           ILogger<ChatService> logger)
        {
            _context = context;
            //_messageProducer = messageProducer;
            _logger = logger;
        }

        public async Task<Chat> CreateChatWithDocumentsAsync(CreateChatRequest request)
        {
            // 1. Preparation
            var uploadsFolder = FolderPath.GetUploadsFolder();

            // Lists to track state for Rollback or Notification
            var createdFilePaths = new List<string>();
            var documentsToProcess = new List<Document>();

            // 2. Begin Transaction (Atomic Operation)
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // A. Create the Chat Session
                var chat = new Chat
                {
                    Name = request.Name
                };

                _context.Chats.Add(chat);
                await _context.SaveChangesAsync();

                // B. Loop through Files
                foreach (var file in request.Files)
                {
                    if (file.Length > 0)
                    {
                        // --- Disk Operation ---

                        //Saving files to the upload folder
                        var safeFileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
                        var filePath = Path.Combine(uploadsFolder, safeFileName);

                        using (var stream = new FileStream(filePath, FileMode.Create)) await file.CopyToAsync(stream);

                        // Add to list immediately (so we can delete it if error occurs later)
                        createdFilePaths.Add(filePath);

                        // --- Database Operation ---
                        var document = new Document
                        {
                            FileName = file.FileName,
                            FilePath = filePath,
                            FileSize = file.Length,
                            ContentType = file.ContentType,
                            Status = DocumentStatus.Pending
                        };

                        _context.Documents.Add(document);

                        // Link Document to Chat (Many-to-Many)
                        _context.ChatDocuments.Add(new ChatDocument
                        {
                            ChatId = chat.Id,
                            Document = document
                        });

                        // Add to list for RabbitMQ
                        documentsToProcess.Add(document);
                    }
                }

                // C. Commit Everything
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                // 3. Notify RabbitMQ (Safe Zone)
                // Performing this AFTER commit. Despite any fails, the data will be already safe in DB.
                NotifyPythonWorker(documentsToProcess);

                return chat;
            }
            catch (Exception ex)
            {
                // 4. Rollback Strategy
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Transaction failed in CreateChatWithDocumentsAsync. Rolling back.");

                // Delete the "Zombie Files" from disk
                CleanupFiles(createdFilePaths);

                throw; // Re-throw to let the Controller know it failed
            }
        }
        private void NotifyPythonWorker(List<Document> documents)
        {
            try
            {
                foreach (var doc in documents)
                {
                    // Sending only what Python needs
                    // TODO: [Technical Debt] We are using an anonymous object here. 
                    // Better approach: Create a shared 'DocumentUploadedEvent' class in DTOs.
                    // Example: _messageProducer.SendMessage(new DocumentUploadedEvent(doc.Id, doc.FilePath));

                    // TODO: [Implementation Required] The 'RabbitMQProducer' class is not written yet.
                    // This line will throw a NullReference or Dependency Injection error until we create Services/RabbitMQProducer.cs


                    //_messageProducer.SendMessage(new
                    //{
                    //    Id = doc.Id,
                    //    FilePath = doc.FilePath
                    //});

                }
            }
            catch (Exception ex)
            {
                // We log this but do NOT throw. 
                // The Chat is already created successfully. We don't want to show "Error" to user 
                // just because the Queue is temporarily down.
                _logger.LogError(ex, "Failed to send messages to RabbitMQ. Documents are stuck in Pending.");
            }
        }

        private void CleanupFiles(List<string> filePaths)
        {
            foreach (var path in filePaths)
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
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
