using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LoggerService;

// Этот класс используется ТОЛЬКО командой "dotnet ef migrations"
// Он позволяет создавать миграции без запуска основного приложения и Docker
public class LogContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        
        // Используем временную строку подключения.
        // Она нужна только чтобы EF Core понял структуру БД (PostgreSQL).
        // Реальное подключение к базе данных здесь НЕ происходит.
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=logs_db;Username=postgres;Password=admin");

        return new AppDbContext(optionsBuilder.Options);
    }
}