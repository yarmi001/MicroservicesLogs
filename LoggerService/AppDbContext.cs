using Common;
using Microsoft.EntityFrameworkCore;

namespace LoggerService;

public class AppDbContext : DbContext
{
    public DbSet<LogEntry> Logs { get; set; } = null!; // DbSet для хранения логов ошибок
    
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING") ?? "Host=localhost;Database=logs_db;Username=postgres;Password=admin";// Получение строки подключения из переменных окружения или использование значения по умолчанию
        optionsBuilder.UseNpgsql(connectionString);// Настройка контекста данных для использования PostgreSQL
    }
}