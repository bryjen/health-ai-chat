using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using Pgvector.EntityFrameworkCore;
using WebApi.Configuration;
using WebApi.Data;
using WebApi.Models;

Directory.SetCurrentDirectory(AppContext.BaseDirectory);

var builder = WebApplication.CreateBuilder(args);
builder.Environment.EnvironmentName = "Development";

// Get connection string
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.WriteLine("Error: Connection string 'DefaultConnection' not found in configuration.");
    Environment.Exit(1);
}

// Create DbContext directly with connection string to avoid service provider lifecycle issues
// Add connection string parameters to prevent premature connection closure
var connectionStringBuilder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString)
{
    // Ensure connection stays open long enough
    CommandTimeout = 60,
    // Disable connection pooling for seeding to avoid disposal issues
    Pooling = false
};

var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql(connectionStringBuilder.ConnectionString, npgsqlOptions =>
    {
        npgsqlOptions.UseVector(); // Required for Vector type mapping
    });
    // Disable retry for seeding to avoid connection disposal issues

using var dbContext = new AppDbContext(optionsBuilder.Options);

// Check if we should just drop tables (for reset script)
if (args.Length > 0 && args[0] == "--drop-tables")
{
    try
    {
        Console.WriteLine("Dropping all tables in conuhacks schema...");
        var dropTablesSql = @"
DO $$ 
DECLARE
    r RECORD;
BEGIN
    FOR r IN (SELECT tablename FROM pg_tables WHERE schemaname = 'conuhacks') 
    LOOP
        EXECUTE 'DROP TABLE IF EXISTS conuhacks.' || quote_ident(r.tablename) || ' CASCADE';
    END LOOP;
END;
$$;
DROP TABLE IF EXISTS ""__EFMigrationsHistory"" CASCADE;";
        
        await dbContext.Database.ExecuteSqlRawAsync(dropTablesSql);
        Console.WriteLine("Successfully dropped all tables.");
        await dbContext.DisposeAsync();
        return 0;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
        await dbContext.DisposeAsync();
        return 1;
    }
}

Console.WriteLine("=== Database Seeding ===\n");

#region Sample Users

var user1 = new User
{
    Id = Guid.NewGuid(),
    Email = "alice@example.com",
    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!", workFactor: 12),
    Provider = AuthProvider.Local,
    ProviderUserId = null,
    CreatedAt = DateTime.UtcNow.AddDays(-30),
    UpdatedAt = DateTime.UtcNow.AddDays(-30)
};

var user2 = new User
{
    Id = Guid.NewGuid(),
    Email = "bob@example.com",
    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!", workFactor: 12),
    Provider = AuthProvider.Local,
    ProviderUserId = null,
    CreatedAt = DateTime.UtcNow.AddDays(-20),
    UpdatedAt = DateTime.UtcNow.AddDays(-20)
};

var user3 = new User
{
    Id = Guid.NewGuid(),
    Email = "charlie@example.com",
    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!", workFactor: 12),
    Provider = AuthProvider.Local,
    ProviderUserId = null,
    CreatedAt = DateTime.UtcNow.AddDays(-10),
    UpdatedAt = DateTime.UtcNow.AddDays(-10)
};

#endregion

// Use simple transaction without retry strategy
using var transaction = await dbContext.Database.BeginTransactionAsync();
try
{
    Console.WriteLine("[1/2] Adding and saving users...");
    dbContext.Users.AddRange(user1, user2, user3);
    await dbContext.SaveChangesAsync();

    await transaction.CommitAsync();
    Console.WriteLine("[2/2] All data saved successfully");
}
catch
{
    await transaction.RollbackAsync();
    throw;
}

Console.WriteLine("Verifying data...");
var userCount = await dbContext.Users.CountAsync();

Console.WriteLine($"\n✓ Successfully seeded database!");
Console.WriteLine($"  - Users: {userCount}");

Console.WriteLine($"\nSample Login Credentials:");
Console.WriteLine($"  alice@example.com   | Password123!");
Console.WriteLine($"  bob@example.com     | Password123!");
Console.WriteLine($"  charlie@example.com | Password123!");

Console.WriteLine("\nHello, World!");

// Explicitly dispose the context
await dbContext.DisposeAsync();
return 0;
