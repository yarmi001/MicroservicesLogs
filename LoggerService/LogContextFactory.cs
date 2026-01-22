using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LoggerService;

public class LogContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=logs_db;Username=postgres;Password=admin");

        return new AppDbContext(optionsBuilder.Options);
    }
}