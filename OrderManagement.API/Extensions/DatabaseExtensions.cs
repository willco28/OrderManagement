using Microsoft.EntityFrameworkCore;
using OrderManagement.Infrastructure.Data;

namespace OrderManagement.API.Extensions
{
    public static class DatabaseExtensions
    {
        public static async Task ApplySchemaAsync(this WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

            var schemaPath = Path.Combine(AppContext.BaseDirectory, "schema.sql");
            if (!File.Exists(schemaPath))
            {
                logger.LogWarning("schema.sql not found at {Path}. Skipping schema apply.", schemaPath);
                return;
            }

            var sql = await File.ReadAllTextAsync(schemaPath);
            logger.LogInformation("Applying schema from schema.sql...");

            try
            {
                await db.Database.ExecuteSqlRawAsync(sql);
                logger.LogInformation("Schema applied successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to apply schema.");
                throw;
            }
        }
    }
}
