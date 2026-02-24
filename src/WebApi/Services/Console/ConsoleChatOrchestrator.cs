using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using WebApi.Models.EfCore.Chat;
using WebApi.Services.Chat.Response;
using WebApi.Data;
using WebApi.Services.Chat;

namespace WebApi.Services.Console;

/// <summary>
/// Console-specific orchestrator that manages the chat loop.
/// Uses web-friendly ChatService and SessionManager under the hood.
/// NOT for web - this is console only!
/// </summary>
public class ConsoleChatOrchestrator(
    ChatService chatService,
    SessionManager sessionManager,
    IServiceScopeFactory serviceScopeFactory,
    ResponseWriter responseWriter)
{
    /// <summary>
    /// Main entry point - handles session selection and runs chat loop.
    /// </summary>
    public async Task RunAsync()
    {
        var session = await SelectOrCreateSessionAsync();
        await RunChatLoopAsync(session);
    }

    /// <summary>
    /// Console-specific: Infinite loop that processes messages until exit.
    /// Web equivalent: Single POST request handler.
    /// </summary>
    private async Task RunChatLoopAsync(AgentSession session)
    {
        while (true)
        {
            await responseWriter.ResetState();

            System.Console.Write("user  >\t");
            var userInput = System.Console.ReadLine() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(userInput))
                continue;

            // Process message and collect streaming updates
            var updates = await CollectStreamingUpdatesAsync(
                chatService.ProcessMessageStreamAsync(userInput, session));

            // Handle tool approvals if needed
            await HandleToolApprovalsAsync(updates, session);
            await responseWriter.CompleteAsync();

            System.Console.WriteLine("\n------------------------------");
        }
    }

    /// <summary>
    /// Console-specific: Prompt user to select existing session or create new.
    /// Web equivalent: Session ID from JWT/cookie/header.
    /// </summary>
    private async Task<AgentSession> SelectOrCreateSessionAsync()
    {
        var sessions = await sessionManager.GetSessionsAsync();

        if (sessions.Count == 0)
        {
            System.Console.WriteLine("No existing sessions. Starting new session...");
            System.Console.Clear();
            return await sessionManager.CreateNewSessionAsync();
        }

        // Display session menu
        System.Console.WriteLine("Select a session:");
        System.Console.WriteLine("  [0] New session");

        for (var i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            var preview = await GetSessionPreviewAsync(s.Id);
            System.Console.WriteLine($"  [{i + 1}] {s.UpdatedAt:yyyy-MM-dd HH:mm} | {preview}");
        }

        System.Console.Write("\n> ");
        var input = System.Console.ReadLine()?.Trim() ?? "0";

        // Handle selection
        if (int.TryParse(input, out var choice) && choice >= 1 && choice <= sessions.Count)
        {
            try
            {
                return await RestoreSessionWithHistoryAsync(sessions[choice - 1]);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("tool"))
            {
                AnsiConsole.MarkupLine($"[red]⚠️  Error: {ex.Message}[/]");
                System.Console.WriteLine("Starting fresh session instead...\n");
                System.Console.Clear();
                return await sessionManager.CreateNewSessionAsync();
            }
        }
        else
        {
            System.Console.Clear();
            return await sessionManager.CreateNewSessionAsync();
        }
    }

    /// <summary>
    /// Restore session and display chat history.
    /// </summary>
    private async Task<AgentSession> RestoreSessionWithHistoryAsync(SessionEntity sessionEntity)
    {
        var session = await sessionManager.LoadSessionAsync(sessionEntity.Id);
        var history = await sessionManager.GetSessionHistoryAsync(sessionEntity.Id);

        System.Console.Clear();
        await DisplaySessionHistory(history);

        return session;
    }

    /// <summary>
    /// Display chat history using ResponseWriter for consistent formatting.
    /// Uses same formatting pipeline as streaming (including tags).
    /// </summary>
    private async Task DisplaySessionHistory(List<ChatMessage> messages)
    {
        foreach (var msg in messages)
        {
            await responseWriter.ResetState();

            // Output role label for user messages
            if (msg.Role == ChatRole.User)
            {
                System.Console.Write("user  >\t");
            }
            else
            {
                System.Console.Write("assistant >\t");
            }

            // Use ResponseWriter for consistent formatting (includes tags)
            await responseWriter.WriteMessageAsync(msg);
            await responseWriter.CompleteAsync();
            System.Console.WriteLine();
        }
        System.Console.WriteLine("------------------------------");
    }

    /// <summary>
    /// Collect streaming updates and display to console.
    /// </summary>
    private async Task<List<AgentResponseUpdate>> CollectStreamingUpdatesAsync(
        IAsyncEnumerable<AgentResponseUpdate> stream)
    {
        var updates = new List<AgentResponseUpdate>();

        await foreach (var update in stream)
        {
            await responseWriter.WriteChunkAsync(update);
            updates.Add(update);
        }

        return updates;
    }

    /// <summary>
    /// Console-specific: Prompt user for tool approval (blocking).
    /// Web equivalent: Return approval request, wait for client callback.
    /// </summary>
    private async Task HandleToolApprovalsAsync(List<AgentResponseUpdate> updates, AgentSession session)
    {
        var approvals = chatService.GetPendingApprovals(updates);

        foreach (var approval in approvals)
        {
            System.Console.Write($"Approve '{approval.FunctionCall.Name}'? (Y/n): ");
            var approved = System.Console.ReadLine()?.Equals("Y", StringComparison.OrdinalIgnoreCase) ?? false;

            // Process approval and stream response
            await CollectStreamingUpdatesAsync(
                chatService.ProcessToolApprovalStreamAsync(approval, approved, session));
        }
    }

    /// <summary>
    /// Get preview text for session (first user message).
    /// </summary>
    private async Task<string> GetSessionPreviewAsync(string sessionId)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var preview = await dbContext.ChatMessages
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.Timestamp)
            .Select(m => m.MessageText)
            .FirstOrDefaultAsync() ?? "(empty)";

        if (preview.Length > 60)
            preview = preview[..60] + "...";

        return preview;
    }
}
