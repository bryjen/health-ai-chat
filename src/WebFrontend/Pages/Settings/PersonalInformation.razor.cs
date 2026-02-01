using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Web.Common.DTOs.Auth;
using WebFrontend.Services;
using WebFrontend.Services.Auth;

namespace WebFrontend.Pages.Settings;

public partial class PersonalInformation : ComponentBase
{
    [Inject] private AuthService AuthService { get; set; } = null!;
    [Inject] private AuthenticationStateProvider AuthStateProvider { get; set; } = null!;
    [Inject] private ToastService ToastService { get; set; } = null!;

    protected PersonalInformationModel Model { get; set; } = new();
    protected bool IsSaving { get; set; } = false;
    protected string? ProfileImageUrl { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await LoadUserDataAsync();
    }

    private async Task LoadUserDataAsync()
    {
        var profile = await AuthService.GetCurrentProfileAsync();
        Model.Email = profile.Email;
        Model.FirstName = profile.FirstName;
        Model.LastName = profile.LastName;
        Model.Username = profile.Username;
        Model.PhoneCountryCode = string.IsNullOrEmpty(profile.PhoneCountryCode) ? "+1" : profile.PhoneCountryCode;
        Model.PhoneNumber = profile.PhoneNumber;
        Model.Country = profile.Country;
        Model.Address = profile.Address;
        Model.City = profile.City;
        Model.PostalCode = profile.PostalCode;
    }

    protected string GetUserInitials()
    {
        var user = AuthService.CurrentUser;
        if (user != null && !string.IsNullOrEmpty(user.Email))
        {
            var email = user.Email;
            var parts = email.Split('@');
            if (parts.Length > 0 && parts[0].Length > 0)
            {
                return parts[0].Substring(0, 1).ToUpperInvariant();
            }
        }
        return "U";
    }

    protected async Task HandleSave()
    {
        IsSaving = true;

        try
        {
            var request = new UpdateUserProfileRequest
            {
                FirstName = Model.FirstName,
                LastName = Model.LastName,
                Username = Model.Username,
                PhoneCountryCode = Model.PhoneCountryCode,
                PhoneNumber = Model.PhoneNumber,
                Country = Model.Country,
                Address = Model.Address,
                City = Model.City,
                PostalCode = Model.PostalCode
            };

            var updatedProfile = await AuthService.UpdateProfileAsync(request);
            Model.FirstName = updatedProfile.FirstName;
            Model.LastName = updatedProfile.LastName;
            Model.Username = updatedProfile.Username;
            Model.PhoneCountryCode = updatedProfile.PhoneCountryCode;
            Model.PhoneNumber = updatedProfile.PhoneNumber;
            Model.Country = updatedProfile.Country;
            Model.Address = updatedProfile.Address;
            Model.City = updatedProfile.City;
            Model.PostalCode = updatedProfile.PostalCode;

            ToastService.ShowSuccess("Profile updated", "Your personal information has been saved successfully.");
        }
        catch (Exception ex)
        {
            ToastService.ShowError("Failed to save", ex.Message);
        }
        finally
        {
            IsSaving = false;
        }
    }

    protected async Task HandleRemoveProfileImage()
    {
        ProfileImageUrl = null;
        // TODO: Call API to remove profile image when backend is ready
        await Task.CompletedTask;
    }
}

public class PersonalInformationModel
{
    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Username { get; set; }

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string Email { get; set; } = string.Empty;

    public string? PhoneCountryCode { get; set; }

    [Phone(ErrorMessage = "Invalid phone number")]
    public string? PhoneNumber { get; set; }

    public string? Country { get; set; }

    public string? Address { get; set; }

    public string? City { get; set; }

    public string? PostalCode { get; set; }
}
