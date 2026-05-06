using Avalonia;
using Avalonia.Controls;
using System.Reactive.Linq;

namespace ServiceBusExplorer.App.Views.Controls;

public partial class TimeSpanControl : UserControl
{
    public static readonly StyledProperty<TimeSpan> ValueProperty =
        AvaloniaProperty.Register<TimeSpanControl, TimeSpan>(nameof(Value),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public TimeSpan Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    private bool _updating;

    public TimeSpanControl()
    {
        InitializeComponent();

        // Per-instance subscription (not class handler, which accumulates across instances)
        this.GetObservable(ValueProperty).Subscribe(_ => SyncFromValue());

        DaysBox.ValueChanged    += (_, _) => SyncToValue();
        HoursBox.ValueChanged   += (_, _) => SyncToValue();
        MinutesBox.ValueChanged += (_, _) => SyncToValue();
        SecondsBox.ValueChanged += (_, _) => SyncToValue();
    }

    private void SyncFromValue()
    {
        if (_updating) return;
        _updating = true;
        try
        {
            var ts = Value;
            DaysBox.Value    = ts.Days;
            HoursBox.Value   = ts.Hours;
            MinutesBox.Value = ts.Minutes;
            SecondsBox.Value = ts.Seconds;
        }
        finally
        {
            _updating = false;
        }
    }

    private void SyncToValue()
    {
        if (_updating) return;
        _updating = true;
        try
        {
            var days    = (int)(DaysBox.Value    ?? 0);
            var hours   = (int)(HoursBox.Value   ?? 0);
            var minutes = (int)(MinutesBox.Value ?? 0);
            var seconds = (int)(SecondsBox.Value ?? 0);
            Value = new TimeSpan(days, hours, minutes, seconds);
        }
        finally
        {
            _updating = false;
        }
    }
}
