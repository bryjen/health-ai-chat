namespace WebApi.Configuration.Options;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable UnusedMember.Global

public enum AgentProvider
{
    MicrosoftFoundry,
    Anthropic
}

/// <summary>
/// AI provider configuration options.
/// </summary>
public class AiOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "AiOptions";

    /// <summary>
    /// Microsoft Foundry (Azure OpenAI) settings.
    /// </summary>
    public MicrosoftFoundryOptions MicrosoftFoundry { get; set; } = new();

    /// <summary>
    /// Anthropic (Claude) settings.
    /// </summary>
    public AnthropicOptions Anthropic { get; set; } = new();
}

/// <summary>
/// Microsoft Foundry (Azure OpenAI) configuration settings.
/// </summary>
public class MicrosoftFoundryOptions
{
    /// <summary>
    /// Azure OpenAI resource endpoint URL (e.g. https://your-resource.openai.azure.com/).
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Azure OpenAI resource API key.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Deployment name for the model (e.g. gpt-4o-mini).
    /// This is the actual deployment name in your Azure OpenAI resource, not the model name.
    /// </summary>
    public string ModelDeploymentName { get; set; } = string.Empty;
}

/// <summary>
/// Anthropic (Claude) API configuration settings.
/// </summary>
public class AnthropicOptions
{
    /// <summary>
    /// Anthropic API key.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Default Claude model to use (e.g. claude-sonnet-4-20250514).
    /// </summary>
    public string DefaultModel { get; set; } = string.Empty;
}
