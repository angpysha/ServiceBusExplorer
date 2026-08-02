namespace ServiceBusExplorer;

/// <summary>
/// Presents structured confirmation requests without coupling view models to a UI framework.
/// </summary>
public interface IConfirmationService
{
    Task<ConfirmationResult> ConfirmAsync(
        ConfirmationRequest request,
        CancellationToken cancellationToken = default);
}
