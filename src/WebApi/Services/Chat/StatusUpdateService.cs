using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using WebApi.Hubs;

namespace WebApi.Services.Chat;

/// <summary>
/// Service for sending real-time status updates via SignalR during chat processing.
/// </summary>
public interface IStatusUpdateService
{
    Task SendStatusUpdateAsync(string connectionId, object statusData);
    Task SendGeneratingAssessmentAsync(string connectionId);
    Task SendAnalyzingAssessmentAsync(string connectionId);
    Task SendAssessmentCreatedAsync(string connectionId, int assessmentId, string hypothesis, decimal confidence);
}

public class StatusUpdateService : IStatusUpdateService
{
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly ILogger<StatusUpdateService> _logger;

    public StatusUpdateService(
        IHubContext<ChatHub> hubContext,
        ILogger<StatusUpdateService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task SendStatusUpdateAsync(string connectionId, object statusData)
    {
        try
        {
            var json = JsonSerializer.Serialize(statusData);
            await _hubContext.Clients.Client(connectionId).SendAsync("StatusUpdate", json);
            _logger.LogDebug("Sent status update to connection {ConnectionId}", connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send status update to connection {ConnectionId}", connectionId);
        }
    }

    public async Task SendGeneratingAssessmentAsync(string connectionId)
    {
        var status = new
        {
            type = "assessment-generating",
            message = "Generating assessment...",
            timestamp = DateTime.UtcNow
        };
        await SendStatusUpdateAsync(connectionId, status);
    }

    public async Task SendAnalyzingAssessmentAsync(string connectionId)
    {
        var status = new
        {
            type = "assessment-analyzing",
            message = "Analyzing assessment...",
            timestamp = DateTime.UtcNow
        };
        await SendStatusUpdateAsync(connectionId, status);
    }

    public async Task SendAssessmentCreatedAsync(string connectionId, int assessmentId, string hypothesis, decimal confidence)
    {
        var status = new
        {
            type = "assessment-created",
            assessmentId = assessmentId,
            hypothesis = hypothesis,
            confidence = confidence,
            timestamp = DateTime.UtcNow
        };
        await SendStatusUpdateAsync(connectionId, status);
    }
}
