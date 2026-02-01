using Microsoft.EntityFrameworkCore;
using WebApi.Data;
using WebApi.Models;

namespace WebApi.Repositories;

public class EpisodeRepository(AppDbContext context)
{
    public async Task<List<Episode>> GetActiveEpisodesAsync(Guid userId, int days = 14)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-days);
        return await context.Episodes
            .Where(e => e.UserId == userId && 
                       e.Status == "active" && 
                       e.StartedAt >= cutoffDate)
            .Include(e => e.Symptom!)
            .OrderByDescending(e => e.StartedAt)
            .ToListAsync();
    }

    public async Task<List<Episode>> GetEpisodesBySymptomAsync(Guid userId, string symptomName)
    {
        return await context.Episodes
            .Where(e => e.UserId == userId && 
                       e.Symptom != null && 
                       e.Symptom.Name == symptomName)
            .Include(e => e.Symptom!)
            .OrderByDescending(e => e.StartedAt)
            .ToListAsync();
    }

    public async Task<Episode> CreateEpisodeAsync(
        Guid userId,
        int symptomId,
        DateTime startedAt,
        string stage = "mentioned",
        string status = "active")
    {
        var episode = new Episode
        {
            SymptomId = symptomId,
            UserId = userId,
            Stage = stage,
            Status = status,
            StartedAt = startedAt,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        context.Episodes.Add(episode);
        await context.SaveChangesAsync();
        return episode;
    }

    public async Task<Episode?> UpdateEpisodeAsync(
        int episodeId,
        int? severity = null,
        string? location = null,
        string? frequency = null,
        List<string>? triggers = null,
        List<string>? relievers = null,
        string? pattern = null)
    {
        var episode = await context.Episodes
            .FirstOrDefaultAsync(e => e.Id == episodeId);

        if (episode == null)
        {
            return null;
        }

        // Update fields
        if (severity.HasValue)
        {
            episode.Severity = severity;
        }
        if (location != null)
        {
            episode.Location = location;
        }
        if (frequency != null)
        {
            episode.Frequency = frequency;
        }
        if (triggers != null)
        {
            episode.Triggers = triggers;
        }
        if (relievers != null)
        {
            episode.Relievers = relievers;
        }
        if (pattern != null)
        {
            episode.Pattern = pattern;
        }

        // Advance stage based on fields filled
        var fieldsFilled = CountFilledFields(episode);
        if (fieldsFilled >= 3 && episode.Stage == "explored")
        {
            episode.Stage = "characterized";
        }
        else if (fieldsFilled >= 1 && episode.Stage == "mentioned")
        {
            episode.Stage = "explored";
        }

        episode.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        return episode;
    }

    public async Task<bool> LinkEpisodesAsync(int episodeId, int relatedEpisodeId)
    {
        var episode = await context.Episodes
            .FirstOrDefaultAsync(e => e.Id == episodeId);

        if (episode == null)
        {
            return false;
        }

        episode.Stage = "linked";
        episode.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ResolveEpisodeAsync(int episodeId)
    {
        var episode = await context.Episodes
            .FirstOrDefaultAsync(e => e.Id == episodeId);

        if (episode == null)
        {
            return false;
        }

        episode.Status = "resolved";
        episode.ResolvedAt = DateTime.UtcNow;
        episode.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        return true;
    }

    public async Task<Episode?> GetEpisodeAsync(int episodeId)
    {
        return await context.Episodes
            .Include(e => e.Symptom!)
            .FirstOrDefaultAsync(e => e.Id == episodeId);
    }

    private static int CountFilledFields(Episode episode)
    {
        var count = 0;
        if (episode.Severity.HasValue) count++;
        if (!string.IsNullOrWhiteSpace(episode.Location)) count++;
        if (!string.IsNullOrWhiteSpace(episode.Frequency)) count++;
        if (episode.Triggers != null && episode.Triggers.Any()) count++;
        if (episode.Relievers != null && episode.Relievers.Any()) count++;
        if (!string.IsNullOrWhiteSpace(episode.Pattern)) count++;
        return count;
    }
}
