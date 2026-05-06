using System.Reactive;
using System.Reactive.Subjects;
using ReactiveUI;

namespace ServiceBusExplorer.ViewModels;

public class TopicDetailViewModel : ReactiveObject
{
    private readonly ITopicService _topicSvc;
    private readonly string _topicName;
    private readonly Subject<Unit> _navigateBack = new();
    private TopicInfo? _topic;
    private bool _isLoading;
    private string? _error;
    private SubscriptionDetailViewModel? _selectedSubscriptionDetail;
    private bool _showSendPanel;

    // Editable fields
    private TimeSpan _defaultMessageTimeToLive;
    private TimeSpan _autoDeleteOnIdle;
    private long _maxSizeInMegabytes;
    private bool _enableBatchedOperations;
    private string? _userMetadata;
    private bool _isSaving;
    private string? _saveError;

    public TopicInfo? Topic
    {
        get => _topic;
        private set
        {
            this.RaiseAndSetIfChanged(ref _topic, value);
            if (value != null) PopulateEditableFields(value);
        }
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

    public SubscriptionDetailViewModel? SelectedSubscriptionDetail
    {
        get => _selectedSubscriptionDetail;
        private set => this.RaiseAndSetIfChanged(ref _selectedSubscriptionDetail, value);
    }

    public bool ShowSendPanel
    {
        get => _showSendPanel;
        set => this.RaiseAndSetIfChanged(ref _showSendPanel, value);
    }

    public TimeSpan DefaultMessageTimeToLive
    {
        get => _defaultMessageTimeToLive;
        set => this.RaiseAndSetIfChanged(ref _defaultMessageTimeToLive, value);
    }
    public TimeSpan AutoDeleteOnIdle
    {
        get => _autoDeleteOnIdle;
        set => this.RaiseAndSetIfChanged(ref _autoDeleteOnIdle, value);
    }
    public long MaxSizeInMegabytes
    {
        get => _maxSizeInMegabytes;
        set => this.RaiseAndSetIfChanged(ref _maxSizeInMegabytes, value);
    }
    public bool EnableBatchedOperations
    {
        get => _enableBatchedOperations;
        set => this.RaiseAndSetIfChanged(ref _enableBatchedOperations, value);
    }
    public string? UserMetadata
    {
        get => _userMetadata;
        set => this.RaiseAndSetIfChanged(ref _userMetadata, value);
    }
    public bool IsSaving
    {
        get => _isSaving;
        private set => this.RaiseAndSetIfChanged(ref _isSaving, value);
    }
    public string? SaveError
    {
        get => _saveError;
        private set => this.RaiseAndSetIfChanged(ref _saveError, value);
    }

    public SubscriptionListViewModel Subscriptions { get; }
    public SendMessageViewModel Send { get; }

    public IObservable<Unit> NavigateBackRequested => _navigateBack;
    public ReactiveCommand<Unit, Unit> NavigateBackCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshInfoCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleSendPanelCommand { get; }
    public ReactiveCommand<Unit, Unit> UpdateCommand { get; }

    public TopicDetailViewModel(
        ITopicService topicSvc,
        ISubscriptionService subscriptionSvc,
        IQueueService queueSvc,
        string topicName)
    {
        _topicSvc = topicSvc;
        _topicName = topicName;
        Subscriptions = new SubscriptionListViewModel(subscriptionSvc, topicName);
        Send = new SendMessageViewModel(queueSvc, topicName);

        NavigateBackCommand = ReactiveCommand.Create(() => _navigateBack.OnNext(Unit.Default));
        ToggleSendPanelCommand = ReactiveCommand.Create(() => { ShowSendPanel = !ShowSendPanel; });

        Subscriptions.WhenAnyValue(x => x.SelectedSubscription)
            .Subscribe(sub =>
            {
                var detail = sub == null
                    ? null
                    : new SubscriptionDetailViewModel(subscriptionSvc, queueSvc, topicName, sub.Name);
                if (detail != null)
                    detail.NavigateBackRequested.Subscribe(_ => Subscriptions.SelectedSubscription = null);
                SelectedSubscriptionDetail = detail;
            });

        RefreshInfoCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            IsLoading = true;
            Error = null;
            try
            {
                Topic = await topicSvc.GetAsync(topicName);
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        });

        UpdateCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (Topic == null) return;
            IsSaving = true;
            SaveError = null;
            try
            {
                var updated = Topic with
                {
                    DefaultMessageTimeToLive = DefaultMessageTimeToLive,
                    AutoDeleteOnIdle = AutoDeleteOnIdle,
                    MaxSizeInMegabytes = MaxSizeInMegabytes,
                    EnableBatchedOperations = EnableBatchedOperations,
                    UserMetadata = UserMetadata,
                };
                Topic = await _topicSvc.UpdateAsync(updated);
            }
            catch (Exception ex)
            {
                SaveError = ex.Message;
            }
            finally
            {
                IsSaving = false;
            }
        });

        RefreshInfoCommand.Execute().Subscribe();
        Subscriptions.RefreshCommand.Execute().Subscribe();
    }

    private void PopulateEditableFields(TopicInfo t)
    {
        DefaultMessageTimeToLive = t.DefaultMessageTimeToLive;
        AutoDeleteOnIdle = t.AutoDeleteOnIdle;
        MaxSizeInMegabytes = t.MaxSizeInMegabytes;
        EnableBatchedOperations = t.EnableBatchedOperations;
        UserMetadata = t.UserMetadata;
    }
}
