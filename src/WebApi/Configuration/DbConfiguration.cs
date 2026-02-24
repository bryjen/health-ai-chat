using Microsoft.EntityFrameworkCore;
using WebApi.Configuration.Options;
using WebApi.Data;
using WebApi.Models;

namespace WebApi.Configuration;

public static class DbConfiguration
{
    /// <summary>
    /// Configures the application's database provider.
    /// </summary>
    /// <remarks>
    /// Falls back to an in-memory EF Core configuration in the case of an empty connection string, OR if connecting
    /// to the specified URL fails.
    /// </remarks>
    public static void ConfigureDatabase(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment? environment = null)
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger("WebApi.Configuration.ServiceConfiguration");

        // skip database registration in test environment
        if (environment?.IsEnvironment("Test") ?? false)
        {
            logger.LogInformation("Environment 'Test' detected. Skipping database registration (tests will configure DbContext separately).");
            return;
        }

        // check if in-memory database is explicitly requested
        if (ShouldUseSqliteDatabase(configuration))
        {
            UseSqliteDatabase(services, logger);
            return;
        }

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogError("Connection string 'DefaultConnection' not found. Falling back to in-memory database.");
            UseSqliteDatabase(services, logger);
            return;
        }

        // try to use PostgreSQL, fall back to in-memory if connection fails
        if (TryUsePostgreSql(services, configuration, connectionString, logger))
        {
            return;
        }

        logger.LogInformation("Using in-memory database provider 'FallbackInMemoryDatabase' due to PostgreSQL connection issues.");
        UseSqliteDatabase(services, logger);
    }

    private static bool ShouldUseSqliteDatabase(IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"];
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        return string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(connectionString, "Sqlite", StringComparison.OrdinalIgnoreCase);
    }

    private static void UseSqliteDatabase(IServiceCollection services, ILogger logger)
    {
        logger.LogInformation("Using sqlite database provider.");
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite("Data Source=salus.db"));

        using var scope = services.BuildServiceProvider().CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated(); // only creates if doesn't exist
        // comment to disable db refreshing
        // SeedData.Initialize(db);     // idempotent seed
    }

    // ReSharper disable once IdentifierTypo
    private static bool TryUsePostgreSql(
        IServiceCollection services,
        IConfiguration configuration,
        string connectionString,
        ILogger logger)
    {
        try
        {
            var (maxRetryCount, maxRetryDelaySeconds) = GetRetryPolicySettings(configuration);

            // Test connection
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(connectionString, npgsqlOptions =>
                {
                    ConfigureRetryPolicy(npgsqlOptions, maxRetryCount, maxRetryDelaySeconds);
                });

            using var testContext = new AppDbContext(optionsBuilder.Options);
            if (!testContext.Database.CanConnect())
            {
                logger.LogWarning("PostgreSQL connection test returned false. Falling back to in-memory database.");
                return false;
            }

            // Connection successful - register PostgreSQL with retry policy
            logger.LogInformation(
                "Successfully connected to PostgreSQL. Using PostgreSQL database provider with retry policy (MaxRetryCount: {MaxRetryCount}, MaxRetryDelay: {MaxRetryDelay}s).",
                maxRetryCount, maxRetryDelaySeconds);

            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(connectionString, npgsqlOptions =>
                {
                    ConfigureRetryPolicy(npgsqlOptions, maxRetryCount, maxRetryDelaySeconds);
                }));

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to connect to PostgreSQL database. Error: {ErrorMessage}. Falling back to in-memory database.",
                ex.Message);
            return false;
        }
    }

    private static (int MaxRetryCount, int MaxRetryDelaySeconds) GetRetryPolicySettings(IConfiguration configuration)
    {
        var retryPolicyConfig = configuration.GetSection(DatabaseRetryPolicySettings.SectionName);
        return (
            MaxRetryCount: retryPolicyConfig.GetValue<int>("MaxRetryCount", 5),
            MaxRetryDelaySeconds: retryPolicyConfig.GetValue<int>("MaxRetryDelaySeconds", 30)
        );
    }

    private static void ConfigureRetryPolicy(
        Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder npgsqlOptions,
        int maxRetryCount,
        int maxRetryDelaySeconds)
    {
        npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: maxRetryCount,
            maxRetryDelay: TimeSpan.FromSeconds(maxRetryDelaySeconds),
            errorCodesToAdd: null);
    }
}

public static class SeedData
{
    // Hardcoded test user ID for AI state
    public static readonly Guid TestUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public static readonly Guid TestConversationId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    public static void Initialize(AppDbContext db)
    {
        // Seed test user if it doesn't exist
        if (!db.Users.Any(u => u.Id == TestUserId))
        {
            db.Users.Add(new User
            {
                Id = TestUserId,
                Email = "test@example.com",
                Provider = AuthProvider.Local,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        // Seed test conversation if it doesn't exist
        if (!db.Conversations.Any(c => c.Id == TestConversationId))
        {
            db.Conversations.Add(new Conversation
            {
                Id = TestConversationId,
                UserId = TestUserId,
                Title = "Test Conversation",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        db.SaveChanges();
    }
}
