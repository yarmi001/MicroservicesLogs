using System.Text;
using System.Text.Json;
using Common;             // Тут лежит LogEntry и ErrorSender
using LoggerService;      // Тут лежит AppDbContext
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

Console.WriteLine("--- Logger Service Starting ---");

// 1. Инициализация БД и создание таблицы
using (var db = new AppDbContext())
{
    bool dbConnected = false;
    while (!dbConnected)
    {
        try
        {
            await db.Database.EnsureCreatedAsync();
            dbConnected = true;
            Console.WriteLine("--> БД подключена и инициализирована.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"--> Ожидание БД: {ex.Message}");
            await Task.Delay(3000);
        }
    }
}

// 2. Подключение к RabbitMQ
var hostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";
var factory = new ConnectionFactory { HostName = hostName };
IConnection connection = null;

while (connection == null)
{
    try
    {
        connection = await factory.CreateConnectionAsync();
        Console.WriteLine("--> RabbitMQ подключен.");
    }
    catch
    {
        Console.WriteLine("--> Ожидание RabbitMQ...");
        await Task.Delay(3000);
    }
}

using var channel = await connection.CreateChannelAsync();

// Объявляем очередь (на всякий случай)
await channel.QueueDeclareAsync("error_queue", durable: true, exclusive: false, autoDelete: false);

Console.WriteLine(" [*] Ожидание сообщений об ошибках...");

// 3. Настройка потребителя
var consumer = new AsyncEventingBasicConsumer(channel);

consumer.ReceivedAsync += async (model, ea) =>
{
    try 
    {
        var body = ea.Body.ToArray();
        var json = Encoding.UTF8.GetString(body);
        
        // Десериализация
        var logEntry = JsonSerializer.Deserialize<LogEntry>(json);

        if (logEntry != null)
        {
            // Сохранение в БД
            using var db = new AppDbContext();
            await db.Logs.AddAsync(logEntry);
            await db.SaveChangesAsync();
            
            Console.WriteLine($"[SAVED] Ошибка от {logEntry.ServiceName}: {logEntry.ErrorMessage}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] Не удалось сохранить лог: {ex.Message}");
    }
}; 

// 4. Запуск прослушивания
await channel.BasicConsumeAsync("error_queue", autoAck: true, consumer: consumer);

// Вечное ожидание, чтобы контейнер не закрылся
await Task.Delay(Timeout.Infinite);