using ReactiveUI;

namespace ServiceBusExplorer.ViewModels;

public class TopicDetailViewModel : ReactiveObject
{
    private TopicInfo? _topic;
    private bool _isLoading;
    private string? _error;

    public TopicInfo? Topic
    {
        get => _topic;
        set => this.RaiseAndSetIfChanged(ref _topic, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    public string? Error
    {
        get => _error;
        private set => this.RaiseAndSetIfChanged(ref _error, value);
    }

    public SubscriptionListViewModel Subscriptions { get; }

    public TopicDetailViewModel(ITopicService topicSvc, ISubscriptionService subscriptionSvc, string topicName)
    {
        Subscriptions = new SubscriptionListViewModel(subscriptionSvc, topicName);
    }
}
