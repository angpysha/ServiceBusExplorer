namespace ServiceBusExplorer;

/// <summary>
/// Describes the severity of an operation that requires confirmation.
/// </summary>
public enum ConfirmationRisk
{
    Irreversible
}

/// <summary>
/// Represents the user's decision for a confirmation request.
/// </summary>
public enum ConfirmationResult
{
    Cancelled,
    Confirmed
}

/// <summary>
/// Contains structured, non-secret context for a destructive-operation confirmation.
/// </summary>
/// <param name="Target">The entity that will be affected.</param>
/// <param name="Source">The exact message source that will be affected.</param>
/// <param name="Consequence">A concise description of the irreversible consequence.</param>
/// <param name="Risk">The operation risk level.</param>
public sealed record ConfirmationRequest(
    string Target,
    MessageSource Source,
    string Consequence,
    ConfirmationRisk Risk);
