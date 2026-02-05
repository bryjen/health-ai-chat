using Microsoft.EntityFrameworkCore;
using Web.Common.DTOs.Health;
using WebApi.Data;
using WebApi.Models;

namespace WebApi.Services.Chat;

/// <summary>
/// Tracks changes to health entities (episodes, appointments, assessments) by comparing database state.
/// </summary>
public class EntityChangeTracker(AppDbContext context, ILogger<EntityChangeTracker> logger)
{
    private const int RecentCutoffSeconds = 30; // Should cover the AI processing time

    public async Task<List<EntityChange>> TrackEpisodeChangesAsync(
        Guid userId,
        List<SymptomChange>? symptomChangesFromAi,
        Dictionary<int, string> episodesBeforeDict)
    {
        var changes = new List<EntityChange>();

        // Find episodes created or updated in the last 30 seconds (should cover the AI processing time)
        var recentCutoff = DateTime.UtcNow.AddSeconds(-RecentCutoffSeconds);
        var recentEpisodes = await context.Episodes
            .Where(e => e.UserId == userId &&
                        (e.CreatedAt >= recentCutoff || e.UpdatedAt >= recentCutoff))
            .Include(e => e.Symptom)
            .ToListAsync();

        foreach (var episode in recentEpisodes)
        {
            var symptomName = episode.Symptom?.Name ?? "Unknown";
            var wasCreated = episode.CreatedAt >= recentCutoff;
            var wasUpdated = !wasCreated && episode.UpdatedAt >= recentCutoff;

            if (wasCreated && !episodesBeforeDict.ContainsKey(episode.Id))
            {
                changes.Add(new EntityChange
                {
                    Id = episode.Id.ToString(),
                    Action = "created",
                    Name = symptomName
                });
            }
            else if (wasUpdated && episodesBeforeDict.ContainsKey(episode.Id))
            {
                changes.Add(new EntityChange
                {
                    Id = episode.Id.ToString(),
                    Action = "updated",
                    Name = symptomName
                });
            }
        }

        // Also check for resolved episodes
        var resolvedEpisodes = await context.Episodes
            .Where(e => e.UserId == userId &&
                        e.Status == "resolved" &&
                        e.ResolvedAt >= recentCutoff)
            .Include(e => e.Symptom)
            .ToListAsync();

        foreach (var episode in resolvedEpisodes)
        {
            if (episodesBeforeDict.ContainsKey(episode.Id))
            {
                var resolvedSymptomName = episode.Symptom?.Name ?? "Unknown";
                changes.Add(new EntityChange
                {
                    Id = episode.Id.ToString(),
                    Action = "resolved",
                    Name = resolvedSymptomName
                });
            }
        }

        return changes;
    }

    public async Task<List<EntityChange>> TrackAppointmentChangesAsync(
        Guid userId,
        List<Guid> appointmentsBefore)
    {
        var changes = new List<EntityChange>();

        // Find appointments created or updated in the last 30 seconds (should cover the AI processing time)
        var recentCutoff = DateTime.UtcNow.AddSeconds(-RecentCutoffSeconds);
        var recentAppointments = await context.Appointments
            .Where(a => a.UserId == userId &&
                        (a.CreatedAt >= recentCutoff || a.UpdatedAt >= recentCutoff))
            .ToListAsync();

        foreach (var appointment in recentAppointments)
        {
            var wasCreated = appointment.CreatedAt >= recentCutoff;
            var wasUpdated = !wasCreated && appointment.UpdatedAt >= recentCutoff;

            if (wasCreated && !appointmentsBefore.Contains(appointment.Id))
            {
                changes.Add(new EntityChange
                {
                    Id = appointment.Id.ToString(),
                    Action = "created"
                });
            }
            else if (wasUpdated && appointmentsBefore.Contains(appointment.Id))
            {
                changes.Add(new EntityChange
                {
                    Id = appointment.Id.ToString(),
                    Action = "updated"
                });
            }
        }

        return changes;
    }

    public async Task<List<EntityChange>> TrackAssessmentChangesAsync(
        Guid userId,
        Guid conversationId,
        List<int> assessmentsBefore)
    {
        var changes = new List<EntityChange>();

        // Find assessments created in the last 30 seconds
        var recentCutoff = DateTime.UtcNow.AddSeconds(-RecentCutoffSeconds);
        var recentAssessments = await context.Assessments
            .Where(a => a.UserId == userId &&
                        a.ConversationId == conversationId &&
                        a.CreatedAt >= recentCutoff)
            .ToListAsync();

        foreach (var assessment in recentAssessments)
        {
            if (!assessmentsBefore.Contains(assessment.Id))
            {
                changes.Add(new EntityChange
                {
                    Id = assessment.Id.ToString(),
                    Action = "created",
                    Name = assessment.Hypothesis,
                    Confidence = assessment.Confidence
                });
            }
        }

        return changes;
    }

    public async Task<(Dictionary<int, string> EpisodesBefore, List<Guid> AppointmentsBefore, List<int> AssessmentsBefore)> 
        GetBeforeStateAsync(Guid userId, Guid conversationId)
    {
        var episodesBefore = await context.Episodes
            .Where(e => e.UserId == userId && e.Status == "active")
            .Select(e => new { e.Id, SymptomName = e.Symptom != null ? e.Symptom.Name : "Unknown" })
            .ToListAsync();
        var episodesBeforeDict = episodesBefore.ToDictionary(e => e.Id, e => e.SymptomName);

        var appointmentsBefore = await context.Appointments
            .Where(a => a.UserId == userId)
            .Select(a => a.Id)
            .ToListAsync();

        var assessmentsBefore = await context.Assessments
            .Where(a => a.UserId == userId && a.ConversationId == conversationId)
            .Select(a => a.Id)
            .ToListAsync();

        return (episodesBeforeDict, appointmentsBefore, assessmentsBefore);
    }
}
