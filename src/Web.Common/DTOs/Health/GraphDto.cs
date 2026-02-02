namespace Web.Common.DTOs.Health;

public enum GraphNodeType
{
    Symptom,
    Diagnosis,
    Root
}

public class GraphNodeDto
{
    public required string Id { get; set; }
    public required string Label { get; set; }
    public GraphNodeType Type { get; set; }
    public int Value { get; set; }
    public int Group { get; set; }
}

public class GraphLinkDto
{
    public required string Source { get; set; }
    public required string Target { get; set; }
    public int Value { get; set; }
}

public class GraphDataDto
{
    public required List<GraphNodeDto> Nodes { get; set; }
    public required List<GraphLinkDto> Links { get; set; }
}
