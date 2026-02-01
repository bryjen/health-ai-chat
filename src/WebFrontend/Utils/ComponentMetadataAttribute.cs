namespace WebFrontend.Utils;

/// <summary>
/// Describes a UI component, its role, and relationships to other components.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ComponentMetadataAttribute : Attribute
{
    /// <summary>
    /// Human-friendly description of what this component does.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Names of other entry components this component depends on.
    /// </summary>
    public string[]? Dependencies { get; set; }

    /// <summary>
    /// Whether this is the primary (entry) component for its group.
    /// </summary>
    public bool IsEntry { get; set; } = true;

    /// <summary>
    /// Logical group identifier, typically nameof(entry component type).
    /// </summary>
    public string? Group { get; set; }
}
