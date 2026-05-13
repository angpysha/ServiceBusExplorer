using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;

namespace ServiceBusExplorer.ViewModels;

public record DashboardRow(string Name, string Type, long Active, long DeadLetter, long Scheduled)
{
    public string TypeColor => Type switch
    {
        "Queue"            => "#0078D4",
        "Topic"            => "#8764B8",
        "Event Hub"        => "#CA5010",
        "Relay"            => "#038387",
        "Notification Hub" => "#107C10",
        _                  => "#767676"
    };
}

public class DashboardViewModel : ReactiveObject
{
    private readonly ObservableAsPropertyHelper<bool> _isLoading;

    public ObservableCollection<DashboardRow> Rows { get; } = new();

    public bool IsLoading => _isLoading.Value;

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

    public DashboardViewModel(QueueListViewModel queues, TopicListViewModel topics)
    {
        void Rebuild()
        {
            Rows.Clear();
            foreach (var q in queues.Queues)
                Rows.Add(new DashboardRow(q.Name, "Queue", q.ActiveMessageCount, q.DeadLetterCount, q.ScheduledMessageCount));
            foreach (var t in topics.Topics)
                Rows.Add(new DashboardRow(t.Name, "Topic", 0, t.SubscriptionCount, 0));
        }

        ((INotifyCollectionChanged)queues.Queues).CollectionChanged += (_, _) => Rebuild();
        ((INotifyCollectionChanged)topics.Topics).CollectionChanged += (_, _) => Rebuild();

        _isLoading = Observable.CombineLatest(
            queues.WhenAnyValue(q => q.IsLoading),
            topics.WhenAnyValue(t => t.IsLoading),
            (q, t) => q || t)
            .ToProperty(this, x => x.IsLoading);

        RefreshCommand = ReactiveCommand.CreateFromTask(() =>
        {
            queues.RefreshCommand.Execute().Subscribe();
            topics.RefreshCommand.Execute().Subscribe();
            return Task.CompletedTask;
        });
    }
}
