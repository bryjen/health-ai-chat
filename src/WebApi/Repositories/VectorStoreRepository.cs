using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Pgvector;
using WebApi.Data;
using WebApi.Models;

namespace WebApi.Repositories;

public class VectorStoreRepository(AppDbContext context)
{
    public async Task StoreEmbeddingAsync(Guid messageId, Guid userId, float[] embedding)
    {
        // Convert float[] to Vector type for storage
        var vector = new Vector(embedding);
        
        // Check if embedding already exists for this message
        var existing = await context.MessageEmbeddings
            .FirstOrDefaultAsync(e => e.MessageId == messageId);

        if (existing != null)
        {
            existing.Embedding = vector;
            await context.SaveChangesAsync();
            return;
        }

        var messageEmbedding = new MessageEmbedding
        {
            Id = Guid.NewGuid(),
            MessageId = messageId,
            UserId = userId,
            Embedding = vector,
            CreatedAt = DateTime.UtcNow
        };

        context.MessageEmbeddings.Add(messageEmbedding);
        await context.SaveChangesAsync();
    }

    public async Task<List<Message>> GetMessagesByConversationAsync(Guid conversationId)
    {
        return await context.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Searches for messages similar to the query embedding, excluding messages from a specific conversation.
    /// Returns messages ordered by cosine similarity (highest first).
    /// </summary>
    public async Task<List<(Message Message, double Similarity)>> SearchSimilarMessagesAsync(
        Guid userId,
        float[] queryEmbedding,
        Guid? excludeConversationId,
        int limit,
        double minSimilarity = 0.7)
    {
        var queryVector = new Vector(queryEmbedding);
        var results = new List<(Message Message, double Similarity)>();

        // Use raw SQL for vector similarity search with pgvector
        // EF Core doesn't support the <=> operator directly, so we need raw SQL
        // Create a new connection to avoid conflicts with EF Core's connection management
        var dbConnection = context.Database.GetDbConnection();
        var connectionString = dbConnection.ConnectionString;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        try
        {
            var sql = @"
                SELECT 
                    m.""Id"",
                    m.""ConversationId"",
                    m.""Role"",
                    m.""Content"",
                    m.""CreatedAt"",
                    1 - (me.""Embedding"" <=> @queryVector::vector) as similarity
                FROM ""conuhacks"".""MessageEmbeddings"" me
                INNER JOIN ""conuhacks"".""Messages"" m ON me.""MessageId"" = m.""Id""
                WHERE me.""UserId"" = @userId
                    AND (@excludeConversationId IS NULL OR m.""ConversationId"" != @excludeConversationId)
                    AND 1 - (me.""Embedding"" <=> @queryVector::vector) >= @minSimilarity
                ORDER BY me.""Embedding"" <=> @queryVector::vector
                LIMIT @limit";

            await using var command = new NpgsqlCommand(sql, connection);
            
            // Set parameters
            // For Vector type, pass name and value - Npgsql will handle it via pgvector extension
            var vectorParam = new NpgsqlParameter("queryVector", queryVector);
            command.Parameters.Add(vectorParam);
            command.Parameters.AddWithValue("userId", userId);
            command.Parameters.AddWithValue("excludeConversationId", excludeConversationId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("minSimilarity", minSimilarity);
            command.Parameters.AddWithValue("limit", limit);

            await using var reader = await command.ExecuteReaderAsync();
            
            while (await reader.ReadAsync())
            {
                var message = new Message
                {
                    Id = reader.GetGuid(0),
                    ConversationId = reader.GetGuid(1),
                    Role = reader.GetString(2),
                    Content = reader.GetString(3),
                    CreatedAt = reader.GetDateTime(4)
                };
                
                var similarity = reader.GetDouble(5);
                results.Add((message, similarity));
            }
        }
        finally
        {
            await connection.CloseAsync();
        }

        return results;
    }
}
