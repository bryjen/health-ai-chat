using Microsoft.EntityFrameworkCore;
using WebApi.Data;
using WebApi.Models;

namespace WebApi.Repositories;

public class NegativeFindingRepository(AppDbContext context)
{
    public async Task<NegativeFinding> RecordNegativeFindingAsync(
        Guid userId,
        string symptomName,
        int? episodeId = null)
    {
        var negativeFinding = new NegativeFinding
        {
            UserId = userId,
            SymptomName = symptomName,
            EpisodeId = episodeId,
            ReportedAt = DateTime.UtcNow
        };

        context.NegativeFindings.Add(negativeFinding);
        await context.SaveChangesAsync();
        return negativeFinding;
    }

    public async Task<List<NegativeFinding>> GetNegativeFindingsAsync(
        Guid userId,
        DateTime? sinceDate = null)
    {
        var query = context.NegativeFindings
            .Where(nf => nf.UserId == userId);

        if (sinceDate.HasValue)
        {
            query = query.Where(nf => nf.ReportedAt >= sinceDate.Value);
        }

        return await query
            .OrderByDescending(nf => nf.ReportedAt)
            .ToListAsync();
    }
}
