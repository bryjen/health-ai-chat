using System.Text.RegularExpressions;

namespace WebApi.Services.Validation;

public class PasswordValidator
{
    public (bool IsValid, string? ErrorMessage) ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return (false, "Password is required");
        }

        if (password.Length < 12)
        {
            return (false, "Password must be at least 12 characters long");
        }

        if (password.Length > 100)
        {
            return (false, "Password must not exceed 100 characters");
        }

        // Check for at least one uppercase letter
        if (!Regex.IsMatch(password, @"[A-Z]"))
        {
            return (false, "Password must contain at least one uppercase letter");
        }

        // Check for at least one lowercase letter
        if (!Regex.IsMatch(password, @"[a-z]"))
        {
            return (false, "Password must contain at least one lowercase letter");
        }

        // Check for at least one digit
        if (!Regex.IsMatch(password, @"[0-9]"))
        {
            return (false, "Password must contain at least one number");
        }

        // Check for at least one special character
        if (!Regex.IsMatch(password, @"[!@#$%^&*()_+\-=\[\]{};':""\\|,.<>\/?]"))
        {
            return (false, "Password must contain at least one special character (!@#$%^&*()_+-=[]{}|;':\",./<>?)");
        }

        // Check for common weak passwords (optional - can be expanded)
        var commonPasswords = new[] { "Password123!", "Password1!", "Admin123!", "Welcome123!" };
        if (commonPasswords.Contains(password, StringComparer.OrdinalIgnoreCase))
        {
            return (false, "Password is too common. Please choose a more unique password");
        }

        return (true, null);
    }
}
