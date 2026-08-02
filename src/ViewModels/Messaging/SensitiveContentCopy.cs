namespace ServiceBusExplorer.ViewModels;

/// <summary>
/// Deliberate sensitive-content copy/export acknowledgment helper.
/// </summary>
public static class SensitiveContentCopy
{
    public const string WarningConsequence =
        "Message content may contain sensitive data. Copy or export only if you intend to share or store it.";

    public static async Task<bool> ConfirmAsync(
        IConfirmationService confirmationService,
        string target,
        MessageSource source,
        CancellationToken cancellationToken = default)
    {
        var result = await confirmationService.ConfirmAsync(
            new ConfirmationRequest(target, source, WarningConsequence, ConfirmationRisk.Irreversible),
            cancellationToken);
        return result == ConfirmationResult.Confirmed;
    }
}
