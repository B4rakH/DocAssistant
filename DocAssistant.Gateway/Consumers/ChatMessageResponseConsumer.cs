using DocAssistant.Gateway.Common.Enums;
using DocAssistant.Gateway.Data.Models;
using DocAssistant.Gateway.Dtos.Events;
using DocAssistant.Gateway.Repositories;
using MassTransit;

namespace DocAssistant.Gateway.Consumers
{
    public class ChatMessageResponseConsumer(
        IChatMessageRepository chatMessageRepository,
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

                    // TODO: Notify user via SignalR about the error
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

                // TODO: Send real-time notification via SignalR
                // await hubContext.Clients.Group(response.ChatId.ToString())
                //     .SendAsync("ReceiveMessage", new {
                //         messageId = aiMessage.Id,
                //         chatId = response.ChatId,
                //         content = aiMessage.Content,
                //         role = MessageRole.Assistant,
                //         confidenceScore = response.ConfidenceScore,
                //         timestamp = aiMessage.Timestamp
                //     });
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
        }
    }
}
