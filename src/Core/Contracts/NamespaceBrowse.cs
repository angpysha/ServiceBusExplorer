#nullable enable
namespace ServiceBusExplorer;

public sealed record NamespaceBrowseRequest(
    ConnectionScope Scope,
    string? EntityPath,
    CapabilitySet Capabilities,
    BrowseSurface Surface,
    ScopedEntityKind EntityKind = ScopedEntityKind.None);

public sealed record NamespaceBrowseResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

    public string? GuidanceMessage { get; init; }

    public static NamespaceBrowseResult<T> Empty(string? guidanceMessage = null) =>
        new() { GuidanceMessage = guidanceMessage };

    public static NamespaceBrowseResult<T> FromItems(IReadOnlyList<T> items) =>
        new() { Items = items };
}
