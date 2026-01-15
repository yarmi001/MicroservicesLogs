using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace Common;

public class RabbitMqLogger
{
    private readonly IChannel _channel;
    private readonly string _serviceName;
    private const string ExchangeName = "logs_exchange"; // Используем Exchange

    public RabbitMqLogger(IChannel channel, string serviceName)
    {
        _channel = channel;
        _serviceName = serviceName;
    }

    // Инициализация (создаем обменник один раз)
    public async Task InitializeAsync()
    {
        // Тип "topic" позволяет фильтровать по ключам (напр. "serviceA.error")
        await _channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Topic, durable: true);
    }

    public async Task LogAsync(string message, LogType type, Exception? ex = null)
    {
        var log = new LogEntry
        {
            ServiceName = _serviceName,
            Message = message,
            Type = type,
            StackTrace = ex?.ToString(),
            Timestamp = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(log);
        var body = Encoding.UTF8.GetBytes(json);

        // Routing Key: например "error.ProducerService" или "info.ConsumerService"
        // Это отвечает на замечание №3 (разбиение сообщений)
        var routingKey = $"{type.ToString().ToLower()}.{_serviceName}";

        await _channel.BasicPublishAsync(
            exchange: ExchangeName,
            routingKey: routingKey, 
            mandatory: false, 
            basicProperties: new BasicProperties(), 
            body: body
        );
    }
}