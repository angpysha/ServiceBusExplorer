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
/// <param name="Target">The entity that will be affected (exact name/path).</param>
/// <param name="Source">
/// The exact message source for source-specific work; null for entity/rule administration.
/// </param>
/// <param name="Consequence">A concise description of the irreversible consequence.</param>
/// <param name="Risk">The operation risk level.</param>
/// <param name="ConfirmActionLabel">Action-specific confirm button label (e.g. Delete, Purge).</param>
public sealed record ConfirmationRequest(
    string Target,
    MessageSource? Source,
    string Consequence,
    ConfirmationRisk Risk,
    string ConfirmActionLabel = "Confirm");
