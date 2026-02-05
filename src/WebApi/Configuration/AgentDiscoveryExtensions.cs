using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;

namespace WebApi.Configuration;

/// <summary>
/// Extension methods for agent discovery endpoints.
/// </summary>
internal static class AgentDiscoveryExtensions
{
    /// <summary>
    /// Maps an agent discovery endpoint that returns all registered AI agents.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="path">The route path for the discovery endpoint.</param>
    public static void MapAgentDiscovery(this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string path)
    {
        var registeredAIAgents = endpoints.ServiceProvider.GetKeyedServices<AIAgent>(KeyedService.AnyKey);

        var routeGroup = endpoints.MapGroup(path);
        routeGroup.MapGet("/", async (CancellationToken cancellationToken) =>
        {
            var results = new List<AgentDiscoveryCard>();
            foreach (var result in registeredAIAgents)
            {
                results.Add(new AgentDiscoveryCard
                {
                    Name = result.Name!,
                    Description = result.Description,
                });
            }

            return Results.Ok(results);
        })
        .WithName("GetAgents");
    }

    internal sealed class AgentDiscoveryCard
    {
        public required string Name { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Description { get; set; }
    }
}
