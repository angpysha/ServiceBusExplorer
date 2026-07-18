#nullable enable
namespace ServiceBusExplorer;

/// <summary>
/// Effective capabilities granted by scope, auth, and successful probes.
/// </summary>
public sealed record CapabilitySet
{
    public bool CanBrowseEntities { get; init; }

    public bool CanAdministerNamespace { get; init; }

    public bool CanSend { get; init; }

    public bool CanInspectMessages { get; init; }

    public bool CanReceiveAndSettle { get; init; }

    public bool CanUseSessions { get; init; }

    public bool CanRetrieveDeferredAndRecover { get; init; }

    public static CapabilitySet ForNamespaceScope(bool adminProbeSucceeded) =>
        new()
        {
            CanBrowseEntities = true,
            CanAdministerNamespace = adminProbeSucceeded,
            CanSend = true,
            CanInspectMessages = true,
            CanReceiveAndSettle = true,
            CanUseSessions = true,
            CanRetrieveDeferredAndRecover = true,
        };

    public static CapabilitySet ForEntityScope() =>
        new()
        {
            CanBrowseEntities = false,
            CanAdministerNamespace = false,
            CanSend = true,
            CanInspectMessages = true,
            CanReceiveAndSettle = true,
            CanUseSessions = true,
            CanRetrieveDeferredAndRecover = true,
        };
}
