using Microsoft.EntityFrameworkCore;
using Web.Common.DTOs.Health;
using WebApi.Data;
using WebApi.Repositories;

namespace WebApi.Services.Graph;

public class GraphDataService
{
    private readonly AppDbContext _context;
    private readonly AssessmentRepository _assessmentRepository;

    public GraphDataService(AppDbContext context, AssessmentRepository assessmentRepository)
    {
        _context = context;
        _assessmentRepository = assessmentRepository;
    }

    public async Task<GraphDataDto> GetAssessmentGraphDataAsync(int assessmentId, Guid userId)
    {
        // Get assessment with all related data
        var assessment = await _context.Assessments
            .Include(a => a.LinkedEpisodes)
                .ThenInclude(l => l.Episode!)
                    .ThenInclude(e => e.Symptom)
            .FirstOrDefaultAsync(a => a.Id == assessmentId && a.UserId == userId);

        if (assessment == null)
        {
            throw new InvalidOperationException($"Assessment {assessmentId} not found or does not belong to user {userId}");
        }

        var nodes = new List<GraphNodeDto>();
        var links = new List<GraphLinkDto>();

        // Create assessment node (center node, type: Diagnosis)
        var assessmentNodeId = $"assessment-{assessment.Id}";
        nodes.Add(new GraphNodeDto
        {
            Id = assessmentNodeId,
            Label = assessment.Hypothesis,
            Type = GraphNodeType.Diagnosis,
            Value = (int)(assessment.Confidence * 100), // Scale confidence 0-1 to 0-100
            Group = 2 // Diagnosis group
        });

        // Group episodes by symptom to aggregate weights
        var symptomGroups = assessment.LinkedEpisodes
            .Where(l => l.Episode?.Symptom != null)
            .GroupBy(l => l.Episode!.Symptom!)
            .ToList();

        foreach (var symptomGroup in symptomGroups)
        {
            var symptom = symptomGroup.Key;
            var maxWeight = symptomGroup.Max(l => l.Weight);
            
            // Create symptom node (peripheral node, type: Symptom)
            var symptomNodeId = $"symptom-{symptom.Id}";
            nodes.Add(new GraphNodeDto
            {
                Id = symptomNodeId,
                Label = symptom.Name,
                Type = GraphNodeType.Symptom,
                Value = (int)(maxWeight * 100), // Scale weight 0-1 to 0-100
                Group = 3 // Symptom group
            });

            // Create link from assessment to symptom
            // Use the max weight for this symptom as the link value
            links.Add(new GraphLinkDto
            {
                Source = assessmentNodeId,
                Target = symptomNodeId,
                Value = (int)(maxWeight * 10) // Scale weight 0-1 to 0-10 for link thickness
            });
        }

        return new GraphDataDto
        {
            Nodes = nodes,
            Links = links
        };
    }
}
