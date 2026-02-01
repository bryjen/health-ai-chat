using System.Diagnostics.CodeAnalysis;

namespace WebApi.Configuration.Options;

/// <summary>
/// Vector store settings for message embeddings and semantic search.
/// </summary>
[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
public class VectorStoreSettings
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "VectorStore";
    
    /// <summary>
    /// Maximum number of context messages to include from the current conversation.
    /// If 0 or negative, includes all messages. Default: 0 (all messages).
    /// </summary>
    public int MaxContextMessages { get; set; } = 0;
    
    /// <summary>
    /// Enable cross-conversation semantic search to find relevant messages from other conversations.
    /// When enabled, searches for semantically similar messages from past conversations when the current
    /// conversation is short or when additional context would be helpful.
    /// Default: false.
    /// </summary>
    public bool EnableCrossConversationSearch { get; set; } = false;
    
    /// <summary>
    /// Maximum number of messages to retrieve from other conversations via semantic search.
    /// Only used when EnableCrossConversationSearch is true.
    /// Default: 5.
    /// </summary>
    public int MaxCrossConversationResults { get; set; } = 5;
    
    /// <summary>
    /// Minimum conversation message count threshold to trigger cross-conversation search.
    /// If the current conversation has fewer messages than this threshold, cross-conversation
    /// search will be performed (if enabled). Default: 5.
    /// </summary>
    public int CrossConversationSearchThreshold { get; set; } = 5;
    
    /// <summary>
    /// Minimum similarity score (0.0 to 1.0) for cross-conversation search results.
    /// Messages with similarity scores below this threshold will be excluded.
    /// Default: 0.7.
    /// </summary>
    public double MinSimilarityScore { get; set; } = 0.7;
}
