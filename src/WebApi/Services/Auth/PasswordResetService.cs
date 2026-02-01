using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using WebApi.Data;
using WebApi.Models;
using WebApi.Services.Email;
using WebApi.Services.Validation;

namespace WebApi.Services.Auth;

public class PasswordResetService(
    AppDbContext context,
    RenderMjmlEmailService emailService,
    PasswordValidator passwordValidator,
    string frontendBaseUrl)
{
    public async Task CreatePasswordResetRequest(string email)
    {
        var emailProcessed = email.Trim();
        // Only allow password reset for Local provider users
        var user = await context.Users.FirstOrDefaultAsync(u => 
            u.Provider == AuthProvider.Local && u.Email == emailProcessed);
        if (user is null)
            return; // Don't reveal if email exists for security
        
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var created = DateTime.UtcNow;
        var willExpireOn = created + TimeSpan.FromHours(1);

        var passwordResetRequest = new PasswordResetRequest
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = token,
            ExpiresAt = willExpireOn,
            CreatedAt = created,
            User = user
        };

        await context.PasswordResetRequests.AddAsync(passwordResetRequest);
        await context.SaveChangesAsync();
        
        var baseUri = new Uri(frontendBaseUrl);
        var resetUrl = new Uri(baseUri, "/auth/password-reset?token=" + Uri.EscapeDataString(token)).ToString();
        await emailService.SendPasswordResetEmailAsync(email, resetUrl, CancellationToken.None);
    }
    
    public async Task<PasswordResetResult> PerformPasswordResetRequest(string token, string newPassword)
    {
        // Validate password first
        var (isValid, errorMessage) = passwordValidator.ValidatePassword(newPassword);
        if (!isValid)
        {
            return PasswordResetResult.Failure(errorMessage ?? "Invalid password");
        }

        var prr = await context.PasswordResetRequests
            .AsTracking()
            .Include(prr => prr.User)
            .FirstOrDefaultAsync(prr => prr.Token == token);
        
        if (prr is null)
        {
            return PasswordResetResult.Failure("Invalid or expired reset token");
        }

        if (prr.ExpiresAt < DateTime.UtcNow)
        {
            context.PasswordResetRequests.Remove(prr);
            await context.SaveChangesAsync();
            return PasswordResetResult.Failure("Reset token has expired");
        }

        var user = prr.User;
        
        // Only allow password reset for Local provider users
        if (user.Provider != AuthProvider.Local)
        {
            context.PasswordResetRequests.Remove(prr);
            await context.SaveChangesAsync();
            return PasswordResetResult.Failure("Password reset is not available for this account type");
        }
        
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, workFactor: 12);
        user.PasswordHash = passwordHash;
        user.UpdatedAt = DateTime.UtcNow;

        context.PasswordResetRequests.Remove(prr);
        await context.SaveChangesAsync();
        
        return PasswordResetResult.Success();
    }
}

public record PasswordResetResult(bool IsSuccess, string? ErrorMessage)
{
    public static PasswordResetResult Success() => new(true, null);
    public static PasswordResetResult Failure(string errorMessage) => new(false, errorMessage);
}