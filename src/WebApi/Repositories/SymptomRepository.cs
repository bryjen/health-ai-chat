using Microsoft.EntityFrameworkCore;
using WebApi.Data;
using WebApi.Models;

namespace WebApi.Repositories;

public class SymptomRepository(AppDbContext context)
{
    public async Task<List<Symptom>> GetSymptomsAsync(Guid userId)
    {
        return await context.Symptoms
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync();
    }

    public async Task<Symptom> GetOrCreateSymptomAsync(Guid userId, string symptomName, string? description = null)
    {
        var existing = await context.Symptoms
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Name == symptomName);

        if (existing != null)
        {
            if (description != null && existing.Description != description)
            {
                existing.Description = description;
                existing.UpdatedAt = DateTime.UtcNow;
                await context.SaveChangesAsync();
            }
            return existing;
        }

        var symptom = new Symptom
        {
            UserId = userId,
            Name = symptomName,
            Description = description,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        context.Symptoms.Add(symptom);
        await context.SaveChangesAsync();
        return symptom;
    }

    public async Task<bool> DeleteAsync(Guid userId, string symptomName)
    {
        var symptom = await context.Symptoms
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Name == symptomName);

        if (symptom == null)
        {
            return false;
        }

        context.Symptoms.Remove(symptom);
        await context.SaveChangesAsync();
        return true;
    }
}
