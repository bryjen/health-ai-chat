using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using WebApi.Data;
using Web.Common.DTOs.Conversations;
using WebApi.Exceptions;
using WebApi.Models;

namespace WebApi.Services.Chat;

public class ConversationService(AppDbContext context)
{
    public async Task<List<ConversationSummaryDto>> GetAllConversationsAsync(Guid? userId = null)
    {
        var query = context.Conversations.AsQueryable();

        if (userId.HasValue)
        {
            query = query.Where(c => c.UserId == userId);
        }

        var conversations = await query
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync();

        var summaries = new List<ConversationSummaryDto>();

        foreach (var conversation in conversations)
        {
            // Get the last message for preview
            var lastMessage = await context.Messages
                .Where(m => m.ConversationId == conversation.Id)
                .OrderByDescending(m => m.CreatedAt)
                .FirstOrDefaultAsync();

            summaries.Add(new ConversationSummaryDto
            {
                Id = conversation.Id,
                Title = conversation.Title,
                LastMessagePreview = lastMessage?.Content?.Length > 100 
                    ? lastMessage.Content.Substring(0, 100) + "..." 
                    : lastMessage?.Content,
                UpdatedAt = conversation.UpdatedAt
            });
        }

        return summaries;
    }

    public async Task<ConversationDto?> GetConversationByIdAsync(Guid conversationId, Guid? userId = null)
    {
        var query = context.Conversations
            .Include(c => c.Messages)
            .AsQueryable();

        if (userId.HasValue)
        {
            query = query.Where(c => c.UserId == userId);
        }

        var conversation = await query
            .FirstOrDefaultAsync(c => c.Id == conversationId);

        if (conversation == null)
        {
            return null;
        }

        conversation.Messages = OrderMessagesChronologically(conversation.Messages).ToList();

        return MapToDto(conversation);
    }

    public async Task<ConversationDto> UpdateConversationTitleAsync(Guid conversationId, string title, Guid? userId = null)
    {
        var query = context.Conversations.AsQueryable();

        if (userId.HasValue)
        {
            query = query.Where(c => c.UserId == userId);
        }

        var conversation = await query
            .FirstOrDefaultAsync(c => c.Id == conversationId);

        if (conversation == null)
        {
            throw new NotFoundException("Conversation not found");
        }

        conversation.Title = title;
        conversation.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();

        // Reload with messages for DTO mapping
        await context.Entry(conversation).Collection(c => c.Messages).LoadAsync();
        conversation.Messages = OrderMessagesChronologically(conversation.Messages).ToList();

        return MapToDto(conversation);
    }

    public async Task<bool> DeleteConversationAsync(Guid conversationId, Guid? userId = null)
    {
        var query = context.Conversations.AsQueryable();

        if (userId.HasValue)
        {
            query = query.Where(c => c.UserId == userId);
        }

        var conversation = await query
            .FirstOrDefaultAsync(c => c.Id == conversationId);

        if (conversation == null)
        {
            throw new NotFoundException("Conversation not found");
        }

        context.Conversations.Remove(conversation);
        await context.SaveChangesAsync();

        return true;
    }

    private static ConversationDto MapToDto(Conversation conversation)
    {
        return new ConversationDto
        {
            Id = conversation.Id,
            Title = conversation.Title,
            Messages = conversation.Messages.Select(m => new MessageDto
            {
                Id = m.Id,
                Role = m.Role,
                Content = m.Content,
                CreatedAt = m.CreatedAt,
                StatusInformationJson = m.StatusInformationJson
            }).ToList(),
            CreatedAt = conversation.CreatedAt,
            UpdatedAt = conversation.UpdatedAt
        };
    }

    private static IEnumerable<Message> OrderMessagesChronologically(IEnumerable<Message> messages)
    {
        return messages
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => GetRoleSortOrder(m.Role))
            .ThenBy(m => m.Id);
    }

    private static int GetRoleSortOrder(string role)
    {
        if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 2;
    }
}
