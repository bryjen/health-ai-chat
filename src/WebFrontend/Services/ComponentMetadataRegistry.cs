using System.Reflection;
using WebFrontend.Utils;

namespace WebFrontend.Services;

public sealed class ComponentMetadataRegistry
{
    private readonly Lazy<IReadOnlyList<ComponentMetadataEntry>> _entries;

    public ComponentMetadataRegistry()
    {
        _entries = new Lazy<IReadOnlyList<ComponentMetadataEntry>>(DiscoverMetadata);
    }

    public IReadOnlyList<ComponentMetadataEntry> Entries => _entries.Value;

    private static IReadOnlyList<ComponentMetadataEntry> DiscoverMetadata()
    {
        var assembly = typeof(ComponentMetadataAttribute).Assembly;

        var items = assembly
            .GetTypes()
            .Select(t => new
            {
                Type = t,
                Attribute = t.GetCustomAttribute<ComponentMetadataAttribute>()
            })
            .Where(x => x.Attribute is not null)
            .Select(x => new ComponentMetadataEntry(
                x.Type,
                x.Attribute!.Description,
                x.Attribute!.IsEntry,
                x.Attribute!.Group,
                x.Attribute!.Dependencies ?? Array.Empty<string>()))
            .OrderBy(e => e.Group)
            .ThenBy(e => e.Type.Name)
            .ToList()
            .AsReadOnly();

        return items;
    }
}

public sealed record ComponentMetadataEntry(
    Type Type,
    string? Description,
    bool IsEntry,
    string? Group,
    IReadOnlyList<string> Dependencies);

