using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Web.Common.DTOs.Auth;
using WebApi.Configuration.Options;
using WebApi.Data;
using WebApi.Exceptions;
using WebApi.Models;
using WebApi.Services.Auth.Validation;
using WebApi.Services.Validation;

namespace WebApi.Services.Auth;

public class AuthService(
    AppDbContext context,
    JwtTokenService jwtTokenService,
    RefreshTokenService refreshTokenService,
    PasswordValidator passwordValidator,
    TokenValidationServiceFactory tokenValidationFactory,
    IOptions<JwtSettings> jwtSettings,
    IOptions<OAuthSettings> oauthSettings)
{
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        // Validate password strength
        var (isValid, errorMessage) = passwordValidator.ValidatePassword(request.Password);
        if (!isValid)
        {
            throw new ValidationException(errorMessage ?? "Invalid password");
        }

        // Check if email already exists for Local provider
        if (await context.Users.AnyAsync(u => u.Provider == AuthProvider.Local && u.Email == request.Email))
        {
            throw new ConflictException("Email already exists");
        }

        // Hash the password
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12);

        // Create new user
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            PasswordHash = passwordHash,
            Provider = AuthProvider.Local,
            ProviderUserId = null,
            FirstName = request.FirstName,
            LastName = request.LastName,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();

        return await GenerateAuthResponseAsync(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        // Find user by email and Local provider
        var user = await context.Users
            .FirstOrDefaultAsync(u => u.Provider == AuthProvider.Local && u.Email == request.Email);

        if (user == null)
        {
            // Use same error message to prevent email enumeration
            throw new UnauthorizedAccessException("Invalid email or password");
        }

        // Verify password
        if (user.PasswordHash == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedAccessException("Invalid email or password");
        }

        return await GenerateAuthResponseAsync(user);
    }

    public async Task<AuthResponse> LoginWithOAuthAsync(AuthProvider provider, string? idToken = null, string? authorizationCode = null, string? redirectUri = null)
    {
        // Get validator for the provider
        var validator = tokenValidationFactory.GetValidator(provider);

        // Get client ID and secret from options
        string clientId;
        string? clientSecret = null;

        switch (provider)
        {
            case AuthProvider.Google:
                clientId = oauthSettings.Value.Google.ClientId;
                if (string.IsNullOrWhiteSpace(clientId))
                    throw new InvalidOperationException("Google Client ID is not configured");
                break;
            case AuthProvider.Microsoft:
                clientId = oauthSettings.Value.Microsoft.ClientId;
                if (string.IsNullOrWhiteSpace(clientId))
                    throw new InvalidOperationException("Microsoft Client ID is not configured");
                break;
            case AuthProvider.GitHub:
                clientId = oauthSettings.Value.GitHub.ClientId;
                clientSecret = oauthSettings.Value.GitHub.ClientSecret;
                if (string.IsNullOrWhiteSpace(clientId))
                    throw new InvalidOperationException("GitHub Client ID is not configured");
                if (string.IsNullOrWhiteSpace(clientSecret))
                    throw new InvalidOperationException("GitHub Client Secret is not configured");
                break;
            default:
                throw new NotSupportedException($"OAuth provider '{provider}' is not supported");
        }

        TokenValidationResult validationResult;

        // Handle authorization code flow (GitHub)
        if (!string.IsNullOrWhiteSpace(authorizationCode))
        {
            if (string.IsNullOrWhiteSpace(redirectUri))
            {
                throw new InvalidOperationException("Redirect URI is required for authorization code flow");
            }

            validationResult = await validator.ValidateAuthorizationCodeAsync(authorizationCode, redirectUri, clientId, clientSecret!);
        }
        // Handle ID token flow (Google, Microsoft)
        else if (!string.IsNullOrWhiteSpace(idToken))
        {
            validationResult = await validator.ValidateIdTokenAsync(idToken, clientId);
        }
        else
        {
            throw new InvalidOperationException("Either IdToken or AuthorizationCode must be provided");
        }

        // Use generic OAuth login method
        return await LoginWithOAuthAsync(provider, validationResult.UserId, validationResult.Email);
    }

    public async Task<AuthResponse> LoginWithOAuthAsync(AuthProvider provider, string providerUserId, string email)
    {
        // Find existing account for this provider
        var user = await context.Users
            .FirstOrDefaultAsync(u => 
                u.Provider == provider && 
                u.ProviderUserId == providerUserId);

        if (user == null)
        {
            // Check if email already exists for this provider
            if (await context.Users.AnyAsync(u => u.Provider == provider && u.Email == email))
            {
                var providerName = provider.ToString();
                throw new ConflictException($"A {providerName} account with this email already exists");
            }

            // Create new account for this provider
            user = new User
            {
                Id = Guid.NewGuid(),
                Email = email,
                PasswordHash = null,
                Provider = provider,
                ProviderUserId = providerUserId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        return await GenerateAuthResponseAsync(user);
    }

    public async Task<AuthResponse> RefreshTokenAsync(string refreshToken)
    {
        // Validate refresh token
        var isValid = await refreshTokenService.IsTokenValidAsync(refreshToken);
        if (!isValid)
        {
            throw new ValidationException("Invalid or expired refresh token");
        }

        var tokenEntity = await refreshTokenService.GetRefreshTokenAsync(refreshToken);
        if (tokenEntity == null || tokenEntity.User == null)
        {
            throw new ValidationException("Invalid refresh token");
        }

        // Revoke the old refresh token (token rotation)
        await refreshTokenService.RevokeRefreshTokenAsync(refreshToken, "Token rotated");

        // Generate new tokens
        return await GenerateAuthResponseAsync(tokenEntity.User);
    }
    
    public async Task<UserDto?> GetUserByIdAsync(Guid userId)
    {
        var user = await context.Users.FindAsync(userId);
        
        if (user == null)
        {
            return null;
        }

        return new UserDto
        {
            Id = user.Id,
            Email = user.Email,
            CreatedAt = user.CreatedAt
        };
    }

    public async Task<UserProfileDto> GetProfileAsync(Guid userId)
    {
        var user = await context.Users.FindAsync(userId);
        if (user == null)
        {
            throw new NotFoundException("User not found");
        }

        return MapToProfileDto(user);
    }

    public async Task<UserProfileDto> UpdateProfileAsync(Guid userId, UpdateUserProfileRequest request)
    {
        var user = await context.Users.FindAsync(userId);
        if (user == null)
        {
            throw new NotFoundException("User not found");
        }

        user.FirstName = request.FirstName;
        user.LastName = request.LastName;
        user.Username = request.Username;
        user.PhoneCountryCode = request.PhoneCountryCode;
        user.PhoneNumber = request.PhoneNumber;
        user.Country = request.Country;
        user.Address = request.Address;
        user.City = request.City;
        user.PostalCode = request.PostalCode;
        user.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();

        return MapToProfileDto(user);
    }

    private static UserDto MapToDto(User user)
    {
        return new UserDto
        {
            Id = user.Id,
            Email = user.Email,
            CreatedAt = user.CreatedAt
        };
    }

    private static UserProfileDto MapToProfileDto(User user)
    {
        return new UserProfileDto
        {
            Id = user.Id,
            Email = user.Email,
            CreatedAt = user.CreatedAt,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Username = user.Username,
            PhoneCountryCode = user.PhoneCountryCode,
            PhoneNumber = user.PhoneNumber,
            Country = user.Country,
            Address = user.Address,
            City = user.City,
            PostalCode = user.PostalCode
        };
    }

    private async Task<AuthResponse> GenerateAuthResponseAsync(User user)
    {
        var accessToken = jwtTokenService.GenerateAccessToken(user, out _);
        var refreshToken = await refreshTokenService.GenerateRefreshTokenAsync(user);

        var accessTokenExpirationMinutes = jwtSettings.Value.AccessTokenExpirationMinutes;
        var refreshTokenExpirationDays = jwtSettings.Value.RefreshTokenExpirationDays;

        return new AuthResponse
        {
            User = MapToDto(user),
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AccessTokenExpiresAt = DateTime.UtcNow.AddMinutes(accessTokenExpirationMinutes),
            RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(refreshTokenExpirationDays)
        };
    }
}

