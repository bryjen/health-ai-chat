using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using WebApi.Configuration;
using WebApi.Data;

namespace WebApi.Controllers.Test;

/// <summary>
/// Debug endpoints for testing and troubleshooting the API, AI services, configuration, and health status.
/// </summary>
[ApiController]
[Route("api/v1/debug")]
[Produces("application/json")]
public class DebugController : ControllerBase
{
    #region AI Testing

    /// <summary>
    /// Tests the connection to Azure OpenAI by making a simple test call.
    /// </summary>
    /// <returns>Connection status and test response</returns>
    /// <response code="200">AI service is reachable and responding</response>
    /// <response code="503">AI service connection failed</response>
    [HttpGet("ai/connection")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> TestAiConnection(
        [FromServices] Kernel kernel,
        [FromServices] ILogger<DebugController> logger)
    {
        try
        {
            const string testPrompt = "Say 'AI connection successful' in exactly those words, nothing else.";
            
            var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage(testPrompt);

            var result = await chatCompletionService.GetChatMessageContentsAsync(
                chatHistory,
                cancellationToken: HttpContext.RequestAborted);

            var responseText = result.FirstOrDefault()?.Content ?? string.Empty;
            
            logger.LogInformation("AI connection test successful. Response received: {Response}", responseText);

            return Ok(new
            {
                status = "connected",
                message = "Successfully reached AI service",
                response = responseText,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI connection test failed");
            
            return StatusCode(503, new
            {
                status = "disconnected",
                message = "Failed to connect to AI service",
                error = ex.Message,
                timestamp = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Gets a programming joke from the AI model.
    /// </summary>
    /// <returns>A programming joke from the AI</returns>
    /// <response code="200">Joke retrieved successfully</response>
    /// <response code="503">AI service connection failed</response>
    [HttpGet("ai/joke")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetAiJoke(
        [FromServices] Kernel kernel,
        [FromServices] ILogger<DebugController> logger)
    {
        try
        {
            const string prompt = "Tell me a short programming joke";
            
            var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage(prompt);

            var result = await chatCompletionService.GetChatMessageContentsAsync(
                chatHistory,
                cancellationToken: HttpContext.RequestAborted);

            var joke = result.FirstOrDefault()?.Content ?? "I couldn't think of a joke right now.";
            
            logger.LogInformation("AI joke endpoint invoked successfully. Joke length: {Length}", joke.Length);

            return Ok(new
            {
                prompt,
                response = joke,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI joke endpoint failed");
            
            return StatusCode(503, new
            {
                status = "error",
                message = "Failed to get joke from AI service",
                error = ex.Message,
                timestamp = DateTime.UtcNow
            });
        }
    }

    #endregion

    #region Configuration

    /// <summary>
    /// Shows the CORS configuration that's actually being used
    /// </summary>
    [HttpGet("config/cors")]
    public IActionResult GetCorsConfig(
        [FromServices] IConfiguration configuration)
    {
        // Use the exact same logic as Program.cs
        var resolvedOrigins = ServiceConfiguration.GetCorsAllowedOrigins(configuration);

        // Check configuration key for debugging
        var corsOriginsString = configuration["Cors__AllowedOrigins"]
                             ?? Environment.GetEnvironmentVariable("Cors__AllowedOrigins");

        var corsOriginsArray = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();

        // Get all environment variables that start with "Cors" for debugging
        var allCorsEnvVars = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .Where(e => e.Key.ToString()?.StartsWith("Cors", StringComparison.OrdinalIgnoreCase) == true)
            .ToDictionary(e => e.Key.ToString()!, e => e.Value?.ToString() ?? "(null)");

        // Build response showing what we found
        var response = new
        {
            corsOriginsString = corsOriginsString ?? "(null)",
            corsOriginsArray = corsOriginsArray ?? Array.Empty<string>(),
            corsOriginsArrayLength = corsOriginsArray?.Length ?? 0,
            environmentVariable = Environment.GetEnvironmentVariable("Cors__AllowedOrigins") ?? "(not set)",
            allCorsEnvironmentVariables = allCorsEnvVars,
            allConfigKeys = new
            {
                corsAllowedOrigins = configuration["Cors__AllowedOrigins"] ?? "(null)",
            },
            resolvedOrigins = resolvedOrigins,
            resolvedOriginsCount = resolvedOrigins.Length,
            willAllowAllOrigins = resolvedOrigins.Length == 0
        };

        return Ok(response);
    }

    /// <summary>
    /// Returns all configuration values as JSON (development only)
    /// </summary>
    /// <returns>Complete configuration as JSON</returns>
    /// <response code="200">Returns all configuration values</response>
    [HttpGet("config/all")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult GetAllConfig(
        [FromServices] IConfiguration configuration)
    {
        var configDict = GetConfigurationAsDictionary(configuration);
        return Ok(configDict);
    }

    private static Dictionary<string, object?> GetConfigurationAsDictionary(IConfiguration configuration)
    {
        var result = new Dictionary<string, object?>();

        foreach (var child in configuration.GetChildren())
        {
            result[child.Key] = GetConfigurationValue(child);
        }

        return result;
    }

    private static object? GetConfigurationValue(IConfigurationSection section)
    {
        var children = section.GetChildren().ToList();
        
        if (children.Count == 0)
        {
            // Leaf node - return the value
            return section.Value;
        }

        // Has children - return as dictionary
        var dict = new Dictionary<string, object?>();
        foreach (var child in children)
        {
            dict[child.Key] = GetConfigurationValue(child);
        }

        // If there's also a value at this level, include it
        if (!string.IsNullOrEmpty(section.Value))
        {
            dict["_value"] = section.Value;
        }

        return dict;
    }

    #endregion

    #region Health

    /// <summary>
    /// Get detailed health status of the API and its dependencies
    /// </summary>
    /// <returns>Health status information including database connectivity</returns>
    /// <response code="200">Service is healthy</response>
    /// <response code="503">Service is unhealthy</response>
    [HttpGet("health")]
    [ProducesResponseType(typeof(HealthStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HealthStatus), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HealthStatus>> GetHealth(
        [FromServices] AppDbContext context,
        [FromServices] ILogger<DebugController> logger)
    {
        var health = new HealthStatus
        {
            Status = "Healthy",
            Timestamp = DateTime.UtcNow,
            Version = GetType().Assembly.GetName().Version?.ToString() ?? "Unknown"
        };

        // Check database connectivity
        try
        {
            if (context.Database.IsRelational())
            {
                var canConnect = await context.Database.CanConnectAsync();
                health.Database = new DatabaseHealth
                {
                    Status = canConnect ? "Connected" : "Disconnected",
                    Provider = context.Database.ProviderName ?? "Unknown"
                };
            }
            else
            {
                health.Database = new DatabaseHealth
                {
                    Status = "InMemory",
                    Provider = "InMemory"
                };
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database health check failed");
            health.Database = new DatabaseHealth
            {
                Status = "Error",
                Provider = context.Database.ProviderName ?? "Unknown",
                Error = ex.Message
            };
            health.Status = "Unhealthy";
        }

        // Determine overall status
        if (health.Status == "Unhealthy" || 
            (health.Database != null && health.Database.Status == "Error"))
        {
            return StatusCode(503, health);
        }

        return Ok(health);
    }

    #endregion
}

/// <summary>
/// Health status response model
/// </summary>
public class HealthStatus
{
    public string Status { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Version { get; set; } = string.Empty;
    public DatabaseHealth? Database { get; set; }
}

/// <summary>
/// Database health information
/// </summary>
public class DatabaseHealth
{
    public string Status { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string? Error { get; set; }
}
