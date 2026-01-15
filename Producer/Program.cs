using System.Text;
using Common; // Убедись, что библиотека Common обновлена (содержит RabbitMqLogger и LogType)
using RabbitMQ.Client;

Console.WriteLine("Producer Service is starting...");

// 1. Подключение к RabbitMQ с Retry Policy (простой вариант)
var hostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";
var factory = new ConnectionFactory() { HostName = hostName };

IConnection connection = null;
while (connection == null)
{
    try
    {
        connection = await factory.CreateConnectionAsync();
        Console.WriteLine("RabbitMq подключен");
    }
    catch (Exception ex)
    {
        Console.WriteLine("Ожидание подключения к RabbitMq: " + ex.Message);
        await Task.Delay(3000); // 3 секунды паузы
    }
}

using var channel = await connection.CreateChannelAsync();

// 2. Объявляем очередь РАБОЧИХ ЗАДАЧ (Work Queue)
// Сюда мы шлем задания для Consumer'а. Это НЕ логи.
await channel.QueueDeclareAsync("work_queue", false, false, false);

// 3. Инициализируем ЛОГГЕР (Log Exchange)
// Сюда мы будем слать отчеты (Info, Error, Warning)
var logger = new RabbitMqLogger(channel, "ProducerService");
await logger.InitializeAsync(); // Важно: Создает Exchange "logs_exchange"

int i = 0;

Console.WriteLine("Starting work loop...");

while (true)
{
    i++;
    try
    {
        // --- БИЗНЕС ЛОГИКА ---
        string msg = $"Work message #{i}";
        var body = Encoding.UTF8.GetBytes(msg);
        
        // Отправляем задачу в очередь работникам (Consumer)
        await channel.BasicPublishAsync("", "work_queue", false, body);
        Console.WriteLine($"[Producer] Sent task: {msg}");

        // --- ЛОГИРОВАНИЕ (INFO) ---
        // Отправляем информацию в систему логирования, что всё хорошо
        await logger.LogAsync($"Task #{i} sent successfully", LogType.Info);

        // Имитируем ошибку каждое 5-е сообщение
        if (i % 5 == 0)
        {
            throw new InvalidOperationException($"Simulated exception at message #{i}");
        }

        await Task.Delay(1000); // Пауза 1 секунда между задачами
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Producer] Caught error: {ex.Message}");
        
        // --- ЛОГИРОВАНИЕ (ERROR) ---
        // Отправляем ошибку в систему логирования
        await logger.LogAsync("Error during task processing", LogType.Error, ex);
        
        await Task.Delay(5000); // Пауза после ошибки
    }
}