using System.Diagnostics.CodeAnalysis;

namespace WebApi.Configuration.Options;

/// <summary>
/// Azure OpenAI settings for Semantic Kernel.
/// Uses a standard Azure OpenAI resource endpoint and API key.
/// </summary>
[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
public class AzureOpenAiSettings
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "AzureOpenAI";
    
    /// <summary>
    /// Azure OpenAI resource endpoint URL (e.g. https://your-resource.openai.azure.com/).
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;
    
    /// <summary>
    /// Azure OpenAI resource API key.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
    
    /// <summary>
    /// Deployment name for the model (e.g. gpt-4.1-mini).
    /// This is the actual deployment name in your Azure OpenAI resource, not the model name.
    /// </summary>
    public string DeploymentName { get; set; } = string.Empty;
    
    /// <summary>
    /// Optional deployment name for embeddings (e.g. text-embedding-3-small for 1536 dimensions, or text-embedding-3-large for 3072 dimensions).
    /// If not specified, uses DeploymentName.
    /// </summary>
    public string? EmbeddingDeploymentName { get; set; }
}
