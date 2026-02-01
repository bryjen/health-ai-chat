using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Web.Common.DTOs;
using Web.Common.DTOs.Auth;
using WebApi.Controllers.Utils;
using WebApi.Exceptions;
using WebApi.Models;
using WebApi.Services.Auth;

namespace WebApi.Controllers.Core;

/// <summary>
/// Handles user authentication and registration
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    /// <summary>
    /// Register a new user account
    /// </summary>
    /// <param name="request">User registration information including email and password</param>
    /// <returns>Authentication response with user details, access token, and refresh token</returns>
    /// <response code="201">User successfully registered</response>
    /// <response code="400">Invalid input (validation failed)</response>
    /// <response code="409">User already exists with this email</response>
    /// <remarks>
    /// Creates a new user account with email and password authentication. Upon successful registration, you'll receive access and refresh tokens for immediate authentication.
    /// 
    /// **Password Requirements:**
    /// - Minimum 12 characters
    /// - At least one uppercase letter (A-Z)
    /// - At least one lowercase letter (a-z)
    /// - At least one number (0-9)
    /// - At least one special character (!@#$%^&amp;*()_+-=[]{}|;:,./&lt;&gt;?)
    /// 
    /// **Example Request:**
    /// ```
    /// POST /api/v1/auth/register
    /// Content-Type: application/json
    /// 
    /// {
    ///   "email": "john.doe@example.com",
    ///   "password": "SecurePass123!"
    /// }
    /// ```
    /// 
    /// **Example Response (201 Created):**
    /// ```json
    /// {
    ///   "user": {
    ///     "id": "550e8400-e29b-41d4-a716-446655440000",
    ///     "email": "john.doe@example.com",
    ///     "createdAt": "2024-01-15T10:00:00Z"
    ///   },
    ///   "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiI1NTBlODQwMC1lMjliLTQxZDQtYTcxNi00NDY2NTU0NDAwMDAiLCJlbWFpbCI6ImpvaG4uZG9lQGV4YW1wbGUuY29tIiwiaWF0IjoxNzA1MzI0MDAwLCJleHAiOjE3MDUzMjc2MDB9...",
    ///   "refreshToken": "base64_encoded_refresh_token_here",
    ///   "accessTokenExpiresAt": "2024-01-15T11:00:00Z",
    ///   "refreshTokenExpiresAt": "2024-01-22T10:00:00Z"
    /// }
    /// ```
    /// 
    /// **Example Error Response (400 Bad Request - Invalid Password):**
    /// ```json
    /// {
    ///   "error": "Bad Request",
    ///   "message": "Password must be at least 12 characters long and contain at least one uppercase letter, one lowercase letter, one number, and one special character"
    /// }
    /// ```
    /// 
    /// **Example Error Response (400 Bad Request - Invalid Email):**
    /// ```json
    /// {
    ///   "error": "Bad Request",
    ///   "message": "Email is required"
    /// }
    /// ```
    /// 
    /// **Example Error Response (409 Conflict):**
    /// ```json
    /// {
    ///   "error": "Conflict",
    ///   "message": "A user with this email already exists"
    /// }
    /// ```
    /// 
    /// **Usage Notes:**
    /// - Store the refresh token securely for token renewal
    /// - Access tokens expire after 1 hour (default)
    /// - Refresh tokens expire after 7 days (default)
    /// - Use the access token in the Authorization header for authenticated requests: `Authorization: Bearer {accessToken}`
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
    /// Authenticate an existing user
    /// </summary>
    /// <param name="request">Login credentials (email and password)</param>
    /// <returns>Authentication response with user details, access token, and refresh token</returns>
    /// <response code="200">Login successful</response>
    /// <response code="401">Invalid credentials (email or password incorrect)</response>
    /// <remarks>
    /// Authenticates a user with their email and password. Returns access and refresh tokens upon successful authentication.
    /// 
    /// **Example Request:**
    /// ```
    /// POST /api/v1/auth/login
    /// Content-Type: application/json
    /// 
    /// {
    ///   "email": "john.doe@example.com",
    ///   "password": "SecurePass123!"
    /// }
    /// ```
    /// 
    /// **Example Response (200 OK):**
    /// ```json
    /// {
    ///   "user": {
    ///     "id": "550e8400-e29b-41d4-a716-446655440000",
    ///     "email": "john.doe@example.com",
    ///     "createdAt": "2024-01-15T10:00:00Z"
    ///   },
    ///   "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiI1NTBlODQwMC1lMjliLTQxZDQtYTcxNi00NDY2NTU0NDAwMDAiLCJlbWFpbCI6ImpvaG4uZG9lQGV4YW1wbGUuY29tIiwiaWF0IjoxNzA1MzI0MDAwLCJleHAiOjE3MDUzMjc2MDB9...",
    ///   "refreshToken": "base64_encoded_refresh_token_here",
    ///   "accessTokenExpiresAt": "2024-01-15T11:00:00Z",
    ///   "refreshTokenExpiresAt": "2024-01-22T10:00:00Z"
    /// }
    /// ```
    /// 
    /// **Example Error Response (401 Unauthorized):**
    /// ```json
    /// {
    ///   "error": "Unauthorized",
    ///   "message": "Invalid email or password"
    /// }
    /// ```
    /// 
    /// **Usage Notes:**
    /// - Store the refresh token securely for token renewal
    /// - Access tokens expire after 1 hour (default)
    /// - Refresh tokens expire after 7 days (default)
    /// - Use the access token in the Authorization header for authenticated requests: `Authorization: Bearer {accessToken}`
    /// - This endpoint is rate-limited to prevent brute force attacks
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
    /// Refresh access token using a refresh token
    /// </summary>
    /// <param name="request">Refresh token request containing the refresh token</param>
    /// <returns>New authentication response with new access and refresh tokens</returns>
    /// <response code="200">Token refreshed successfully</response>
    /// <response code="400">Invalid or expired refresh token</response>
    /// <remarks>
    /// Refreshes an expired access token using a valid refresh token. Returns new access and refresh tokens.
    /// This allows users to maintain their session without re-authenticating.
    /// 
    /// **Example Request:**
    /// ```
    /// POST /api/v1/auth/refresh
    /// Content-Type: application/json
    /// 
    /// {
    ///   "refreshToken": "base64_encoded_refresh_token_from_previous_login_or_register"
    /// }
    /// ```
    /// 
    /// **Example Response (200 OK):**
    /// ```json
    /// {
    ///   "user": {
    ///     "id": "550e8400-e29b-41d4-a716-446655440000",
    ///     "email": "john.doe@example.com",
    ///     "createdAt": "2024-01-15T10:00:00Z"
    ///   },
    ///   "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiI1NTBlODQwMC1lMjliLTQxZDQtYTcxNi00NDY2NTU0NDAwMDAiLCJlbWFpbCI6ImpvaG4uZG9lQGV4YW1wbGUuY29tIiwiaWF0IjoxNzA1MzI3NjAwLCJleHAiOjE3MDUzMzEyMDB9...",
    ///   "refreshToken": "new_base64_encoded_refresh_token",
    ///   "accessTokenExpiresAt": "2024-01-15T12:00:00Z",
    ///   "refreshTokenExpiresAt": "2024-01-22T10:00:00Z"
    /// }
    /// ```
    /// 
    /// **Example Error Response (400 Bad Request):**
    /// ```json
    /// {
    ///   "error": "Bad Request",
    ///   "message": "Invalid or expired refresh token"
    /// }
    /// ```
    /// 
    /// **Usage Notes:**
    /// - Refresh tokens are single-use - a new refresh token is issued with each refresh
    /// - Store the new refresh token to replace the old one
    /// - Refresh tokens expire after 7 days (default)
    /// - If the refresh token is expired or invalid, the user must log in again
    /// - This endpoint should be called when the access token expires (typically after 1 hour)
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
    /// Get current authenticated user's information
    /// </summary>
    /// <returns>Current user details</returns>
    /// <response code="200">User information retrieved successfully</response>
    /// <response code="401">User not authenticated or invalid token</response>
    /// <response code="404">User not found</response>
    /// <remarks>
    /// Retrieves the profile information of the currently authenticated user. This endpoint requires a valid JWT access token.
    /// 
    /// **Authentication Required**: Include JWT token in Authorization header
    /// 
    /// **Example Request:**
    /// ```
    /// GET /api/v1/auth/me
    /// Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiI1NTBlODQwMC1lMjliLTQxZDQtYTcxNi00NDY2NTU0NDAwMDAiLCJlbWFpbCI6ImpvaG4uZG9lQGV4YW1wbGUuY29tIiwiaWF0IjoxNzA1MzI0MDAwLCJleHAiOjE3MDUzMjc2MDB9...
    /// ```
    /// 
    /// **Example Response (200 OK):**
    /// ```json
    /// {
    ///   "id": "550e8400-e29b-41d4-a716-446655440000",
    ///   "email": "john.doe@example.com",
    ///   "createdAt": "2024-01-15T10:00:00Z"
    /// }
    /// ```
    /// 
    /// **Example Error Response (401 Unauthorized):**
    /// ```json
    /// {
    ///   "error": "Unauthorized",
    ///   "message": "Invalid token"
    /// }
    /// ```
    /// 
    /// **Example Error Response (404 Not Found):**
    /// ```json
    /// {
    ///   "error": "Not Found",
    ///   "message": "User not found"
    /// }
    /// ```
    /// 
    /// **Usage Notes:**
    /// - Use this endpoint to verify token validity and get current user information
    /// - The user ID from the response can be used for user-specific operations
    /// - If the token is expired, use the refresh token endpoint to get a new access token
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
    /// Request a password reset email
    /// </summary>
    /// <param name="request">Email address for password reset</param>
    /// <returns>Success response (always returns 200 for security reasons)</returns>
    /// <response code="200">Password reset email sent if email exists</response>
    /// <response code="400">Email is required</response>
    /// <remarks>
    /// Initiates a password reset process by sending a reset link to the user's email address.
    /// 
    /// **Security Note**: For security reasons, this endpoint always returns 200 OK even if the email doesn't exist.
    /// This prevents email enumeration attacks where attackers could determine which emails are registered.
    /// 
    /// **Example Request:**
    /// ```
    /// POST /api/v1/auth/password-reset/request
    /// Content-Type: application/json
    /// 
    /// {
    ///   "email": "john.doe@example.com"
    /// }
    /// ```
    /// 
    /// **Example Response (200 OK):**
    /// ```json
    /// {
    ///   "message": "If an account with that email exists, a password reset link has been sent."
    /// }
    /// ```
    /// 
    /// **Example Error Response (400 Bad Request):**
    /// ```json
    /// {
    ///   "error": "Bad Request",
    ///   "message": "Email is required"
    /// }
    /// ```
    /// 
    /// **Usage Notes:**
    /// - The password reset email contains a secure token that expires after a set time (typically 1 hour)
    /// - The token in the email should be used with the `/api/v1/auth/password-reset/confirm` endpoint
    /// - Users should check their email (including spam folder) for the reset link
    /// - The reset link is only valid for a limited time for security purposes
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
    /// Confirm password reset with token and new password
    /// </summary>
    /// <param name="request">Password reset confirmation with token (from email) and new password</param>
    /// <returns>Success response</returns>
    /// <response code="200">Password reset successful</response>
    /// <response code="400">Invalid token, expired token, or password validation failed</response>
    /// <remarks>
    /// Completes the password reset process by validating the reset token and updating the user's password.
    /// The token is obtained from the password reset email sent by the `/api/v1/auth/password-reset/request` endpoint.
    /// 
    /// **Password Requirements:**
    /// - Minimum 12 characters
    /// - At least one uppercase letter (A-Z)
    /// - At least one lowercase letter (a-z)
    /// - At least one number (0-9)
    /// - At least one special character (!@#$%^&amp;*()_+-=[]{}|;:,./&lt;&gt;?)
    /// 
    /// **Example Request:**
    /// ```
    /// POST /api/v1/auth/password-reset/confirm
    /// Content-Type: application/json
    /// 
    /// {
    ///   "token": "base64_encoded_token_from_password_reset_email",
    ///   "newPassword": "NewSecurePass123!"
    /// }
    /// ```
    /// 
    /// **Example Response (200 OK):**
    /// ```json
    /// {
    ///   "message": "Password has been reset successfully. You can now log in with your new password."
    /// }
    /// ```
    /// 
    /// **Example Error Response (400 Bad Request - Invalid Token):**
    /// ```json
    /// {
    ///   "error": "Bad Request",
    ///   "message": "Invalid or expired token"
    /// }
    /// ```
    /// 
    /// **Example Error Response (400 Bad Request - Invalid Password):**
    /// ```json
    /// {
    ///   "error": "Bad Request",
    ///   "message": "Password must be at least 12 characters long and contain at least one uppercase letter, one lowercase letter, one number, and one special character"
    /// }
    /// ```
    /// 
    /// **Example Error Response (400 Bad Request - Missing Fields):**
    /// ```json
    /// {
    ///   "error": "Bad Request",
    ///   "message": "Token is required"
    /// }
    /// ```
    /// 
    /// **Usage Notes:**
    /// - The token is typically found in the password reset email link (e.g., `?token=...`)
    /// - Tokens expire after a set time (typically 1 hour) for security
    /// - After successful password reset, the user should log in with the new password
    /// - The old password is immediately invalidated
    /// - If the token is expired or invalid, request a new password reset email
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
    /// Authenticate with OAuth provider using ID token or authorization code
    /// </summary>
    /// <param name="request">OAuth login request with provider name and either ID token or authorization code</param>
    /// <returns>Authentication response with user details, access token, and refresh token</returns>
    /// <response code="200">Login successful</response>
    /// <response code="400">Invalid request (missing or invalid parameters, unsupported provider)</response>
    /// <response code="401">Token validation failed</response>
    /// <response code="409">Account conflict (email already exists with different provider)</response>
    /// <remarks>
    /// Authenticates a user using OAuth providers (Google, Microsoft, GitHub). Supports both ID token flow (Google, Microsoft) and authorization code flow (GitHub).
    /// If the user doesn't exist, a new account is automatically created.
    /// 
    /// **Supported Providers:**
    /// - `Google`: Uses ID token flow
    /// - `Microsoft`: Uses ID token flow
    /// - `GitHub`: Uses authorization code flow
    /// 
    /// **For Google and Microsoft (ID Token Flow):**
    /// The frontend should obtain the ID token from the OAuth provider's authentication flow and send it here.
    /// 
    /// **Example Request (Google):**
    /// ```
    /// POST /api/v1/auth/oauth
    /// Content-Type: application/json
    /// 
    /// {
    ///   "provider": "Google",
    ///   "idToken": "eyJhbGciOiJSUzI1NiIsImtpZCI6IjEyMzQ1Njc4OTAifQ.eyJpc3MiOiJodHRwczovL2FjY291bnRzLmdvb2dsZS5jb20iLCJzdWIiOiIxMTIyMzM0NDU1NjY3Nzg4OTkiLCJlbWFpbCI6ImpvaG4uZG9lQGdtYWlsLmNvbSIsImV4cCI6MTcwNTMyNDAwMH0..."
    /// }
    /// ```
    /// 
    /// **Example Request (Microsoft):**
    /// ```
    /// POST /api/v1/auth/oauth
    /// Content-Type: application/json
    /// 
    /// {
    ///   "provider": "Microsoft",
    ///   "idToken": "eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiIsIng1dCI6IjEyMzQ1Njc4OTAifQ.eyJpc3MiOiJodHRwczovL2xvZ2luLm1pY3Jvc29mdG9ubGluZS5jb20iLCJzdWIiOiIxMTIyMzM0NDU1NjY3Nzg4OTkiLCJlbWFpbCI6ImpvaG4uZG9lQG91dGxvb2suY29tIiwiZXhwIjoxNzA1MzI0MDAwfQ..."
    /// }
    /// ```
    /// 
    /// **For GitHub (Authorization Code Flow):**
    /// The frontend should send the authorization code received from GitHub's OAuth callback.
    /// 
    /// **Example Request (GitHub):**
    /// ```
    /// POST /api/v1/auth/oauth
    /// Content-Type: application/json
    /// 
    /// {
    ///   "provider": "GitHub",
    ///   "authorizationCode": "abc123def456ghi789",
    ///   "redirectUri": "https://localhost:5000/login?provider=GitHub"
    /// }
    /// ```
    /// 
    /// **Example Response (200 OK):**
    /// ```json
    /// {
    ///   "user": {
    ///     "id": "550e8400-e29b-41d4-a716-446655440000",
    ///     "email": "john.doe@gmail.com",
    ///     "createdAt": "2024-01-15T10:00:00Z"
    ///   },
    ///   "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiI1NTBlODQwMC1lMjliLTQxZDQtYTcxNi00NDY2NTU0NDAwMDAiLCJlbWFpbCI6ImpvaG4uZG9lQGdtYWlsLmNvbSIsImlhdCI6MTcwNTMyNDAwMCwiZXhwIjoxNzA1MzI3NjAwfQ...",
    ///   "refreshToken": "base64_encoded_refresh_token_here",
    ///   "accessTokenExpiresAt": "2024-01-15T11:00:00Z",
    ///   "refreshTokenExpiresAt": "2024-01-22T10:00:00Z"
    /// }
    /// ```
    /// 
    /// **Example Error Response (400 Bad Request - Missing Provider):**
    /// ```json
    /// {
    ///   "error": "Bad Request",
    ///   "message": "Provider is required"
    /// }
    /// ```
    /// 
    /// **Example Error Response (400 Bad Request - Missing Token/Code):**
    /// ```json
    /// {
    ///   "error": "Bad Request",
    ///   "message": "Either IdToken or AuthorizationCode is required"
    /// }
    /// ```
    /// 
    /// **Example Error Response (400 Bad Request - Invalid Provider):**
    /// ```json
    /// {
    ///   "error": "Bad Request",
    ///   "message": "Invalid provider 'Facebook'. Supported providers: Google, Microsoft, GitHub"
    /// }
    /// ```
    /// 
    /// **Example Error Response (401 Unauthorized):**
    /// ```json
    /// {
    ///   "error": "Unauthorized",
    ///   "message": "Token validation failed"
    /// }
    /// ```
    /// 
    /// **Example Error Response (409 Conflict):**
    /// ```json
    /// {
    ///   "error": "Conflict",
    ///   "message": "An account with this email already exists with a different authentication provider"
    /// }
    /// ```
    /// 
    /// **Usage Notes:**
    /// - For Google/Microsoft: Use the ID token from the OAuth provider's authentication response
    /// - For GitHub: Use the authorization code from the OAuth callback URL
    /// - The redirectUri for GitHub must match the one configured in your GitHub OAuth app
    /// - If the user doesn't exist, a new account is automatically created
    /// - The email from the OAuth provider is used as the account identifier
    /// - Store the returned tokens for authenticated API requests
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
