using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using DocAssistant.Gateway.Dtos.Events;
using DotNetEnv;

namespace DocAssistant.Gateway.Services;

public class RabbitMqService : IRabbitMQService, IDisposable
{
    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private readonly ILogger<RabbitMqService> _logger;

    private RabbitMqService(IConnection connection, IChannel channel, ILogger<RabbitMqService> logger)
    {
        _connection = connection;
        _channel = channel;
        _logger = logger;
    }

    public static async Task<RabbitMqService> CreateServiceAsync(ILogger<RabbitMqService> logger)
    {
        // Load RabbitMQ settings from .env file
        Env.Load();
        var envVariables = Environment.GetEnvironmentVariables();

        var factory = new ConnectionFactory
        {
            Uri = new(envVariables["RABBITMQ_URI"].ToString() ?? throw new Exception("RabbitMQ Uri cannot found")),
            UserName = envVariables["RABBITMQ_USERNAME"].ToString() ?? throw new Exception("Username cannot found"),
            Password = envVariables["RABBITMQ_PASSWORD"].ToString() ?? throw new Exception("Password cannot found"),
        };

        // Create long-lived connection and channel (reused for performance)
        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();

        // Declare durable exchange
        await channel.ExchangeDeclareAsync("documents", ExchangeType.Direct, durable: true);

        await channel.QueueDeclareAsync(
                queue: "documents.uploaded",
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null);

        await channel.QueueBindAsync(
                queue: "documents.uploaded",
                exchange: "documents",
                routingKey: "uploaded",
                arguments: null);

        return new RabbitMqService(connection, channel, logger);
    }

    public async Task PublishDocumentUploadedAsync(DocumentUploadedEvent message)
    {
        // Serialize message to JSON bytes
        var json = JsonSerializer.Serialize(message);

        var properties = new BasicProperties
        {
            Persistent = true
        };

        // Publish to RabbitMQ exchange
        await _channel.BasicPublishAsync(
            exchange: "documents",
            routingKey: "uploaded",
            body: Encoding.UTF8.GetBytes(json),
            mandatory: false);

        _logger.LogInformation("Published document upload event for DocumentId: {DocumentId}", message.DocumentId);
    }


    // Automatically dispose resources
    public void Dispose()
    {
        // Clean up RabbitMQ resources
        _channel?.CloseAsync().GetAwaiter().GetResult();
        _connection?.CloseAsync().GetAwaiter().GetResult();
        _channel?.Dispose();
        _connection?.Dispose();
    }
}
