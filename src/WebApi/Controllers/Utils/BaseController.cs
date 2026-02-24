using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace WebApi.Controllers.Utils;

/// <summary>
/// Represents the currently authenticated user from JWT claims
/// </summary>
public record CurrentUser(Guid UserId, string Email);

/// <summary>
/// Base controller providing common functionality for authenticated controllers
/// </summary>
[Authorize]
[ApiController]
[EnableRateLimiting("authenticated")]
public abstract class BaseController
    : ControllerBase
{
    /// <summary>
    /// Gets the current authenticated user from JWT token claims
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Thrown when user claims are missing or invalid</exception>
    protected CurrentUser CurrentUser
    {
        get
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var emailClaim = User.FindFirst(ClaimTypes.Email)?.Value;

            if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
            {
                throw new UnauthorizedAccessException("Invalid token: user ID claim is missing or invalid");
            }

            if (string.IsNullOrWhiteSpace(emailClaim))
            {
                throw new UnauthorizedAccessException("Invalid token: email claim is missing");
            }

            return new CurrentUser(userId, emailClaim);
        }
    }

    /// <summary>
    /// Gets the current user's ID from the JWT token claims
    /// </summary>
    /// <returns>The user's GUID</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when user ID claim is missing or invalid</exception>
    protected Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("Invalid token: user ID claim is missing or invalid");
        }

        return userId;
    }
}
