using DocAssistant.Gateway.Common;
using DocAssistant.Gateway.Common.Enums;
using DocAssistant.Gateway.Data;
using DocAssistant.Gateway.Data.Models;
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
        ISendEndpointProvider endpointProvider) : IChatService
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
             * 1. SAVE FILES FIRST (No Transaction)
             *    - Save files to uploaded_files folder
             *    - Store file paths in memory
             *    - If file save fails, cleanup and return error

             * 2. DATABASE TRANSACTION (Fast Operations Only)
             *    - Create document records (status: Pending)
             *    - Commit transaction

             * 3. PUBLISH TO RABBITMQ (After Commit)
             *    - Send documents as batch or individually
             *    - Use Outbox Pattern for reliability (optional but recommended)

             * 4. CONSUMER PROCESSES (Separate Service)
             *    - AI Service processes files
             *    - Sends result back via RabbitMQ
             *    - DocumentResultConsumer updates status
             *    - Enable retry mechanism with exponential backoff

             * 5. CLEANUP ON FAILURE
             *    - If transaction fails: delete uploaded files
             *    - If RabbitMQ fails: log error, documents stay in Pending
             *    - Background job can retry pending documents
             */

            // Validate chat exists before processing files
            var chatExists = await chatRepository.ExistsByIdAsync(chatId);
            if (!chatExists)
                throw new InvalidOperationException($"Chat with ID {chatId} not found");

            var uploadsFolder = FolderPath.GetUploadsFolder();

            var documentsToProcess = new List<Document>();
            var savedFilePaths = new List<string>();

            try
            {
                // Save files
                var fileMetadata = new List<(IFormFile file, string filePath)>(files.Count);

                foreach (var file in files)
                {
                    var safeFileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
                    var filePath = Path.Combine(uploadsFolder, safeFileName);

                    // Using larger buffer for better performance
                    await using (var stream = new FileStream(
                        filePath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: 81920,
                        useAsync: true))
                    {
                        await file.CopyToAsync(stream);
                    }

                    fileMetadata.Add((file, filePath));
                    savedFilePaths.Add(filePath);
                }

                using var transaction = await context.Database.BeginTransactionAsync();
                try
                {
                    // Process database operations in batch
                    
                    foreach (var (file, filePath) in fileMetadata)
                    {
                        // TODO: Fix unnecessary mapping
                        var document = await documentRepository.CreateAsync(new CreateDocumentDto
                        {
                            ChatId = chatId,
                            File = file,
                            FilePath = filePath
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

                    // Re-throw to outer catch for file cleanup
                    throw;
                }

                // Send to RabbitMQ after successful database operations
                await SendDocumentAsync(documentsToProcess);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to upload files for chat {ChatId}", chatId);

                if (ex is not RabbitMQException)
                    // Cleanup uploaded files on any failure
                    CleanupFiles(savedFilePaths);

                throw;
            }
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
                    // Just log, don't crash.
                    logger.LogWarning(ex, $"Failed to delete file during rollback: {path}");
                }
            }
        }
    }
}
