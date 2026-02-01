using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using WebApi.Data;
using WebApi.Models;
using WebApi.Repositories;

namespace WebApi.Services.VectorStore;

public class VectorStoreService(
    Kernel kernel,
    VectorStoreRepository vectorStoreRepository,
    AppDbContext context,
    ILogger<VectorStoreService> logger)
{
    public async Task StoreMessageAsync(Guid userId, Guid messageId, string messageContent)
    {
        try
        {
            // Generate embedding using Semantic Kernel
            var embeddingService = kernel.GetRequiredService<ITextEmbeddingGenerationService>();
            var embeddingData = await embeddingService.GenerateEmbeddingAsync(messageContent);
            
            // Convert to float array
            var vector = embeddingData.ToArray();
            
            // Validate embedding dimensions (database expects 1536 for vector(1536))
            const int expectedDimensions = 1536;
            if (vector.Length != expectedDimensions)
            {
                throw new InvalidOperationException(
                    $"Embedding dimension mismatch: Expected {expectedDimensions} dimensions (for text-embedding-3-small), " +
                    $"but got {vector.Length} dimensions. Please ensure 'AzureOpenAI:EmbeddingDeploymentName' is set to a " +
                    $"deployment that produces {expectedDimensions}-dimensional embeddings (e.g., text-embedding-3-small).");
            }
            
            await vectorStoreRepository.StoreEmbeddingAsync(messageId, userId, vector);
            
            logger.LogDebug("Stored embedding for message {MessageId}", messageId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error storing embedding for message {MessageId}", messageId);
            throw;
        }
    }

    public async Task<List<Message>> GetConversationMessagesAsync(Guid conversationId)
    {
        try
        {
            var messages = await vectorStoreRepository.GetMessagesByConversationAsync(conversationId);
            logger.LogDebug("Retrieved {Count} messages from conversation {ConversationId}", messages.Count, conversationId);
            return messages;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving messages from conversation {ConversationId}", conversationId);
            throw;
        }
    }

    /// <summary>
    /// Searches for semantically similar messages from other conversations.
    /// Generates an embedding for the query text and searches for similar messages.
    /// </summary>
    public async Task<List<Message>> SearchSimilarMessagesAsync(
        Guid userId,
        string queryText,
        Guid? excludeConversationId,
        int limit,
        double minSimilarity = 0.7)
    {
        try
        {
            // Generate embedding for the query text
            var embeddingService = kernel.GetRequiredService<ITextEmbeddingGenerationService>();
            var embeddingData = await embeddingService.GenerateEmbeddingAsync(queryText);
            
            var queryVector = embeddingData.ToArray();
            
            // Validate embedding dimensions
            const int expectedDimensions = 1536;
            if (queryVector.Length != expectedDimensions)
            {
                throw new InvalidOperationException(
                    $"Embedding dimension mismatch: Expected {expectedDimensions} dimensions, but got {queryVector.Length}.");
            }
            
            // Search for similar messages
            var results = await vectorStoreRepository.SearchSimilarMessagesAsync(
                userId,
                queryVector,
                excludeConversationId,
                limit,
                minSimilarity);
            
            logger.LogDebug(
                "Found {Count} similar messages for user {UserId} (excluded conversation: {ExcludeConversationId})",
                results.Count,
                userId,
                excludeConversationId);
            
            // Return just the messages (without similarity scores)
            return results.Select(r => r.Message).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error searching for similar messages for user {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Deletes all message embeddings. Use this to fix dimension mismatches.
    /// Embeddings will be regenerated automatically when new messages are sent.
    /// </summary>
    public async Task<int> ClearAllEmbeddingsAsync()
    {
        var count = await context.MessageEmbeddings.CountAsync();
        context.MessageEmbeddings.RemoveRange(context.MessageEmbeddings);
        await context.SaveChangesAsync();
        logger.LogWarning("Cleared {Count} message embeddings. They will be regenerated with correct dimensions on next message.", count);
        return count;
    }
}
