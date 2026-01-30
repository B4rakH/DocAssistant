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

namespace DocAssistant.Gateway.Services
{
    public class ChatService(
        AppDbContext context,
        IChatRepository chatRepository,
        IDocumentRepository documentRepository,
        ILogger<ChatService> logger,
        ISendEndpointProvider endpointProvider,
        IMinioService minioService) : IChatService
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

        public async Task<ChatMessageResponse> PostMessageAsync(Guid chatId, string message)
        {
            return null;
        }


        public async Task DeleteChatAsync(Guid chatId)
        {
            // TODO: Delete chat with its entities and send message to RabbitMQ
            // for deleting it on AI service too.

            /**
             * Scenario:
             * User requests chat deletion
             * Transaction starts
             * Delete chat from database
             * Delete uncascaded entities
             * Send message to RabbitMQ for AI service to delete chat data
             * TODO: waiting for confirmation from AI service?
             * End transaction and save changes
             * Return Ok
             */

            await chatRepository.DeleteAsync(chatId);

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
    }
}
