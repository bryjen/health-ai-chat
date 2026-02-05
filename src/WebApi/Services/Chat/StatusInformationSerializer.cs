using System.Text.Json;
using Web.Common.DTOs.Health;

namespace WebApi.Services.Chat;

/// <summary>
/// Serializes status information by merging entity changes with real-time status updates.
/// </summary>
public class StatusInformationSerializer(ILogger<StatusInformationSerializer> logger)
{
    public string? Serialize(
        List<EntityChange> symptomChanges,
        List<EntityChange> appointmentChanges,
        List<EntityChange> assessmentChanges,
        List<object>? realTimeStatusUpdates = null)
    {
        // Include real-time status updates even if no EntityChanges
        if (!symptomChanges.Any() && !appointmentChanges.Any() && !assessmentChanges.Any() &&
            (realTimeStatusUpdates == null || !realTimeStatusUpdates.Any()))
        {
            return null;
        }

        var statusList = new List<object>();

        // Add symptom changes
        foreach (var change in symptomChanges)
        {
            switch (change.Action.ToLowerInvariant())
            {
                case "created":
                    if (int.TryParse(change.Id, out var episodeId))
                    {
                        statusList.Add(new
                        {
                            type = "symptom-added",
                            symptomName = change.Name ?? "Unknown symptom",
                            episodeId = episodeId,
                            location = (string?)null,
                            timestamp = DateTime.UtcNow
                        });
                    }

                    break;

                case "updated":
                    statusList.Add(new
                    {
                        type = "general",
                        message = !string.IsNullOrEmpty(change.Name)
                            ? $"Updated {change.Name} details"
                            : "Updated symptom details",
                        timestamp = DateTime.UtcNow
                    });
                    break;

                case "resolved":
                    statusList.Add(new
                    {
                        type = "general",
                        message = !string.IsNullOrEmpty(change.Name)
                            ? $"Resolved {change.Name}"
                            : "Resolved symptom",
                        timestamp = DateTime.UtcNow
                    });
                    break;
            }
        }

        // Add assessment changes
        foreach (var change in assessmentChanges)
        {
            switch (change.Action.ToLowerInvariant())
            {
                case "created":
                    if (int.TryParse(change.Id, out var assessmentId))
                    {
                        // Check if we already have this from real-time updates
                        var alreadyExists = realTimeStatusUpdates?.Any(s =>
                            JsonSerializer.Serialize(s)
                                .Contains($"\"assessmentId\":{assessmentId}")) == true;

                        if (!alreadyExists)
                        {
                            statusList.Add(new
                            {
                                type = "assessment-created",
                                assessmentId = assessmentId,
                                hypothesis = change.Name ?? "Assessment",
                                confidence = change.Confidence ?? 0m,
                                timestamp = DateTime.UtcNow
                            });
                        }
                    }

                    break;
            }
        }

        // Add real-time status updates (assessment-generating, assessment-analyzing, assessment-created)
        // These are sent during processing and should be persisted
        // Sort them by type order first, then timestamp to maintain correct order
        if (realTimeStatusUpdates != null && realTimeStatusUpdates.Any())
        {
            // Sort real-time updates by type order (generating -> created -> analyzing) then timestamp
            var sortedUpdates = realTimeStatusUpdates
                .Select(update =>
                {
                    var json = JsonSerializer.Serialize(update);
                    using var doc = JsonDocument.Parse(json);
                    var timestamp = doc.RootElement.TryGetProperty("timestamp", out var ts)
                        ? ts.GetDateTime()
                        : DateTime.UtcNow;
                    var type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : "";
                    var typeOrder = type switch
                    {
                        "assessment-generating" => 1,
                        "assessment-created" => 2,
                        "assessment-analyzing" => 3,
                        _ => 5
                    };
                    return new { Update = update, Timestamp = timestamp, TypeOrder = typeOrder };
                })
                .OrderBy(x => x.TypeOrder)
                .ThenBy(x => x.Timestamp)
                .Select(x => x.Update)
                .ToList();

            foreach (var statusUpdate in sortedUpdates)
            {
                // Deserialize to check type and avoid duplicates
                var statusJson = JsonSerializer.Serialize(statusUpdate);
                using var doc = JsonDocument.Parse(statusJson);

                if (doc.RootElement.TryGetProperty("type", out var typeElement))
                {
                    var type = typeElement.GetString();

                    // Only add if not already present (avoid duplicates with EntityChanges)
                    if (type == "assessment-created")
                    {
                        // Always add assessment-created from real-time updates (it has the link)
                        // Check if we already added this assessment from EntityChanges
                        if (doc.RootElement.TryGetProperty("assessmentId", out var idElement))
                        {
                            var id = idElement.GetInt32();
                            var alreadyExists = statusList.Any(s =>
                                JsonSerializer.Serialize(s).Contains($"\"assessmentId\":{id}"));
                            if (!alreadyExists)
                            {
                                statusList.Add(statusUpdate);
                            }
                        }
                        else
                        {
                            // Add even without ID if it's from real-time
                            statusList.Add(statusUpdate);
                        }
                    }
                    else
                    {
                        // For other types (generating, complete, analyzing), always add
                        // Check if we already have this exact status to avoid duplicates
                        var alreadyExists = statusList.Any(s =>
                        {
                            var sJson = JsonSerializer.Serialize(s);
                            using var sDoc = JsonDocument.Parse(sJson);
                            if (sDoc.RootElement.TryGetProperty("type", out var sType))
                            {
                                return sType.GetString() == type;
                            }

                            return false;
                        });

                        if (!alreadyExists)
                        {
                            statusList.Add(statusUpdate);
                        }
                    }
                }
            }
        }

        if (!statusList.Any())
        {
            return null;
        }

        return JsonSerializer.Serialize(statusList);
    }
}
