using Azure.Messaging.ServiceBus;

namespace ServiceBusExplorer.Services;

/// <summary>
/// Maps the product's explicit message source to the Azure Service Bus SDK source.
/// </summary>
public static class MessageSourceMapper
{
    public static SubQueue Map(MessageSource source) =>
        source switch
        {
            MessageSource.Active => SubQueue.None,
            MessageSource.DeadLetter => SubQueue.DeadLetter,
            MessageSource.TransferDeadLetter => SubQueue.TransferDeadLetter,
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown message source.")
        };
}
