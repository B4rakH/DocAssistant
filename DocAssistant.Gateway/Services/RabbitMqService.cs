using DocAssistant.Gateway.Dtos.Events;
using DotNetEnv;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace DocAssistant.Gateway.Services;

public class RabbitMqService : IMessageProducer, IDisposable
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
        Env.Load();
        var envVariables = Environment.GetEnvironmentVariables();

        var factory = new ConnectionFactory
        {
            Uri = new(envVariables["RABBITMQ_URI"].ToString() ?? throw new Exception("RabbitMQ Uri cannot found")),
            UserName = envVariables["RABBITMQ_USERNAME"].ToString() ?? throw new Exception("Username cannot found"),
            Password = envVariables["RABBITMQ_PASSWORD"].ToString() ?? throw new Exception("Password cannot found"),
        };

        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();

        // 1. Declare main exchange
        await channel.ExchangeDeclareAsync("documents", ExchangeType.Direct, durable: true);

        // 2. Declare DLX (for failed messages)
        await channel.ExchangeDeclareAsync("my_dlx_exchange", ExchangeType.Fanout, durable: true);

        // 3. Declare DLQ (holds failed messages)
        await channel.QueueDeclareAsync(
            queue: "documents.uploaded.dlq",
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        await channel.QueueBindAsync(
            queue: "documents.uploaded.dlq",
            exchange: "my_dlx_exchange",
            routingKey: "uploaded",
            arguments: null);

        // 4. Declaring main queue WITH DLX configuration
        var uploadQueueArgs = new Dictionary<string, object?>
    {
        { "x-dead-letter-exchange", "my_dlx_exchange" },
        { "x-dead-letter-routing-key", "uploaded" }
    };

        await channel.QueueDeclareAsync(
            queue: "documents.uploaded",
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: uploadQueueArgs);

        await channel.QueueBindAsync(
            queue: "documents.uploaded",
            exchange: "documents",
            routingKey: "uploaded",
            arguments: null);

        // 5. Declare results queue (same as before)
        await channel.QueueDeclareAsync(
            queue: "documents.results",
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        await channel.QueueBindAsync(
            queue: "documents.results",
            exchange: "documents",
            routingKey: "results",
            arguments: null);

        return new RabbitMqService(connection, channel, logger);
    }
    public async Task<bool> PublishDocumentUploadedAsync(DocumentUploadedEvent message)
    {
        try
        {
            // Serialize message to JSON bytes
            var json = JsonSerializer.Serialize(message);
            var body = Encoding.UTF8.GetBytes(json);
            var properties = new BasicProperties { Persistent = true };

            // Publish to RabbitMQ exchange
            await _channel.BasicPublishAsync(
                exchange: "documents",
                routingKey: "uploaded",
                body: body,
                mandatory: false,
                basicProperties:properties);

            _logger.LogInformation("Published document upload event for DocumentId: {DocumentId}", message.DocumentId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish document upload event for DocumentId: {DocumentId}", message.DocumentId);
            return false;
        }
    }

    // Start consuming results; onMessageReceived invoked when a result arrives
    public Task StartConsumingResultsAsync(Func<DocumentProcessedEvent, Task> onMessageReceived, CancellationToken cancellationToken = default)
    {
        var consumer = new AsyncEventingBasicConsumer(_channel);

        consumer.ReceivedAsync += async (sender, args) =>
        {
            try
            {
                var json = Encoding.UTF8.GetString(args.Body.ToArray());
                var result = JsonSerializer.Deserialize<DocumentProcessedEvent>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result != null)
                {
                    // invoke user handler
                    await onMessageReceived(result);
                }

                // acknowledge
                await _channel.BasicAckAsync(args.DeliveryTag, multiple: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing documents.results message");
                // nack without requeue to avoid tight retry loops; change to requeue:true if you want retries
                await _channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false);
            }
        };

        // start consuming (manual ack)
        _channel.BasicConsumeAsync(queue: "documents.results", autoAck: false, consumer: consumer);

        return Task.CompletedTask;
    }

    // Automatically dispose resources
    public void Dispose()
    {
        // Clean up RabbitMQ resources
        try
        {
            _channel?.CloseAsync().GetAwaiter().GetResult();
            _connection?.CloseAsync().GetAwaiter().GetResult();
        }
        catch {}
        _channel?.Dispose();
        _connection?.Dispose();
    }
}
