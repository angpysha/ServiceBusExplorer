#nullable enable

namespace ServiceBusExplorer;

/// <summary>
/// How dead-letter diagnostic properties are treated when building a replacement message.
/// </summary>
public enum DiagnosticPropertyTreatment
{
    /// <summary>
    /// Dead-letter diagnostic keys are preserved as ordinary application properties.
    /// </summary>
    RetainAsCustom,

    /// <summary>
    /// Dead-letter diagnostic keys are removed from the replacement application properties.
    /// </summary>
    Remove
}

/// <summary>
/// Input for selected-message recovery: send each replacement before settling the original.
/// </summary>
public sealed record RecoveryRequest(
    EntityAddress SourceAddress,
    MessageSource Source,
    EntityAddress DestinationAddress,
    DiagnosticPropertyTreatment DiagnosticPropertyTreatment,
    IReadOnlyList<ObservedMessage> SelectedMessages);

/// <summary>
/// Selected-message recovery: sends replacements to an explicit compatible destination and
/// settles originals only after successful replacement send.
/// </summary>
public interface IRecoveryService
{
    Task<RecoveryOperation> RecoverAsync(
        RecoveryRequest request,
        CancellationToken cancellationToken = default);
}

