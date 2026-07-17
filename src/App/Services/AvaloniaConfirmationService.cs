using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using ServiceBusExplorer.App.Views.Dialogs;

namespace ServiceBusExplorer.App.Services;

/// <summary>
/// Presents destructive-operation confirmation requests as owned Avalonia dialogs.
/// </summary>
public sealed class AvaloniaConfirmationService : IConfirmationService
{
    public async Task<ConfirmationResult> ConfirmAsync(
        ConfirmationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Application.Current?.ApplicationLifetime
                is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
        {
            return ConfirmationResult.Cancelled;
        }

        var dialog = new ConfirmationDialog(request);
        using var registration = cancellationToken.Register(dialog.Close);
        return await dialog.ShowDialog<ConfirmationResult>(owner);
    }
}
