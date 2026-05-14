using DocAssistant.Gateway.Common;
using DocAssistant.Gateway.Common.Enums;
using DocAssistant.Gateway.Data;
using DocAssistant.Gateway.Data.Models;
using DocAssistant.Gateway.Dtos.ChatMessage;
using DocAssistant.Gateway.Dtos.Document;
using DocAssistant.Gateway.Dtos.Events;
using DocAssistant.Gateway.Exceptions;
using DocAssistant.Gateway.Mappers;
using DocAssistant.Gateway.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace DocAssistant.Gateway.Services
{
    public class ChatService(
        AppDbContext context,
        IChatRepository chatRepository,
        IChatMessageRepository chatMessageRepository,
        IDocumentRepository documentRepository,
        ILogger<ChatService> logger,
        ISendEndpointProvider endpointProvider,
        IMinIOService minioService) : IChatService
    {
        public async Task<Chat> CreateChatAsync(string chatName)
        {
            return await chatRepository.CreateAsync(chatName);
        }

        public async Task<Chat?> GetByIdAsync(Guid chatId)
        {
            return await chatRepository.GetByIdAsync(chatId);
        }

        public async Task<List<Chat>> GetAllAsync()
        {
            return await chatRepository.GetAllAsync();
        }

        public async Task UploadFilesAsync(Guid chatId, List<IFormFile> files)
        {
            /**
             * Scenario:
             * 1. VALIDATE CHAT EXISTS
             *    - Check if chat exists before processing files

             * 2. UPLOAD FILES TO MINIO (No Transaction)
             *    - Upload files to MinIO object storage
             *    - Store object names in memory
             *    - If upload fails, cleanup uploaded objects and return error

             * 3. DATABASE TRANSACTION (Fast Operations Only)
             *    - Create document records (status: Pending)
             *    - Commit transaction

             * 4. PUBLISH TO RABBITMQ (After Commit)
             *    - Send documents as batch or individually
             *    - Use Outbox Pattern for reliability (optional but recommended)

             * 5. CONSUMER PROCESSES (Separate Service)
             *    - AI Service downloads files from MinIO using object path
             *    - Processes files and sends result back via RabbitMQ
             *    - DocumentResultConsumer updates status
             *    - Enable retry mechanism with exponential backoff

             * 6. CLEANUP ON FAILURE
             *    - If transaction fails: delete uploaded MinIO objects
             *    - If RabbitMQ fails: log error, documents stay in Pending
             *    - Background job can retry pending documents
             */

            // Validate chat exists before processing files
            var chatExists = await chatRepository.ExistsByIdAsync(chatId);
            if (!chatExists)
                throw new InvalidOperationException($"Chat with ID {chatId} not found");

            var documentsToProcess = new List<Document>();
            var uploadedObjectNames = new List<string>();

            try
            {
                // Upload files to MinIO
                var fileMetadata = new List<(IFormFile file, string objectPath)>(files.Count);

                foreach (var file in files)
                {
                    var objectName = $"{chatId}/{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
                    var objectPath = await minioService.UploadFileAsync(file, objectName);

                    fileMetadata.Add((file, objectPath));
                    uploadedObjectNames.Add(objectName);
                }

                using var transaction = await context.Database.BeginTransactionAsync();
                try
                {
                    // Process database operations in batch
                    
                    foreach (var (file, objectPath) in fileMetadata)
                    {
                        // TODO: Fix unnecessary mapping (map to DocumentUploadedEvent directly)
                        var document = await documentRepository.CreateAsync(new CreateDocumentDto
                        {
                            ChatId = chatId,
                            File = file,
                            FilePath = objectPath
                        }, isTransaction: true);

                        documentsToProcess.Add(document);
                    }

                    // Single SaveChanges call
                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch (Exception ex)
                {
                    // Rollback database transaction
                    await transaction.RollbackAsync();
                    logger.LogError(ex, "Database transaction failed for chat {ChatId}. Rolling back.", chatId);

                    // Re-throw to outer catch for MinIO cleanup
                    throw;
                }

                // Send to RabbitMQ after successful database operations
                await SendDocumentAsync(documentsToProcess);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to upload files for chat {ChatId}", chatId);

                if (ex is not RabbitMQException)
                    // Cleanup uploaded files from MinIO on any failure
                    await CleanupMinioFilesAsync(uploadedObjectNames);

                throw;
            }
        }

        public async Task<ChatMessageResponse> PostMessageAsync(Guid chatId, ChatMessageRequest request)
        {
            /**
             * Optimized Flow with Immediate Compensation:
             * 1. Validate chat exists AND all documents completed (single query)
             * 2. Save user message to database (fast transaction)
             * 3. Send to AI service via RabbitMQ (outside transaction)
             * 4. If RabbitMQ fails: DELETE the message from database (compensation)
             * 
             * Benefits:
             * - Transaction held for <10ms (no network I/O)
             * - Better scalability
             * - No database locks during RabbitMQ call
             * - Maintains consistency: no orphaned messages
             * 
             * Trade-off:
             * - Message is lost if RabbitMQ fails (vs. retrying later)
             * - User needs to resend the message
             */

            // 1. Combined validation in single database query (via repository)
            var validation = await chatRepository.ValidateChatAndDocumentsAsync(chatId);

            if (!validation.ChatExists)
                throw new InvalidOperationException($"Chat with ID {chatId} not found");

            if (!validation.AllDocumentsCompleted)
                throw new InvalidOperationException(
                    "Cannot send message: some documents are still being processed. Please wait until all documents are ready.");

            // 2. Create message entity
            var userMessage = request.ToModelFromRequest(chatId, MessageRole.User);

            // 3. Save to database in fast transaction (no network I/O)
            using var transaction = await context.Database.BeginTransactionAsync();
            
            try
            {
                await chatMessageRepository.CreateAsync(userMessage);
                await transaction.CommitAsync();

                logger.LogInformation(
                    "User message {MessageId} saved to database for chat {ChatId}", 
                    userMessage.Id, chatId);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                
                logger.LogError(ex, 
                    "Failed to save chat message to database for chat {ChatId}. Transaction rolled back.", 
                    chatId);
                
                throw;
            }

            // 4. Send to AI service via RabbitMQ (outside transaction)
            try
            {
                await SendChatMessageToAIAsync(chatId, userMessage);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, 
                    "Failed to send message to AI service. MessageId: {MessageId}. Performing compensation: deleting message from database.", 
                    userMessage.Id);
                
                // Immediate Compensation: Delete the orphaned message from database
                try
                {
                    await chatMessageRepository.DeleteAsync(userMessage.Id);
                    
                    logger.LogInformation(
                        "Compensation completed: Deleted message {MessageId} from database after RabbitMQ failure.", 
                        userMessage.Id);
                }
                catch (Exception deleteEx)
                {
                    logger.LogCritical(deleteEx, 
                        "CRITICAL: Failed to delete message {MessageId} during compensation. Manual cleanup required!", 
                        userMessage.Id);
                }
                
                throw new RabbitMQException(
                    "Failed to send message to AI service. Your message could not be processed. Please try again.", 
                    ex);
            }

            // 5. Return immediately - frontend will receive actual response via SignalR
            return new ChatMessageResponse
            {
                MessageId = userMessage.Id,
                ChatId = chatId,
                Content = "Your message is being processed...",
                Role = MessageRole.Assistant,
                ConfidenceScore = null,
                Timestamp = userMessage.Timestamp  // Use message timestamp, not DateTime.UtcNow
            };
        }

        public async Task DeleteChatAsync(Guid chatId)
        {
            // 1. Load document file paths before cascade delete removes them
            var documentFilePaths = await context.Documents
                .Where(d => d.ChatId == chatId)
                .Select(d => d.FilePath)
                .ToListAsync();

            // 2. Delete chat from database (cascade deletes Messages + Documents)
            await chatRepository.DeleteAsync(chatId);

            // 3. Cleanup MinIO files (best-effort, don't fail the operation)
            if (documentFilePaths.Count > 0)
            {
                await CleanupMinioFilesAsync(documentFilePaths);
            }

            // 4. Notify AI Service to delete Qdrant collection (fire-and-forget)
            await SendChatDeletedEventAsync(chatId);
        }

        private async Task SendDocumentAsync(List<Document> documents)
        {
            try
            {
                var endpoint = await endpointProvider.GetSendEndpoint(new Uri($"queue:{QueueNames.uploadFileQueue}"));

                // Publish each document to RabbitMQ
                var tasks = documents.Select(doc =>
                    endpoint.Send(doc.ToUploadedEventFromModel())
                );

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send messages to RabbitMQ. Documents are stuck in Pending.");
                // TODO: Consider implementing a background job to retry pending documents

                throw new RabbitMQException("Failed to send messages to RabbitMQ", ex);
            }
        }

        private async Task CleanupMinioFilesAsync(List<string> objectNames)
        {
            // Delete files from MinIO during rollback
            if (objectNames.Count == 0)
                return;

            try
            {
                await minioService.DeleteFilesAsync(objectNames);
            }
            catch (Exception ex)
            {
                // Just log, don't crash.
                logger.LogWarning(ex, "Failed to cleanup {Count} files from MinIO during rollback", objectNames.Count);
            }
        }

        private async Task SendChatMessageToAIAsync(Guid chatId, ChatMessage userMessage)
        {
            try
            {
                var endpoint = await endpointProvider.GetSendEndpoint(new Uri($"queue:{QueueNames.chatMessageQueue}"));

                var messageEvent = new ChatMessageSentEvent
                {
                    ChatId = chatId,
                    MessageId = userMessage.Id,
                    Content = userMessage.Content,
                    Timestamp = userMessage.Timestamp,
                    CorrelationId = Guid.NewGuid()
                };

                await endpoint.Send(messageEvent);

                logger.LogInformation("Sent chat message {MessageId} to AI service for chat {ChatId} [CorrelationId: {CorrelationId}]", 
                    userMessage.Id, chatId, messageEvent.CorrelationId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to publish chat message {MessageId} to RabbitMQ", userMessage.Id);
                throw;
            }
        }

        private async Task SendChatDeletedEventAsync(Guid chatId)
        {
            try
            {
                var endpoint = await endpointProvider.GetSendEndpoint(new Uri($"queue:{QueueNames.chatDeletedQueue}"));

                var deletedEvent = new ChatDeletedEvent
                {
                    ChatId = chatId,
                    CorrelationId = Guid.NewGuid()
                };

                await endpoint.Send(deletedEvent);

                logger.LogInformation("Sent ChatDeletedEvent for chat {ChatId} [CorrelationId: {CorrelationId}]",
                    chatId, deletedEvent.CorrelationId);
            }
            catch (Exception ex)
            {
                // Log but don't throw — chat is already deleted from DB
                logger.LogWarning(ex, "Failed to send ChatDeletedEvent for chat {ChatId}. AI Service cleanup may be needed manually.", chatId);
            }
        }
    }
}
