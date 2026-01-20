using System.Text;
using System.Text.Json;
using RabbitMQ.Client;

namespace Common;

public class RabbitMqLogger
{
    private readonly IChannel _channel;
    private readonly string _serviceName;
    
    // Имя обменника, куда мы будем кидать все логи
    private const string ExchangeName = "logs_exchange";

    public RabbitMqLogger(IChannel channel, string serviceName)
    {
        _channel = channel;
        _serviceName = serviceName;
    }

    // 1. Инициализация (Создаем Exchange)
    // Этот метод обязательно вызывать при старте сервиса
    public async Task InitializeAsync()
    {
        // Создаем обменник типа TOPIC. 
        // Это позволит LoggerService подписываться по маске (например "#" или "error.*")
        await _channel.ExchangeDeclareAsync(ExchangeName, ExchangeType.Topic, durable: true);
    }

    // 2. Метод отправки лога
    public async Task LogAsync(string message, LogType type, Exception? ex = null)
    {
        try
        {
            // Формируем объект (DTO)
            var log = new LogEntry
            {
                Id = Guid.NewGuid(), // Генерируем ID здесь (чтобы валидатор не ругался)
                ServiceName = _serviceName, // Имя сервиса (Producer/Consumer)
                Message = message,
                Type = type,
                StackTrace = ex?.ToString(), // StackTrace нужен только если есть Exception
                Timestamp = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(log);
            var body = Encoding.UTF8.GetBytes(json);

            // Генерируем Routing Key для гибкой фильтрации
            // Пример: "info.ProducerService" или "error.ConsumerService"
            var routingKey = $"{type.ToString().ToLower()}.{_serviceName}";

            // Публикуем в EXCHANGE (а не в очередь напрямую)
            await _channel.BasicPublishAsync(
                exchange: ExchangeName,
                routingKey: routingKey,
                mandatory: false,
                basicProperties: new BasicProperties(),
                body: body
            );

            // Дублируем в консоль для отладки
            // Console.WriteLine($"[RabbitMqLogger] Sent: {message}");
        }
        catch (Exception e)
        {
            // Если система логирования упала, мы не должны ронять основной сервис
            Console.WriteLine($"[RabbitMqLogger] FATAL: Failed to send log! {e.Message}");
        }
    }
}