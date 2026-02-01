using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebApi.Configuration;
using WebApi.Configuration.Options;
using WebApi.Configuration.Validators;
using WebApi.Data;
using WebApi.Middleware;
using WebApi.Repositories;
using WebApi.Services.AI.Plugins;
using WebApi.Services.AI.Scenarios;
using WebApi.Services.Auth;
using WebApi.Services.Chat;
using WebApi.Services.Email;
using WebApi.Services.Validation;
using WebApi.Services.VectorStore;
using WebApi.Validators;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(ServiceConfiguration.ConfigureJsonCallback);

builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<AppDbContext>();

builder.Services.ConfigureAppOptions(builder.Configuration);
builder.Services.ConfigureEmail(builder.Configuration);
builder.Services.ConfigureOpenApi();
builder.Services.ConfigureDatabase(builder.Configuration, builder.Environment);
var corsEnabled = builder.Services.ConfigureCors(builder.Configuration);
builder.Services.AddDataProtection();
builder.Services.ConfigureJwtAuth(builder.Configuration, builder.Environment);
builder.Services.ConfigureRateLimiting(builder.Configuration);
builder.Services.ConfigureRequestLimits(builder.Configuration);
builder.Services.ConfigureResponseCompression(builder.Environment);
builder.Services.ConfigureResponseCaching(builder.Environment);
builder.Services.ConfigureOpenTelemetry(builder.Configuration, builder.Logging, builder.Environment);
builder.Services.ConfigureAuthServices(builder.Configuration);
builder.Services.ConfigureAi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ConversationService>();

// Register location service as singleton for caching
builder.Services.AddSingleton<WebApi.Services.Location.LocationService>();

// Health chat repositories
builder.Services.AddScoped<SymptomRepository>();
builder.Services.AddScoped<EpisodeRepository>();
builder.Services.AddScoped<AssessmentRepository>();
builder.Services.AddScoped<NegativeFindingRepository>();
builder.Services.AddScoped<AppointmentRepository>();
builder.Services.AddScoped<VectorStoreRepository>();

// Health chat services
builder.Services.AddScoped<VectorStoreService>();
builder.Services.AddScoped<ConversationContextService>();
builder.Services.AddScoped<SymptomTrackerPlugin>();
builder.Services.AddScoped<AssessmentPlugin>();
builder.Services.AddScoped<HealthChatScenario>();
builder.Services.AddScoped<ResponseRouterService>();
builder.Services.AddScoped<HealthChatOrchestrator>();

// SignalR
builder.Services.AddSignalR();
builder.Services.AddScoped<IStatusUpdateService, StatusUpdateService>();

var app = builder.Build();

// validate options on startup -> fast fail
ValidateConfigurationOnStartup(app.Services, app.Environment, app.Logger);  

// request/response logging, must be before GlobalExceptionHandlerMiddleware so it can capture error responses
app.UseMiddleware<RequestLoggingMiddleware>();

// global unhandled exception handling (should be early in pipeline, after logging)
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

// middleware for security response security headers
app.UseMiddleware<SecurityHeadersMiddleware>();

app.UseRateLimiter();

if (app.Environment.IsProduction())
{
    app.UseResponseCompression();
}

// no need to hide openapi docs since this is a "test" project anyways
// in a production environment, just place this in an if statement
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "API v1");
    options.RoutePrefix = string.Empty; // Set Swagger UI at the app's root
    options.DocumentTitle = "API Documentation";
    options.DefaultModelsExpandDepth(2);
    options.DefaultModelExpandDepth(2);
    options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
    options.EnableDeepLinking();
    options.DisplayRequestDuration();
});

if (app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
}

// Routing must come before response caching
app.UseRouting();

if (app.Environment.IsProduction())
{
    app.UseResponseCaching();
}

if (corsEnabled)
{
    app.UseCors();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// SignalR hub
app.MapHub<WebApi.Hubs.ChatHub>("/hubs/chat");

// Health check endpoint
app.MapHealthChecks("/health");

// Apply migrations automatically in container/dev environments (safe no-op if already applied).
// This is required for docker-compose scenarios where the DB starts empty.
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var env = services.GetRequiredService<IHostEnvironment>();

    // Only attempt migration if a relational DbContext is registered (tests override this).
    if (!env.IsEnvironment("Test"))
    {
        var db = services.GetService<AppDbContext>();
        if (db != null && db.Database.IsRelational())
        {
            try
            {
                db.Database.Migrate();
            }
            catch (Exception ex) when (ex.Message.Contains("pending changes") || ex.Message.Contains("PendingModelChanges"))
            {
                // Migration pending - this is expected during development when model changes haven't been migrated yet
                // In production, migrations should be applied via CI/CD or manual process
            }
        }
    }
}

app.Run();

// Make the implicit Program class public for testing
public partial class Program { }
