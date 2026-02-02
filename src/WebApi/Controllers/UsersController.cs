using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Common.DTOs;
using Web.Common.DTOs.Auth;
using WebApi.Controllers.Utils;
using WebApi.Exceptions;
using WebApi.Services.Auth;

namespace WebApi.Controllers;

/// <summary>
/// Handles user profile management endpoints.
/// Provides access to retrieve and update the authenticated user's profile information.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    /// <summary>
    /// Retrieves the profile information of the currently authenticated user.
    /// </summary>
    /// <returns>Complete user profile information.</returns>
    /// <response code="200">User profile retrieved successfully.</response>
    /// <response code="401">User not authenticated or invalid token.</response>
    /// <response code="404">User not found in the system.</response>
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserProfileDto>> GetMeAsync(
        [FromServices] AuthService authService)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
        {
            return this.UnauthorizedError("Invalid token");
        }

        try
        {
            var profile = await authService.GetProfileAsync(userId);
            return Ok(profile);
        }
        catch (NotFoundException ex)
        {
            return this.NotFoundError(ex.Message);
        }
    }

    /// <summary>
    /// Updates the profile information of the currently authenticated user.
    /// </summary>
    /// <param name="request">The updated profile information.</param>
    /// <returns>The updated user profile information.</returns>
    /// <response code="200">User profile updated successfully.</response>
    /// <response code="400">Invalid input data or validation failed.</response>
    /// <response code="401">User not authenticated or invalid token.</response>
    /// <response code="404">User not found in the system.</response>
    [HttpPut("me")]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserProfileDto>> UpdateMeAsync(
        [FromBody] UpdateUserProfileRequest request,
        [FromServices] AuthService authService)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
        {
            return this.UnauthorizedError("Invalid token");
        }

        try
        {
            var profile = await authService.UpdateProfileAsync(userId, request);
            return Ok(profile);
        }
        catch (NotFoundException ex)
        {
            return this.NotFoundError(ex.Message);
        }
    }
}
