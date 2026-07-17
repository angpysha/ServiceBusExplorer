namespace ServiceBusExplorer;

/// <summary>
/// Identifies the page from which a send was requested.
/// </summary>
public enum SendTargetKind
{
    Queue,
    Topic,
    Subscription
}

/// <summary>
/// Separates the requested UI context from the entity path used by the send backend.
/// </summary>
public sealed record SendTargetContext(
    SendTargetKind RequestedKind,
    string RequestedEntityPath,
    string ActualDestinationPath)
{
    public string DestinationDescription =>
        RequestedKind switch
        {
            SendTargetKind.Queue => $"Sends to queue '{ActualDestinationPath}'.",
            SendTargetKind.Topic => $"Publishes to topic '{ActualDestinationPath}'.",
            SendTargetKind.Subscription =>
                $"Publishes to parent topic '{ActualDestinationPath}' for subscription '{RequestedEntityPath}'.",
            _ => throw new ArgumentOutOfRangeException(
                nameof(RequestedKind),
                RequestedKind,
                "Unknown send target kind.")
        };

    public string SuccessDescription =>
        RequestedKind switch
        {
            SendTargetKind.Queue => $"Message sent to queue '{ActualDestinationPath}'.",
            SendTargetKind.Topic => $"Message published to topic '{ActualDestinationPath}'.",
            SendTargetKind.Subscription =>
                $"Message published to parent topic '{ActualDestinationPath}' for subscription '{RequestedEntityPath}'.",
            _ => throw new ArgumentOutOfRangeException(
                nameof(RequestedKind),
                RequestedKind,
                "Unknown send target kind.")
        };

    public string FailurePrefix =>
        RequestedKind == SendTargetKind.Subscription
            ? $"Publish to parent topic '{ActualDestinationPath}' failed"
            : $"Send to '{ActualDestinationPath}' failed";
}
