using DocAssistant.Gateway.Common.Enums;
using DocAssistant.Gateway.Data.Models;
using DocAssistant.Gateway.Dtos.Events;
using DocAssistant.Gateway.Hubs;
using DocAssistant.Gateway.Repositories;
using MassTransit;
using Microsoft.AspNetCore.SignalR;

namespace DocAssistant.Gateway.Consumers
{
    public class ChatMessageResponseConsumer(
        IChatMessageRepository chatMessageRepository,
        IHubContext<ChatHub> hubContext,
        ILogger<ChatMessageResponseConsumer> logger) : IConsumer<ChatMessageResponseEvent>
    {
        public async Task Consume(ConsumeContext<ChatMessageResponseEvent> context)
        {
            var response = context.Message;

            logger.LogInformation(
                "Received AI response for message {MessageId} in chat {ChatId} with confidence {ConfidenceScore} [CorrelationId: {CorrelationId}]",
                response.MessageId,
                response.ChatId,
                response.ConfidenceScore,
                response.CorrelationId);

            try
            {
                if (!response.IsSuccess)
                {
                    logger.LogWarning(
                        "AI service reported error for message {MessageId} in chat {ChatId}: {Error}",
                        response.MessageId,
                        response.ChatId,
                        response.ErrorMessage);

                    // Notify user about AI failure in real-time
                    await SendMessageToClientAsync(response.ChatId, new
                    {
                        ChatId = response.ChatId,
                        Content = "Sorry, an error occurred while processing your question. Please try again.",
                        Role = MessageRole.Assistant,
                        IsError = true,
                        Timestamp = DateTime.UtcNow
                    });
                    return;
                }

                // Prevent duplicate AI responses on retry
                var alreadyExists = await chatMessageRepository.ExistsResponseForMessageAsync(
                    response.ChatId, response.Timestamp);

                if (alreadyExists)
                {
                    logger.LogWarning(
                        "AI response for message {MessageId} in chat {ChatId} already exists. Skipping duplicate.",
                        response.MessageId,
                        response.ChatId);
                    return;
                }

                // Save AI response to database
                var aiMessage = new ChatMessage
                {
                    ChatId = response.ChatId,
                    Role = MessageRole.Assistant,
                    Content = response.Response,
                    Timestamp = response.Timestamp
                };

                await chatMessageRepository.CreateAsync(aiMessage);

                logger.LogInformation(
                    "Saved AI response {ResponseId} for chat {ChatId}",
                    aiMessage.Id,
                    response.ChatId);

                // Push AI response to connected clients in real-time
                await SendMessageToClientAsync(response.ChatId, new
                {
                    MessageId = aiMessage.Id,
                    ChatId = response.ChatId,
                    Content = aiMessage.Content,
                    Role = MessageRole.Assistant,
                    ConfidenceScore = response.ConfidenceScore,
                    Timestamp = aiMessage.Timestamp
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to process AI response for message {MessageId} in chat {ChatId} [CorrelationId: {CorrelationId}]",
                    response.MessageId,
                    response.ChatId,
                    response.CorrelationId);

                // Let MassTransit retry mechanism handle it
                throw;
            }
        }
        // Best-effort SignalR notification — never crashes the consumer
        private async Task SendMessageToClientAsync(Guid chatId, object payload)
        {
            try
            {
                await hubContext.Clients.Group(chatId.ToString())
                    .SendAsync("ReceiveMessage", payload);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send SignalR notification for chat {ChatId}", chatId);
            }
        }
    }
}
