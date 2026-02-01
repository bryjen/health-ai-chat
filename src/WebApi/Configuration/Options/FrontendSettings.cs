using System.Diagnostics.CodeAnalysis;

namespace WebApi.Configuration.Options;

/// <summary>
/// Frontend application settings
/// </summary>
[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
public class FrontendSettings
{
    public const string SectionName = "Frontend";
    
    public string BaseUrl { get; set; } = string.Empty;
}
