using Web.Common.DTOs.Health;

namespace WebApi.Services.Chat;

public class ResponseRouterService(ILogger<ResponseRouterService> logger)
{
    public HealthAssistantResponse RouteResponse(HealthAssistantResponse response, Guid userId)
    {
        if (response.Appointment == null)
        {
            return response;
        }

        var appointment = response.Appointment;
        var urgency = appointment.Urgency?.ToLowerInvariant() ?? "none";

        switch (urgency)
        {
            case "emergency":
                logger.LogWarning("Emergency appointment detected for user {UserId}. Urgency: {Urgency}, Action: {Action}",
                    userId, appointment.Urgency, appointment.EmergencyAction);
                // Don't trigger booking for emergencies - user should seek immediate care
                return response;

            case "high":
            case "medium":
                if (appointment.ReadyToBook)
                {
                    logger.LogInformation("Appointment booking triggered for user {UserId}. Urgency: {Urgency}",
                        userId, appointment.Urgency);
                    // In the future, this could trigger a booking flow or send notification
                    // For now, just return the response with booking flag set
                    return response;
                }
                return response;

            default:
                // Low urgency or no appointment needed
                return response;
        }
    }
}
