using System.Text;
using Common;
using RabbitMQ.Client;

Console.WriteLine("Producer Service is starting...");// Вывод сообщения о запуске сервиса

var hostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";// Получение имени хоста RabbitMQ из переменных окружения или использование значения по умолчанию
var factory = new ConnectionFactory() { HostName = hostName };// Создание фабрики

IConnection connection = null; // Инициализация подключения
while (connection == null)
{
    try
    {
        connection = await factory.CreateConnectionAsync();// Попытка установить подключение к RabbitMQ
        Console.WriteLine("RabbitMq подключен");// Вывод сообщения об успешном подключении к RabbitMQ
    }
    catch (Exception ex)
    {
        Console.WriteLine("Ожидание подключения к RabbitMq: " + ex.Message);// Вывод сообщения об ошибке подключения к RabbitMQ
        await Task.Delay(5000);// Ожидание перед повторной попыткой подключения
    }
}

using var channel = await connection.CreateChannelAsync();// Создание канала для взаимодействия с RabbitMQ
await channel.QueueDeclareAsync("work_queue", false, false, false);// Объявление очереди для ошибок

var errorSender = new ErrorSender(channel, "Producer Service");// Инициализация отправителя ошибок

int i = 0;

while (true)
{
    i++;
    try
    {
        //Имитируем работу сервиса
        string msg = $"Work message #{i}"; // Создание сообщения
        var body = Encoding.UTF8.GetBytes(msg); // Преобразование сообщения в массив байтов
        await channel.BasicPublishAsync("", "work_queue", false, body); // Отправка сообщения в очередь
        Console.WriteLine($"[Producer] Sent: {msg}"); // Вывод информации об отправленном сообщении

        //Имитируем ошибку каждое пятое сообщение
        if (i % 5 == 0)
        {
            throw new InvalidOperationException($"Simulated exception at message #{i}"); // Генерация
        }
    }
    catch (Exception ex)
    {
        await errorSender.SendErrorAsync(ex); // Отправка ошибки в очередь
        await Task.Delay(5000);
    }
}