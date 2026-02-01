using Microsoft.AspNetCore.Components;
using WebFrontend.Services;
using WebFrontend.Services.Auth;

namespace WebFrontend.Pages.Settings;

public partial class AppSettings : ComponentBase
{
    [Inject] private ToastService ToastService { get; set; } = null!;
    [Inject] private AuthService AuthService { get; set; } = null!;

    protected AppSettingsModel Settings { get; set; } = new();

    protected override async Task OnInitializedAsync()
    {
        await LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        // TODO: Load settings from backend/localStorage
        // For now, using default values
        Settings = new AppSettingsModel
        {
            DebugMode = false,
            DeveloperMode = false,
            PrivacyMode = false,
            AnalyticsEnabled = true,
            EmailNotifications = true,
            PushNotifications = false,
            AutoSave = true,
            PerformanceMode = false,
            ApiRateLimiting = true
        };
    }

    private async Task SaveSettingsAsync()
    {
        // TODO: Save settings to backend/localStorage
        await Task.CompletedTask;
    }

    private async Task OnDebugModeChanged(bool value)
    {
        Settings.DebugMode = value;
        await SaveSettingsAsync();
        ToastService.ShowSuccess(
            value ? "Debug Mode Enabled" : "Debug Mode Disabled",
            value ? "Detailed logging is now active." : "Debug logging has been turned off."
        );
    }

    private async Task OnDeveloperModeChanged(bool value)
    {
        Settings.DeveloperMode = value;
        await SaveSettingsAsync();
        ToastService.ShowWarning(
            value ? "Developer Mode Enabled" : "Developer Mode Disabled",
            value ? "Experimental features are now available. Use with caution." : "Developer features have been disabled."
        );
    }

    private async Task OnPrivacyModeChanged(bool value)
    {
        Settings.PrivacyMode = value;
        await SaveSettingsAsync();
        ToastService.ShowInfo(
            value ? "Privacy Mode Enabled" : "Privacy Mode Disabled",
            value ? "Your data will not be stored or used for training." : "Privacy mode has been turned off."
        );
    }

    private async Task OnAnalyticsChanged(bool value)
    {
        Settings.AnalyticsEnabled = value;
        await SaveSettingsAsync();
        ToastService.ShowSuccess(
            value ? "Analytics Enabled" : "Analytics Disabled",
            value ? "Anonymous usage data will be collected." : "Analytics collection has been disabled."
        );
    }

    private async Task OnEmailNotificationsChanged(bool value)
    {
        Settings.EmailNotifications = value;
        await SaveSettingsAsync();
        ToastService.ShowSuccess(
            value ? "Email Notifications Enabled" : "Email Notifications Disabled",
            value ? "You will receive email updates." : "Email notifications have been turned off."
        );
    }

    private async Task OnPushNotificationsChanged(bool value)
    {
        Settings.PushNotifications = value;
        await SaveSettingsAsync();
        
        if (value)
        {
            // Request browser permission for push notifications
            // TODO: Implement browser notification permission request
        }
        
        ToastService.ShowSuccess(
            value ? "Push Notifications Enabled" : "Push Notifications Disabled",
            value ? "You will receive browser push notifications." : "Push notifications have been turned off."
        );
    }

    private async Task OnAutoSaveChanged(bool value)
    {
        Settings.AutoSave = value;
        await SaveSettingsAsync();
        ToastService.ShowSuccess(
            value ? "Auto-save Enabled" : "Auto-save Disabled",
            value ? "Your work will be saved automatically." : "Auto-save has been turned off."
        );
    }

    private async Task OnPerformanceModeChanged(bool value)
    {
        Settings.PerformanceMode = value;
        await SaveSettingsAsync();
        ToastService.ShowInfo(
            value ? "Performance Mode Enabled" : "Performance Mode Disabled",
            value ? "Application performance has been optimized." : "Performance optimizations have been disabled."
        );
    }

    private async Task OnApiRateLimitingChanged(bool value)
    {
        Settings.ApiRateLimiting = value;
        await SaveSettingsAsync();
        ToastService.ShowWarning(
            value ? "API Rate Limiting Enabled" : "API Rate Limiting Disabled",
            value ? "API calls are now rate-limited." : "API rate limiting has been disabled. Use with caution."
        );
    }

    private async Task HandleEditPrivacyMode()
    {
        // TODO: Implement privacy mode edit dialog
        ToastService.ShowInfo(
            "Privacy Mode Settings",
            "Advanced privacy settings will be available soon."
        );
        await Task.CompletedTask;
    }

    private async Task HandleDeleteAccount()
    {
        // TODO: Implement account deletion confirmation dialog
        ToastService.ShowWarning(
            "Account Deletion",
            "This feature is not yet implemented. Please contact support to delete your account."
        );
        await Task.CompletedTask;
    }
}

public class AppSettingsModel
{
    public bool DebugMode { get; set; }
    public bool DeveloperMode { get; set; }
    public bool PrivacyMode { get; set; }
    public bool AnalyticsEnabled { get; set; }
    public bool EmailNotifications { get; set; }
    public bool PushNotifications { get; set; }
    public bool AutoSave { get; set; }
    public bool PerformanceMode { get; set; }
    public bool ApiRateLimiting { get; set; }
}