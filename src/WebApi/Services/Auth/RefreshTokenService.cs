using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebApi.Configuration.Options;
using WebApi.Data;
using WebApi.Models;

namespace WebApi.Services.Auth;

public class RefreshTokenService(AppDbContext context, IOptions<JwtSettings> jwtSettings)
{
    public async Task<string> GenerateRefreshTokenAsync(User user)
    {
        var refreshTokenExpirationDays = jwtSettings.Value.RefreshTokenExpirationDays;
        
        // Revoke all existing refresh tokens for this user (token rotation)
        await RevokeAllUserTokensAsync(user.Id, "New token issued");
        
        var token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
        var expiresAt = DateTime.UtcNow.AddDays(refreshTokenExpirationDays);
        
        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = token,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow
        };

        context.RefreshTokens.Add(refreshToken);
        await context.SaveChangesAsync();

        return token;
    }

    public async Task<RefreshToken?> GetRefreshTokenAsync(string token)
    {
        return await context.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == token);
    }

    public async Task RevokeRefreshTokenAsync(string token, string? reason = null)
    {
        var refreshToken = await GetRefreshTokenAsync(token);
        if (refreshToken != null && refreshToken.RevokedAt == null)
        {
            refreshToken.RevokedAt = DateTime.UtcNow;
            refreshToken.RevocationReason = reason;
            await context.SaveChangesAsync();
        }
    }

    public async Task RevokeAllUserTokensAsync(Guid userId, string? reason = null)
    {
        var activeTokens = await context.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAt == null && rt.ExpiresAt > DateTime.UtcNow)
            .ToListAsync();

        foreach (var token in activeTokens)
        {
            token.RevokedAt = DateTime.UtcNow;
            token.RevocationReason = reason;
        }

        await context.SaveChangesAsync();
    }

    public async Task<bool> IsTokenValidAsync(string token)
    {
        var refreshToken = await GetRefreshTokenAsync(token);
        
        if (refreshToken == null)
        {
            return false;
        }

        if (refreshToken.RevokedAt != null)
        {
            return false;
        }

        if (refreshToken.ExpiresAt <= DateTime.UtcNow)
        {
            return false;
        }

        return true;
    }
}
