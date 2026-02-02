using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Web.Common.DTOs;
using Web.Common.DTOs.Auth;
using WebApi.Controllers.Utils;
using WebApi.Exceptions;
using WebApi.Models;
using WebApi.Services.Auth;

// disabled to avoid no xml docs on injected services as parameters
#pragma warning disable CS1573 // Parameter has no matching param tag in the XML comment (but other parameters do)

namespace WebApi.Controllers.Core;

/// <summary>
/// ***Handles user authentication, registration, and account management***.
/// Provides endpoints for email/password authentication, OAuth login, token refresh, password reset, and user profile retrieval.
/// <br/><br/><br/>
/// For other user-related operations, such as personal information management, user rate calculations, etc., see the `Users` controller/grouping.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    /// <summary>
    /// User Registration
    /// </summary>
    /// <param name="request">User registration information including email and password.</param>
    /// <returns>Authentication response containing user details, access token, and refresh token.</returns>
    /// <response code="201"> User successfully registered.</response>
    /// <response code="400"> Invalid input data or validation failed. **Returns standardized `ErrorResponse` model**.</response>
    /// <response code="409"> User already exists with the provided email address. **Returns standardized `ErrorResponse` model**.</response>
    /// <remarks>
    /// Creates a new user account using email and password credentials. On a success, access and refresh tokens are returned.
    ///
    /// Passwords must be:
    /// - at least 12 characters long
    /// - contain a combination of at least one uppercase and lowercase letter, a number, and a special character
    ///
    /// Access tokens expire after 1 hour by default, while refresh tokens expire after 7 days.
    /// </remarks>
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> Register(
        [FromBody] RegisterRequest request,
        [FromServices] AuthService authService)
    {
        try
        {
            var response = await authService.RegisterAsync(request);
            return CreatedAtAction(nameof(GetCurrentUser), response);
        }
        catch (ValidationException ex)
        {
            return this.BadRequestError(ex.Message);
        }
        catch (ConflictException ex)
        {
            return this.ConflictError(ex.Message);
        }
    }

    /// <summary>
    /// User Login
    /// </summary>
    /// <param name="request">Login credentials containing email and password.</param>
    /// <response code="200">Login successful.</response>
    /// <response code="401">Invalid credentials or authentication failed. **Returns standardized `ErrorResponse` model**.</response>
    /// <remarks>
    /// Authenticates a user using their email and password. Returns access and refresh tokens upon successful authentication.
    /// Access tokens expire after 1 hour by default, while refresh tokens expire after 7 days.
    ///
    /// This endpoint is rate-limited to prevent brute force attacks.
    /// </remarks>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login(
        [FromBody] LoginRequest request,
        [FromServices] AuthService authService)
    {
        try
        {
            var response = await authService.LoginAsync(request);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return this.UnauthorizedError(ex.Message);
        }
    }

    /// <summary>
    /// Access Token refresh using Refresh Token
    /// </summary>
    /// <param name="request">Refresh token request containing the current refresh token.</param>
    /// <response code="200">Token refreshed successfully.</response>
    /// <response code="400">Invalid or expired refresh token. **Returns standardized `ErrorResponse` model**.</response>
    /// <remarks>
    /// Refreshes an expired access token using a valid refresh token. Returns new access and refresh tokens.
    /// Refresh tokens are single-use and must be replaced with the newly issued token.
    /// If the refresh token is expired or invalid, the user must log in again.
    /// Call this endpoint when the access token expires, typically after 1 hour.
    /// </remarks>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthResponse>> RefreshToken(
        [FromBody] RefreshTokenRequest request,
        [FromServices] AuthService authService)
    {
        try
        {
            var response = await authService.RefreshTokenAsync(request.RefreshToken);
            return Ok(response);
        }
        catch (ValidationException ex)
        {
            return this.BadRequestError(ex.Message);
        }
    }

    /// <summary>
    /// Retrieves Current User Profile
    /// </summary>
    /// <response code="200">User information retrieved successfully.</response>
    /// <response code="401">User not authenticated or invalid token. **Returns standardized `ErrorResponse` model**.</response>
    /// <response code="404">User wasn't found in the system. **Returns standardized `ErrorResponse` model**.</response>
    /// <remarks>
    /// Retrieves the profile information of the currently authenticated user.
    /// Requires a valid JWT access token in the Authorization header.
    ///
    /// Remarks:
    /// - This endpoint can be used to verify token validity and obtain current user information.
    /// - The user ID from the response can be used for user-specific operations throughout the API.
    /// </remarks>
    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> GetCurrentUser(
        [FromServices] AuthService authService)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
        {
            return this.UnauthorizedError("Invalid token");
        }

        var user = await authService.GetUserByIdAsync(userId);

        if (user == null)
        {
            return this.NotFoundError("User not found");
        }

        return Ok(user);
    }

    /// <summary>
    /// Creates a Password Reset Request
    /// </summary>
    /// <param name="request">Email address for password reset.</param>
    /// <response code="200">Password reset email sent if email exists.</response>
    /// <response code="400">Email address is required. **Returns standardized `ErrorResponse` model**.</response>
    /// <remarks>
    /// Initializes a password reset request. This endpoint essentially creates a "password reset token", which is used
    /// in the `password-reset/confirm` endpoint to complete the reset request.
    /// **This is a 2-step process and tightly coupled to the frontend**. As the writing of this, the current
    /// implementation sends an email with a link to a page in the frontend which completes the request.
    ///
    /// This endpoint always returns success to prevent attackers from determining which emails are registered.
    /// </remarks>
    [HttpPost("password-reset/request")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> RequestPasswordReset(
        [FromBody] PasswordResetRequestDto request,
        [FromServices] PasswordResetService passwordResetService)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return this.BadRequestError("Email is required");
        }

        await passwordResetService.CreatePasswordResetRequest(request.Email);
        return Ok(new { message = "If an account with that email exists, a password reset link has been sent." });
    }

    /// <summary>
    /// Completes a Password Reset Request
    /// </summary>
    /// <param name="request">Password reset confirmation containing the token from email and new password.</param>
    /// <response code="200">Password reset successful.</response>
    /// <response code="400">Invalid token, expired token, or password validation failed. **Returns standardized `ErrorResponse` model**.</response>
    /// <remarks>
    /// Completes a password reset request. This endpoint changes a user's password given a valid "password reset token".
    /// For more information, see the `password-reset/request` endpoint which creates the token.
    ///
    /// The rules for the password follow the same rules as registration:
    /// - at least 12 characters long
    /// - contain a combination of at least one uppercase and lowercase letter, a number, and a special character
    ///
    /// After a successful password reset, the old password is immediately invalidated and the user should log in with the new password.
    /// If the token is expired or invalid, request a new password reset email.
    /// </remarks>
    [HttpPost("password-reset/confirm")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> ConfirmPasswordReset(
        [FromBody] ConfirmPasswordResetRequestDto request,
        [FromServices] PasswordResetService passwordResetService)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return this.BadRequestError("Token is required");
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return this.BadRequestError("New password is required");
        }

        var result = await passwordResetService.PerformPasswordResetRequest(request.Token, request.NewPassword);

        if (!result.IsSuccess)
        {
            return this.BadRequestError(result.ErrorMessage ?? "Invalid or expired token");
        }

        return Ok(new { message = "Password has been reset successfully. You can now log in with your new password." });
    }

    /// <summary>
    /// OAuth Login/Registration
    /// </summary>
    /// <param name="request">OAuth login request containing provider name and either ID token or authorization code.</param>
    /// <response code="200">OAuth login successful.</response>
    /// <response code="400">Invalid request, missing parameters, or unsupported provider. **Returns standardized `ErrorResponse` model**.</response>
    /// <response code="401">Token validation failed or invalid credentials. **Returns standardized `ErrorResponse` model**.</response>
    /// <response code="409">Account conflict - email already exists with a different authentication provider. **Returns standardized `ErrorResponse` model**.</response>
    /// <remarks>
    /// Authenticates users using OAuth providers. Supports **Google**, **Microsoft**, and **GitHub**.
    /// If the user doesn't exist, **a new account is automatically created** using the email from the OAuth provider.
    ///
    /// **Remarks**:
    /// - For Google and Microsoft, use the ID Token Flow by providing the `id_token` obtained from the OAuth provider.
    /// - For GitHub, use the Authorization Code Flow by providing the `authorization_code`.
    /// - The `redirect_uri` must match the one used during the OAuth authorization request.
    ///
    /// <br/>
    /// <br/>
    ///
    /// **Google (ID Token Flow):**
    /// ```json
    /// {
    ///   "provider": "Google",
    ///   "id_token": "eyJhbGciOiJSUzI1NiIsImtpZCI6IjEyMzQ1Njc4OTAifQ.eyJpc3MiOiJodHRwczovL2FjY291bnRzLmdvb2dsZS5jb20iLCJzdWIiOiIxMTIyMzM0NDU1NjY3Nzg4OTkiLCJlbWFpbCI6ImpvaG4uZG9lQGdtYWlsLmNvbSIsImV4cCI6MTcwNTMyNDAwMH0..."
    /// }
    /// ```
    ///
    /// **Microsoft (ID Token Flow):**
    /// ```json
    /// {
    ///   "provider": "Microsoft",
    ///   "id_token": "eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiIsIng1dCI6IjEyMzQ1Njc4OTAifQ.eyJpc3MiOiJodHRwczovL2xvZ2luLm1pY3Jvc29mdG9ubGluZS5jb20iLCJzdWIiOiIxMTIyMzM0NDU1NjY3Nzg4OTkiLCJlbWFpbCI6ImpvaG4uZG9lQG91dGxvb2suY29tIiwiZXhwIjoxNzA1MzI0MDAwfQ..."
    /// }
    /// ```
    ///
    /// **GitHub (Authorization Code Flow):**
    /// ```json
    /// {
    ///   "provider": "GitHub",
    ///   "authorization_code": "abc123def456ghi789",
    ///   "redirect_uri": "https://localhost:5000/login?provider=GitHub"
    /// }
    /// ```
    ///
    /// All providers return the same response format with user details, access token, and refresh token.
    /// Store the returned tokens securely for authenticated API requests.
    /// </remarks>
    [HttpPost("oauth")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> OAuthLogin(
        [FromBody] OAuthLoginRequest request,
        [FromServices] AuthService authService)
    {
        if (string.IsNullOrWhiteSpace(request.Provider))
        {
            return this.BadRequestError("Provider is required");
        }

        if (string.IsNullOrWhiteSpace(request.IdToken) && string.IsNullOrWhiteSpace(request.AuthorizationCode))
        {
            return this.BadRequestError("Either IdToken or AuthorizationCode is required");
        }

        if (!Enum.TryParse<AuthProvider>(request.Provider, ignoreCase: true, out var provider))
        {
            return this.BadRequestError($"Invalid provider '{request.Provider}'. Supported providers: {string.Join(", ", Enum.GetNames<AuthProvider>().Where(p => p != "Local"))}");
        }

        if (provider == AuthProvider.Local)
        {
            return this.BadRequestError("Local provider is not supported for OAuth login");
        }

        try
        {
            var response = await authService.LoginWithOAuthAsync(
                provider,
                idToken: request.IdToken,
                authorizationCode: request.AuthorizationCode,
                redirectUri: request.RedirectUri);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return this.UnauthorizedError(ex.Message);
        }
        catch (ConflictException ex)
        {
            return this.ConflictError(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return this.BadRequestError(ex.Message);
        }
        catch (NotSupportedException ex)
        {
            return this.BadRequestError(ex.Message);
        }
    }

    public record PasswordResetRequestDto(string Email);
    public record ConfirmPasswordResetRequestDto(string Token, string NewPassword);
}
