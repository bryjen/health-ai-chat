using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Pgvector.EntityFrameworkCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Resend;
using WebApi.Configuration.Options;
using WebApi.Data;
using WebApi.Services.Auth;
using WebApi.Services.Auth.Validation;
using WebApi.Services.Email;
using WebApi.Services.Validation;

using static WebApi.Configuration.DatabaseConfigurationHelpers;

namespace WebApi.Configuration;

public static class ServiceConfiguration
{
    /// <summary>
    /// Resolves CORS allowed origins from configuration.
    /// If an environment variable is set, use it (comma-separated). Otherwise, fall back to `appsettings.json` array.
    /// </summary>
    internal static string[] GetCorsAllowedOrigins(IConfiguration configuration)
    {
        // Check environment variable first
        var corsOriginsString = configuration["Cors__AllowedOrigins"]
                              ?? Environment.GetEnvironmentVariable("Cors__AllowedOrigins");

        if (!string.IsNullOrWhiteSpace(corsOriginsString))
        {
            return corsOriginsString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        // Fall back to appsettings.json array
        var corsOriginsArray = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        return corsOriginsArray ?? Array.Empty<string>();
    }
    
    /// <summary>
    /// Configures Cross-Origin Resource Sharing (CORS) for the application.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">The configuration instance to read CORS settings from.</param>
    /// <returns>True if CORS was configured, false if CORS is disabled.</returns>
    /// <remarks>
    /// <para>
    /// CORS can be disabled by setting "Cors:Enabled" to false in configuration or environment variable "Cors__Enabled" to "false".
    /// When disabled, CORS services are still registered (for middleware compatibility) but no CORS policy is added,
    /// and UseCors() middleware will not be called in the pipeline.
    /// </para>
    /// <para>
    /// CORS origins are resolved from configuration in the following order:
    /// 1. Environment variable "Cors__AllowedOrigins" (comma-separated)
    /// 2. Configuration section "Cors:AllowedOrigins" (array)
    /// </para>
    /// <para>
    /// If no origins are configured, the policy allows all origins, methods, and headers (permissive mode).
    /// When specific origins are configured, the policy allows credentials and uses those origins only.
    /// </para>
    /// </remarks>
    public static bool ConfigureCors(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        // Check if CORS is disabled
        var corsEnabled = configuration.GetValue<bool>("Cors:Enabled", true);
        var corsEnabledEnv = configuration["Cors__Enabled"] ?? Environment.GetEnvironmentVariable("Cors__Enabled");
        if (!string.IsNullOrWhiteSpace(corsEnabledEnv) && bool.TryParse(corsEnabledEnv, out var parsed))
        {
            corsEnabled = parsed;
        }

        // Always register CORS services for middleware compatibility, but only add policy if enabled
        services.AddCors(options =>
        {
            if (corsEnabled)
            {
                var corsOrigins = GetCorsAllowedOrigins(configuration);
                options.AddDefaultPolicy(policy =>
                {
                    if (corsOrigins.Length == 0)
                    {
                        // Allow all origins (permissive mode) - cannot use AllowCredentials() with AllowAnyOrigin()
                        policy.AllowAnyOrigin()
                            .AllowAnyMethod()
                            .AllowAnyHeader();
                    }
                    else
                    {
                        // Specific origins - can use credentials
                        policy.WithOrigins(corsOrigins)
                            .AllowAnyMethod()
                            .AllowAnyHeader()
                            .AllowCredentials();
                    }
                });
            }
            // If CORS is disabled, no policy is added, so CORS headers won't be applied
        });
        
        return corsEnabled;
    }
    
    /// <summary>
    /// Configures OpenAPI + Swagger.
    /// </summary>
    public static void ConfigureOpenApi(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SupportNonNullableReferenceTypes();
            
            var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }
        });
    }
    
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
        if (ShouldUseInMemoryDatabase(configuration))
        {
            UseInMemoryDatabase(services, logger);
            return;
        }

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogError("Connection string 'DefaultConnection' not found. Falling back to in-memory database.");
            UseInMemoryDatabase(services, logger);
            return;
        }

        // try to use PostgreSQL, fall back to in-memory if connection fails
        if (TryUsePostgreSql(services, configuration, connectionString, logger))
        {
            return;
        }

        logger.LogInformation("Using in-memory database provider 'FallbackInMemoryDatabase' due to PostgreSQL connection issues.");
        UseInMemoryDatabase(services, logger);
    }
    
    /// <summary>
    /// Configures JWT Bearer authentication and authorization for the application.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">The configuration instance to read JWT settings from.</param>
    /// <param name="environment">The hosting environment to determine test mode behavior.</param>
    /// <remarks>
    /// <para>
    /// Reads JWT configuration from the "Jwt" section, including:
    /// - Secret: The signing key for JWT tokens (required)
    /// - Issuer: The token issuer (required)
    /// - Audience: The token audience (required)
    /// </para>
    /// <para>
    /// In test environments, if no JWT secret is configured, a default test secret is provided
    /// to allow the application to start deterministically.
    /// </para>
    /// <para>
    /// Token validation includes issuer, audience, lifetime, and signing key validation.
    /// Clock skew is set to zero for strict time validation.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when JWT Secret is not configured in non-test environments.</exception>
    public static void ConfigureJwtAuth(
        this IServiceCollection services, 
        IConfiguration configuration,
        IHostEnvironment environment)
    { 
        // Add JWT Authentication
        var jwtSettings = configuration.GetSection("Jwt");

        // Tests (WebApplicationFactory) can run before test-specific configuration overrides are applied.
        // Provide a safe default JWT secret in Test environment so the host can start deterministically.
        if (environment.IsEnvironment("Test") && string.IsNullOrWhiteSpace(jwtSettings["Secret"]))
        {
            configuration["Jwt:Secret"] = "TestJwtSecret_ForLocalUnitTests_ChangeMe_1234567890";
            jwtSettings = configuration.GetSection("Jwt");
        }
        var secretKey = jwtSettings["Secret"] ?? throw new InvalidOperationException("JWT Secret not configured");

        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtSettings["Issuer"],
                    ValidAudience = jwtSettings["Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
                    ClockSkew = TimeSpan.Zero
                };
            });

        services.AddAuthorization();
    }
    
    /// <summary>
    /// Configures OpenTelemetry for distributed tracing, metrics, and logging.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">The configuration instance to read OpenTelemetry settings from.</param>
    /// <param name="loggingBuilder">The logging builder to configure OpenTelemetry logging.</param>
    /// <param name="environment">The hosting environment to determine if telemetry should be enabled.</param>
    /// <remarks>
    /// <para>
    /// This method configures OpenTelemetry for:
    /// - Structured logging with formatted messages and scopes
    /// - Distributed tracing for ASP.NET Core and HTTP client requests
    /// - Metrics for ASP.NET Core, HTTP client, and runtime instrumentation
    /// </para>
    /// <para>
    /// Configuration is read from environment variables:
    /// - OTEL_SERVICE_NAME: The service name (defaults to "WebApi")
    /// - OTEL_EXPORTER_OTLP_ENDPOINT: The OTLP exporter endpoint URL (optional)
    /// </para>
    /// <para>
    /// OpenTelemetry is automatically disabled in test environments to avoid interference with tests.
    /// If an OTLP endpoint is configured, telemetry data is exported to that endpoint.
    /// Otherwise, telemetry is collected but not exported.
    /// </para>
    /// </remarks>
    public static void ConfigureOpenTelemetry(
        this IServiceCollection services, 
        IConfiguration configuration,
        ILoggingBuilder loggingBuilder,
        IHostEnvironment environment)
    {
        // only emit otel metrics if we aren't in test
        if (environment.IsEnvironment("Test")) 
            return;
        
        var serviceName = configuration["OTEL_SERVICE_NAME"] ?? "WebApi";
        var otlpEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var resourceBuilder = ResourceBuilder.CreateDefault().AddService(serviceName);

        Uri? endpointUri = null;
        if (!string.IsNullOrWhiteSpace(otlpEndpoint) && Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var parsed))
        {
            endpointUri = parsed;
        }

        // Logs (structured)
        loggingBuilder.AddOpenTelemetry(logging =>
        {
            logging.SetResourceBuilder(resourceBuilder);
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            logging.ParseStateValues = true;

            if (endpointUri != null)
            {
                logging.AddOtlpExporter(o => o.Endpoint = endpointUri);
            }
        });

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing
                    .SetResourceBuilder(resourceBuilder)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (endpointUri != null)
                {
                    tracing.AddOtlpExporter(o => o.Endpoint = endpointUri);
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .SetResourceBuilder(resourceBuilder)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (endpointUri != null)
                {
                    metrics.AddOtlpExporter(o => o.Endpoint = endpointUri);
                }
            });
    }
    
    
    /// <summary>
    /// Configures email services using Resend as the email provider.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">The configuration instance to read email settings from.</param>
    /// <remarks>
    /// <para>
    /// Reads email configuration from the "Email:Resend" section:
    /// - ApiKey: The Resend API key for authentication
    /// - Domain: The email domain to send emails from
    /// </para>
    /// <para>
    /// Registers:
    /// - IResend as a singleton for the Resend client
    /// - RenderMjmlEmailService as a transient service
    /// </para>
    /// </remarks>
    public static void ConfigureEmail(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        var resendApiKey = configuration["Email:Resend:ApiKey"] ?? string.Empty;
        var emailDomain = configuration["Email:Resend:Domain"] ?? string.Empty;
        services.AddSingleton<IResend>(ResendClient.Create(resendApiKey));
        services.AddTransient<RenderMjmlEmailService>(sp =>
            new RenderMjmlEmailService(sp.GetRequiredService<IResend>(), emailDomain));
    }
    
    /// <summary>
    /// Configures rate limiting for the application.
    /// </summary>
    /// <remarks>
    /// Sets up three rate limiting policies:
    /// - Global: Applies to all requests (100 requests per minute per IP/user)
    /// - Auth: Stricter limits for authentication endpoints (5 requests per minute per IP)
    /// - Authenticated: Higher limits for authenticated users (200 requests per minute per user)
    /// </remarks>
    public static void ConfigureRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var globalConfig = configuration.GetSection("RateLimiting:Global");
        var authConfig = configuration.GetSection("RateLimiting:Auth");
        var authenticatedConfig = configuration.GetSection("RateLimiting:Authenticated");
        
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            
            // Global rate limiter - applies to all requests
            var globalPermitLimit = globalConfig.GetValue<int>("PermitLimit", 100);
            var globalWindowMinutes = globalConfig.GetValue<int>("WindowMinutes", 1);
            var globalQueueLimit = globalConfig.GetValue<int>("QueueLimit", 10);
            
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                // Use authenticated user ID if available, otherwise use IP address
                var partitionKey = context.User.Identity?.Name 
                    ?? context.Connection.RemoteIpAddress?.ToString() 
                    ?? "anonymous";
                
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: partitionKey,
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = globalPermitLimit,
                        Window = TimeSpan.FromMinutes(globalWindowMinutes),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = globalQueueLimit
                    });
            });
            
            // Auth endpoints policy - stricter limits to prevent brute force attacks (IP-based)
            var authPermitLimit = authConfig.GetValue<int>("PermitLimit", 5);
            var authWindowMinutes = authConfig.GetValue<int>("WindowMinutes", 1);
            var authQueueLimit = authConfig.GetValue<int>("QueueLimit", 2);
            
            options.AddPolicy("auth", context =>
            {
                var partitionKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: partitionKey,
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = authPermitLimit,
                        Window = TimeSpan.FromMinutes(authWindowMinutes),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = authQueueLimit
                    });
            });
            
            // Authenticated users policy - higher limits for authenticated users
            var authenticatedPermitLimit = authenticatedConfig.GetValue<int>("PermitLimit", 200);
            var authenticatedWindowMinutes = authenticatedConfig.GetValue<int>("WindowMinutes", 1);
            var authenticatedQueueLimit = authenticatedConfig.GetValue<int>("QueueLimit", 20);
            
            options.AddPolicy("authenticated", context =>
            {
                var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (userId == null)
                {
                    // If not authenticated, use no limiter (will fall back to global)
                    return RateLimitPartition.GetNoLimiter("anonymous");
                }
                
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: userId,
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = authenticatedPermitLimit,
                        Window = TimeSpan.FromMinutes(authenticatedWindowMinutes),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = authenticatedQueueLimit
                    });
            });
            
            // Custom response when rate limit is exceeded
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/json";
                
                // Try to get retry after from metadata
                var retryAfter = 60; // default to 60 seconds
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterValue))
                {
                    retryAfter = (int)((TimeSpan)retryAfterValue).TotalSeconds;
                }
                
                context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString();
                
                var response = new
                {
                    message = "Rate limit exceeded. Please try again later.",
                    retryAfter = retryAfter
                };
                
                await context.HttpContext.Response.WriteAsJsonAsync(response, cancellationToken);
            };
        });
    }
    
    /// <summary>
    /// Configures response compression for the application (production only).
    /// </summary>
    /// <remarks>
    /// Enables Gzip and Brotli compression for responses to reduce bandwidth usage.
    /// Only enabled in a production environment.
    /// </remarks>
    public static void ConfigureResponseCompression(
        this IServiceCollection services,
        IHostEnvironment environment)
    {
        // Only enable compression in production
        if (!environment.IsProduction())
        {
            return;
        }

        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true; // Enable compression for HTTPS
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            
            // Compress these MIME types
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
            {
                "application/json",
                "application/xml",
                "text/json",
                "text/xml"
            });
        });

        // Configure compression levels
        services.Configure<BrotliCompressionProviderOptions>(options =>
        {
            options.Level = CompressionLevel.Optimal;
        });

        services.Configure<GzipCompressionProviderOptions>(options =>
        {
            options.Level = CompressionLevel.Optimal;
        });
    }

    /// <summary>
    /// Configures response caching for the application (production only).
    /// </summary>
    /// <remarks>
    /// Enables HTTP-level response caching to reduce server load and improve performance.
    /// Only enabled in a production environment.
    /// </remarks>
    public static void ConfigureResponseCaching(
        this IServiceCollection services,
        IHostEnvironment environment)
    {
        // Only enable response caching in production
        if (!environment.IsProduction())
        {
            return;
        }

        services.AddResponseCaching(options =>
        {
            // Maximum cacheable response size (100 MB)
            options.MaximumBodySize = 100 * 1024 * 1024;
            
            // Maximum cache size (100 MB)
            options.SizeLimit = 100 * 1024 * 1024;
            
            // Use case-sensitive paths for cache keys
            options.UseCaseSensitivePaths = false;
        });
    }
    
    /// <summary>
    /// Configures request size limits for the application.
    /// </summary>
    /// <remarks>
    /// Sets global request body size limits and form data limits.
    /// Individual endpoints can override these limits using the [RequestSizeLimit(bytes)] attribute.
    /// </remarks>
    public static void ConfigureRequestLimits(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var limitsConfig = configuration.GetSection(RequestLimitsSettings.SectionName);
        var maxRequestBodySizeBytes = limitsConfig.GetValue<long>("MaxRequestBodySizeBytes", 10 * 1024 * 1024); // Default: 10 MB
        var maxFormValueLength = limitsConfig.GetValue<int>("MaxFormValueLength", 4 * 1024 * 1024); // Default: 4 MB
        var maxFormKeyLength = limitsConfig.GetValue<int>("MaxFormKeyLength", 2 * 1024); // Default: 2 KB
        var maxFormFileSizeBytes = limitsConfig.GetValue<long>("MaxFormFileSizeBytes", 5 * 1024 * 1024); // Default: 5 MB

        // Configure form options (for form data limits)
        services.Configure<FormOptions>(options =>
        {
            options.ValueLengthLimit = maxFormValueLength;
            options.KeyLengthLimit = maxFormKeyLength;
            options.MultipartBodyLengthLimit = maxRequestBodySizeBytes;
            options.MultipartHeadersLengthLimit = 16384; // 16 KB for headers
        });

        // Configure Kestrel server options (for request body size limits)
        services.Configure<KestrelServerOptions>(options =>
        {
            options.Limits.MaxRequestBodySize = maxRequestBodySizeBytes;
        });

        // Configure IIS server options (for IIS hosting)
        services.Configure<IISServerOptions>(options =>
        {
            options.MaxRequestBodySize = maxRequestBodySizeBytes;
        });
    }
    
    /// <summary>
    /// Configures authentication-related services.
    /// </summary>
    public static void ConfigureAuthServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpClient(); // Required for GitHubTokenValidationService
        services.AddScoped<JwtTokenService>();
        services.AddScoped<RefreshTokenService>();
        services.AddScoped<PasswordValidator>();
        services.AddScoped<GoogleTokenValidationService>();
        services.AddScoped<MicrosoftTokenValidationService>();
        services.AddScoped<GitHubTokenValidationService>();
        services.AddScoped<TokenValidationServiceFactory>();
        services.AddScoped<AuthService>();
        
        // Configure PasswordResetService
        services.AddScoped<PasswordResetService>(sp =>
        {
            var frontendUrl = configuration["Frontend:BaseUrl"] ?? throw new InvalidOperationException("Frontend URL not configured");
            return new PasswordResetService(
                sp.GetRequiredService<AppDbContext>(), 
                sp.GetRequiredService<RenderMjmlEmailService>(),
                sp.GetRequiredService<PasswordValidator>(),
                frontendUrl);
        });
    }
    
    /// <summary>
    /// Configures JSON serialization options for ASP.NET Core controllers.
    /// </summary>
    /// <param name="options">The JSON options to configure.</param>
    /// <remarks>
    /// <para>
    /// Applies the following JSON serialization settings:
    /// - WriteIndented: true - Formats JSON output with indentation for readability
    /// - PropertyNamingPolicy: SnakeCaseLower - Converts property names to snake_case
    /// - PropertyNameCaseInsensitive: true - Allows case-insensitive property name matching during deserialization
    /// </para>
    /// <para>
    /// This method is used internally by the JSON options configuration callback.
    /// </para>
    /// </remarks>
    internal static void ConfigureJsonCallback(JsonOptions options)
    {
        var jsonSerializerOptions = options.JsonSerializerOptions;
        jsonSerializerOptions.WriteIndented = true;
        jsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        jsonSerializerOptions.PropertyNameCaseInsensitive = true;
    }
}

internal static class DatabaseConfigurationHelpers
{
    internal static bool ShouldUseInMemoryDatabase(IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"];
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        
        return string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(connectionString, "InMemory", StringComparison.OrdinalIgnoreCase);
    }

    internal static void UseInMemoryDatabase(IServiceCollection services, ILogger logger)
    {
        logger.LogInformation("Using in-memory database provider 'FallbackInMemoryDatabase'.");
        services.AddDbContext<AppDbContext>(options => 
            options.UseInMemoryDatabase("FallbackInMemoryDatabase"));
    }

    // ReSharper disable once IdentifierTypo
    internal static bool TryUsePostgreSql(
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
                    npgsqlOptions.UseVector();
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
                    npgsqlOptions.UseVector();
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