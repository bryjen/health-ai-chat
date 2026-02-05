using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using WebApi.Hubs;

namespace WebApi.Services.Chat;

/// <summary>
/// Service for sending real-time status updates via SignalR during chat processing.
/// </summary>
public interface IStatusUpdateService
{
    string? ConnectionId { get; }
    void SetConnectionId(string connectionId);
    Task SendStatusUpdateAsync(object statusData);
    Task SendGeneratingAssessmentAsync();
    Task SendAnalyzingAssessmentAsync();
    Task SendAssessmentCreatedAsync(int assessmentId, string hypothesis, decimal confidence);
}

public class StatusUpdateService : IStatusUpdateService
{
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly ILogger<StatusUpdateService> _logger;

    public string? ConnectionId { get; private set; }

    public StatusUpdateService(
        IHubContext<ChatHub> hubContext,
        ILogger<StatusUpdateService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public void SetConnectionId(string connectionId)
    {
        ConnectionId = connectionId;
    }

    public async Task SendStatusUpdateAsync(object statusData)
    {
        if (ConnectionId == null)
        {
            _logger.LogWarning("[STATUS UPDATE SERVICE] Cannot send status update - ConnectionId is not set");
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(statusData);
            _logger.LogInformation("[STATUS UPDATE SERVICE] Sending status update to connection {ConnectionId}, JSON: {Json}", ConnectionId, json);
            await _hubContext.Clients.Client(ConnectionId).SendAsync("StatusUpdate", json);
            _logger.LogInformation("[STATUS UPDATE SERVICE] Successfully sent status update to connection {ConnectionId}", ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[STATUS UPDATE SERVICE] Failed to send status update to connection {ConnectionId}", ConnectionId);
        }
    }

    public async Task SendGeneratingAssessmentAsync()
    {
        var status = new
        {
            type = "assessment-generating",
            message = "Generating assessment...",
            timestamp = DateTime.UtcNow
        };
        await SendStatusUpdateAsync(status);
    }

    public async Task SendAnalyzingAssessmentAsync()
    {
        _logger.LogInformation("[STATUS UPDATE SERVICE] SendAnalyzingAssessmentAsync called for connection {ConnectionId}", ConnectionId);
        var status = new
        {
            type = "assessment-analyzing",
            message = "Analyzing assessment...",
            timestamp = DateTime.UtcNow
        };
        await SendStatusUpdateAsync(status);
    }

    public async Task SendAssessmentCreatedAsync(int assessmentId, string hypothesis, decimal confidence)
    {
        _logger.LogInformation("[STATUS UPDATE SERVICE] SendAssessmentCreatedAsync called for connection {ConnectionId}, AssessmentId={AssessmentId}", ConnectionId, assessmentId);
        var status = new
        {
            type = "assessment-created",
            assessmentId = assessmentId,
            hypothesis = hypothesis,
            confidence = confidence,
            timestamp = DateTime.UtcNow
        };
        await SendStatusUpdateAsync(status);
    }
}
