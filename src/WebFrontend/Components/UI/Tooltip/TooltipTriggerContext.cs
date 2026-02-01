using System.Collections.Generic;

namespace WebFrontend.Components.UI.Tooltip;

/// <summary>
/// Provides attributes and identifiers required to make an element the tooltip trigger.
/// </summary>
public sealed class TooltipTriggerContext
{
    public TooltipTriggerContext(Dictionary<string, object?> attributes, string? tooltipId, string? contentId)
    {
        Attributes = attributes;
        TooltipId = tooltipId;
        ContentId = contentId;
    }

    /// <summary>
    /// Attributes that must be forwarded to the trigger element (data-tooltip-trigger, aria-describedby, custom overrides, etc.).
    /// </summary>
    public Dictionary<string, object?> Attributes { get; }

    /// <summary>
    /// The identifier shared between the tooltip trigger and its content.
    /// </summary>
    public string? TooltipId { get; }

    /// <summary>
    /// The identifier applied to the tooltip content for accessibility hooks.
    /// </summary>
    public string? ContentId { get; }
}
