using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace WebApi.Services.AI;

/// <summary>
/// Abstract base class implementing the Template Method Pattern for AI scenarios.
/// Provides a common structure for executing AI interactions while allowing
/// concrete scenarios to customize system prompts, chat history building, and context enrichment.
/// </summary>
/// <typeparam name="TIn">The input request type for the scenario</typeparam>
/// <typeparam name="TOut">The output response type for the scenario</typeparam>
public abstract class AiScenarioHandler<TIn, TOut>(
    Kernel kernel, 
    ILogger<AiScenarioHandler<TIn, TOut>> logger)
{
    protected readonly Kernel Kernel = kernel;
    protected readonly ILogger<AiScenarioHandler<TIn, TOut>> Logger = logger;

    /// <summary>
    /// Template method that orchestrates the AI scenario execution.
    /// Can be overridden in derived classes for custom execution logic.
    /// </summary>
    /// <param name="input">The input request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The scenario response</returns>
    public virtual async Task<TOut> ExecuteAsync(TIn input, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get scenario-specific system prompt
            var systemPrompt = GetSystemPrompt();

            // Optionally get embeddings context (returns null by default)
            var embeddingsContext = await GetEmbeddingsContextAsync(input, cancellationToken);

            // Build chat history with system prompt, context, and user input
            var chatHistory = BuildChatHistory(input, embeddingsContext);
            
            // Add system prompt if not already in chat history
            if (!chatHistory.Any(m => m.Role == AuthorRole.System))
            {
                chatHistory.Insert(0, new ChatMessageContent(AuthorRole.System, systemPrompt));
            }

            // Invoke kernel to get AI response
            var responseText = await GetChatCompletionAsync(chatHistory, cancellationToken);

            // Create and return response
            return CreateResponse(responseText);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error executing AI scenario for type {ScenarioType}", typeof(TOut).Name);
            throw;
        }
    }

    /// <summary>
    /// Gets the system prompt for this scenario. Must be implemented by concrete scenarios.
    /// </summary>
    /// <returns>The system prompt string</returns>
    protected abstract string GetSystemPrompt();

    /// <summary>
    /// Builds the chat history from the input and optional context.
    /// Must be implemented by concrete scenarios.
    /// </summary>
    /// <param name="input">The input request</param>
    /// <param name="context">Optional context string (e.g., from embeddings search)</param>
    /// <returns>The constructed chat history</returns>
    protected abstract ChatHistory BuildChatHistory(TIn input, string? context);

    /// <summary>
    /// Optionally retrieves embeddings context for the input.
    /// Override in concrete scenarios to provide context enrichment.
    /// </summary>
    /// <param name="input">The input request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Context string or null if not needed</returns>
    protected virtual Task<string?> GetEmbeddingsContextAsync(TIn input, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(null);
    }

    /// <summary>
    /// Creates the response object from the AI-generated text.
    /// Override if custom response construction is needed.
    /// </summary>
    /// <param name="responseText">The AI-generated response text</param>
    /// <returns>The scenario response object</returns>
    protected virtual TOut CreateResponse(string responseText)
    {
        // Default implementation assumes TOut has a constructor or property that accepts string
        // Concrete scenarios should override if needed
        if (typeof(TOut) == typeof(string))
        {
            return (TOut)(object)responseText;
        }

        throw new InvalidOperationException(
            $"Cannot create response of type {typeof(TOut).Name} from string. " +
            $"Override {nameof(CreateResponse)} in {GetType().Name}.");
    }

    /// <summary>
    /// Invokes the chat completion service with the provided chat history.
    /// </summary>
    /// <param name="chatHistory">The chat history to send</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The AI-generated response text</returns>
    protected async Task<string> GetChatCompletionAsync(ChatHistory chatHistory, CancellationToken cancellationToken = default)
    {
        var chatCompletionService = Kernel.GetRequiredService<IChatCompletionService>();
        var executionSettings = new OpenAIPromptExecutionSettings
        {
            // Enable automatic function calling - this allows the AI to invoke kernel functions
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
        };

        var response = await chatCompletionService.GetChatMessageContentsAsync(
            chatHistory,
            executionSettings,
            Kernel,
            cancellationToken: cancellationToken);

        var assistantMessage = response.FirstOrDefault();
        var responseText = assistantMessage?.Content ?? "I apologize, but I couldn't generate a response.";

        Logger.LogDebug("Generated AI response of length {Length} characters", responseText.Length);
        return responseText;
    }
}
