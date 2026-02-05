using System.Collections.Generic;

namespace WebApi.Hubs;

/// <summary>
/// Abstract base class for client connection handlers. Provides methods for sending real-time updates and notifications
/// to the connected client. Automatically tracks all status updates for persistence.
///
/// Intended to be owned by the direct receiver of the user/client message(s), but also contains tracking to help with
/// persistance.
/// </summary>
public abstract class ClientConnection
{
    private readonly List<object> _trackedStatusUpdates = new();

    /// <summary>
    /// Sends a status update to the client (fire-and-forget).
    /// Automatically tracks the update for persistence.
    /// </summary>
    public void SendStatusUpdate(object statusData)
    {
        // Track for persistence
        _trackedStatusUpdates.Add(statusData);

        // Send via implementation (fire-and-forget)
        SendStatusUpdateCore(statusData);
    }

    /// <summary>
    /// Core implementation for sending status updates. Implementations should send via their transport mechanism.
    /// </summary>
    protected abstract void SendStatusUpdateCore(object statusData);

    /// <summary>
    /// Sends a "generating assessment" status update (fire-and-forget).
    /// </summary>
    public void SendGeneratingAssessment()
    {
        var status = new
        {
            type = "assessment-generating",
            message = "Generating assessment...",
            timestamp = DateTime.UtcNow
        };
        SendStatusUpdate(status);
    }

    /// <summary>
    /// Sends an "analyzing assessment" status update (fire-and-forget).
    /// </summary>
    public void SendAnalyzingAssessment()
    {
        var status = new
        {
            type = "assessment-analyzing",
            message = "Analyzing assessment...",
            timestamp = DateTime.UtcNow
        };
        SendStatusUpdate(status);
    }

    /// <summary>
    /// Sends an "assessment created" status update with details (fire-and-forget).
    /// </summary>
    public void SendAssessmentCreated(int assessmentId, string hypothesis, decimal confidence)
    {
        var status = new
        {
            type = "assessment-created",
            assessmentId = assessmentId,
            hypothesis = hypothesis,
            confidence = confidence,
            timestamp = DateTime.UtcNow
        };
        SendStatusUpdate(status);
    }

    /// <summary>
    /// Gets all tracked status updates for persistence.
    /// </summary>
    public List<object> GetTrackedStatusUpdates()
    {
        return _trackedStatusUpdates;
    }
}
