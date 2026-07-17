using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ServiceBusExplorer.App.Views.Dialogs;

public partial class ConfirmationDialog : Window
{
    public ConfirmationDialog()
    {
        InitializeComponent();
    }

    public ConfirmationDialog(ConfirmationRequest request)
        : this()
    {
        TargetText.Text = $"Target: {request.Target}\nSource: {FormatSource(request.Source)}";
        ConsequenceText.Text = request.Consequence;
        Opened += (_, _) => CancelButton.Focus();
    }

    private static string FormatSource(MessageSource source) =>
        source switch
        {
            MessageSource.Active => "Active messages",
            MessageSource.DeadLetter => "Dead-letter messages",
            MessageSource.TransferDeadLetter => "Transfer dead-letter messages",
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown message source.")
        };

    private void OnCancel(object? sender, RoutedEventArgs e) =>
        Close(ConfirmationResult.Cancelled);

    private void OnConfirm(object? sender, RoutedEventArgs e) =>
        Close(ConfirmationResult.Confirmed);
}
