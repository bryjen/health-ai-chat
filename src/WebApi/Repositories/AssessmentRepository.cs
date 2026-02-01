using Microsoft.EntityFrameworkCore;
using WebApi.Data;
using WebApi.Models;

namespace WebApi.Repositories;

public class AssessmentRepository(AppDbContext context)
{
    public async Task<Assessment> CreateAssessmentAsync(Assessment assessment)
    {
        assessment.CreatedAt = DateTime.UtcNow;
        context.Assessments.Add(assessment);
        await context.SaveChangesAsync();
        return assessment;
    }

    public async Task<Assessment?> UpdateAssessmentAsync(
        int assessmentId,
        string? hypothesis = null,
        decimal? confidence = null,
        List<string>? differentials = null,
        string? reasoning = null,
        string? recommendedAction = null,
        List<int>? episodeIds = null,
        List<int>? negativeFindingIds = null)
    {
        var assessment = await context.Assessments
            .FirstOrDefaultAsync(a => a.Id == assessmentId);

        if (assessment == null)
        {
            return null;
        }

        if (hypothesis != null)
        {
            assessment.Hypothesis = hypothesis;
        }
        if (confidence.HasValue)
        {
            assessment.Confidence = confidence.Value;
        }
        if (differentials != null)
        {
            assessment.Differentials = differentials;
        }
        if (reasoning != null)
        {
            assessment.Reasoning = reasoning;
        }
        if (recommendedAction != null)
        {
            assessment.RecommendedAction = recommendedAction;
        }
        if (episodeIds != null)
        {
            assessment.EpisodeIds = episodeIds;
        }
        if (negativeFindingIds != null)
        {
            assessment.NegativeFindingIds = negativeFindingIds;
        }

        await context.SaveChangesAsync();
        return assessment;
    }

    public async Task<Assessment?> GetAssessmentByConversationAsync(Guid conversationId)
    {
        return await context.Assessments
            .Where(a => a.ConversationId == conversationId)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<List<Assessment>> GetRecentAssessmentsAsync(Guid userId, int limit = 10)
    {
        return await context.Assessments
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<Assessment?> GetAssessmentByIdAsync(int id)
    {
        return await context.Assessments
            .FirstOrDefaultAsync(a => a.Id == id);
    }
}
