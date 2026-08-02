namespace ServiceBusExplorer;

/// <summary>
/// Identifies the exact Service Bus message source used by a source-specific operation.
/// </summary>
public enum MessageSource
{
    Active,
    DeadLetter,
    TransferDeadLetter
}
