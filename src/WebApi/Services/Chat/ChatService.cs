using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

#pragma warning disable MEAI001

namespace WebApi.Services.Chat;

/// <summary>
/// Core chat processing service - web-friendly, no UI coupling.
/// Processes one message and returns streaming updates.
/// </summary>
public class ChatService(
    AIAgent agent,
    SessionManager sessionManager)
{
    /// <summary>
    /// Process a single message and stream responses.
    /// Web: Yield directly to SSE/SignalR
    /// Console: Collect and display
    /// </summary>
    public async IAsyncEnumerable<AgentResponseUpdate> ProcessMessageStreamAsync(
        string message,
        AgentSession session)
    {
        await foreach (var update in agent.RunStreamingAsync(message, session))
        {
            yield return update;
        }

        // Persist after streaming completes
        await sessionManager.PersistSessionAsync(session);
    }

    /// <summary>
    /// Process tool approval response and continue streaming.
    /// Used when agent requests approval for function calls.
    /// </summary>
    public async IAsyncEnumerable<AgentResponseUpdate> ProcessToolApprovalStreamAsync(
        FunctionApprovalRequestContent approval,
        bool approved,
        AgentSession session)
    {
        var response = new ChatMessage(ChatRole.User, [approval.CreateResponse(approved)]);

        await foreach (var update in agent.RunStreamingAsync([response], session))
        {
            yield return update;
        }
    }

    /// <summary>
    /// Extract pending tool approvals from response updates.
    /// Returns empty list if no approvals needed.
    /// </summary>
    public List<FunctionApprovalRequestContent> GetPendingApprovals(List<AgentResponseUpdate> updates)
    {
        return updates
            .SelectMany(u => u.UserInputRequests)
            .OfType<FunctionApprovalRequestContent>()
            .ToList();
    }
}
