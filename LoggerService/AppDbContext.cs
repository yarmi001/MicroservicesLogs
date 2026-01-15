using Common;
using Microsoft.EntityFrameworkCore;

namespace LoggerService;

public class AppDbContext : DbContext
{
    public DbSet<LogEntry> Logs { get; set; }

    // Конструктор для DI
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
}