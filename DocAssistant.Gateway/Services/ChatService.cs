using DocAssistant.Gateway.Common;
using DocAssistant.Gateway.Common.Enums;
using DocAssistant.Gateway.Data;
using DocAssistant.Gateway.Data.Models;
using DocAssistant.Gateway.Dtos.Chat;
using DocAssistant.Gateway.Dtos.Events;
using DocAssistant.Gateway.Mappers;
using DocAssistant.Gateway.Repositories;
using MassTransit;

namespace DocAssistant.Gateway.Services
{
    public class ChatService(
        AppDbContext context,
        IChatRepository chatRepository,
        IDocumentRepository documentRepository,
        IChatDocumentRepository chatDocumentRepository,
        ILogger<ChatService> logger,
        ISendEndpointProvider endpointProvider) : IChatService
    {
        public async Task<Chat> CreateChatAsync(CreateChatRequest chatRequest)
        {
            // Get upload directory path
            var uploadsFolder = FolderPath.GetUploadsFolder();

            // Track files for cleanup on failure
            var createdFilePaths = new List<string>();
            var documentsToProcess = new List<Document>();


            // Start database transaction for atomic operations
            // TODO: Start transaction in repository layer instead?
            using var transaction = await context.Database.BeginTransactionAsync();

            try
            {
                // Create new chat session
                var chat = await chatRepository.CreateAsync(chatRequest, isTransaction:true);

                // Process each uploaded file
                foreach (var file in chatRequest.Files)
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
                        var document = await documentRepository.CreateAsync(file, filePath, isTransaction:true);

                        // Link document to chat
                        await chatDocumentRepository.CreateAsync(new ChatDocument
                        {
                            ChatId = chat.Id,
                            Document = document
                        },
                        isTransaction:true);

                        // Queue for RabbitMQ notification
                        documentsToProcess.Add(document);
                    }
                }

                // Commit all database changes
                await context.SaveChangesAsync();
                await transaction.CommitAsync();

                // Send to RabbitMQ after successful commit
                await SendDocumentAsync(documentsToProcess);

                return chat;
            }
            catch (Exception ex)
            {
                // Rollback database changes
                await transaction.RollbackAsync();
                logger.LogError(ex, "Transaction failed in CreateChatWithDocumentsAsync. Rolling back.");

                // Delete files from disk
                CleanupFiles(createdFilePaths);

                throw;
            }
        }

        public async Task DeleteChatAsync(Guid chatId)
        {
            // TODO: Delete chat with its entities and send message to RabbitMQ
            // for deleting it on AI service too.
        }

        private async Task SendDocumentAsync(List<Document> documents)
        {
            try
            {
                var endpoint = await endpointProvider.GetSendEndpoint(new Uri("queue:documents.uploaded"));
                
                // Publish each document to RabbitMQ
                foreach (var doc in documents)
                {
                    //TODO: Manage this fire-and-forget better(?)
                    await endpoint.Send(doc.ToUploadedEventFromModel());
                }
            }
            catch (Exception ex)
            {
                // Don't throw - chat already created successfully
                logger.LogError(ex, "Failed to send messages to RabbitMQ. Documents are stuck in Pending.");
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
