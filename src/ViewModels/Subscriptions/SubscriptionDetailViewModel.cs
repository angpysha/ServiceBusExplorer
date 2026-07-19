using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Subjects;
using DynamicData;
using ReactiveUI;

namespace ServiceBusExplorer.ViewModels;

public class SubscriptionDetailViewModel : ReactiveObject
{
    private readonly ISubscriptionService _subSvc;
    private readonly IQueueService _queueSvc;
    private readonly IMessageBrowseService _browseService;
    private readonly IConfirmationService _confirmationService;
    private readonly Func<string, Task> _copyToClipboard;
    private readonly string _entityPath;
    private readonly string _topicName;
    private readonly string _subscriptionName;
    private readonly Subject<Unit> _navigateBack = new();
    private readonly SourceList<ObservedMessage> _observedSource = new();
    private readonly SourceList<ReceivedMessage> _receivedSource = new();
    private SubscriptionInfo? _info;
    private bool _isLoading;
    private string? _error;
    private ReceivedMessage? _selectedMessage;
    private ObservedMessage? _selectedObservedMessage;
    private int _peekCount = 20;
    private MessageSource? _selectedSource;
    private bool _showSendPanel;
    private bool _isReceiveMode;
    private IReceiveSession? _activeSession;
    private SourceAvailability _browseAvailability = SourceAvailability.Empty;
    private BrowseContinuation? _browseContinuation;
    private bool _userAcceptedSensitiveCopy;

    // Editable fields
    private TimeSpan _lockDuration;
    private int _maxDeliveryCount;
    private TimeSpan _defaultMessageTimeToLive;
    private TimeSpan _autoDeleteOnIdle;
    private bool _enableBatchedOperations;
    private bool _enableDeadLetteringOnMessageExpiration;
    private string? _forwardTo;
    private string? _forwardDeadLetteredMessagesTo;
    private string? _userMetadata;
    private bool _isSaving;
    private string? _saveError;

    public SubscriptionInfo? Info
    {
        get => _info;
        private set
        {
            this.RaiseAndSetIfChanged(ref _info, value);
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

    public ReceivedMessage? SelectedMessage
    {
        get => _selectedMessage;
        set => this.RaiseAndSetIfChanged(ref _selectedMessage, value);
    }

    public ObservedMessage? SelectedObservedMessage
    {
        get => _selectedObservedMessage;
        set => this.RaiseAndSetIfChanged(ref _selectedObservedMessage, value);
    }

    public int PeekCount
    {
        get => _peekCount;
        set => this.RaiseAndSetIfChanged(ref _peekCount, value);
    }

    public MessageSource? SelectedSource
    {
        get => _selectedSource;
        set => this.RaiseAndSetIfChanged(ref _selectedSource, value);
    }

    public SourceAvailability BrowseAvailability
    {
        get => _browseAvailability;
        private set => this.RaiseAndSetIfChanged(ref _browseAvailability, value);
    }

    public bool HasMoreObservedMessages => _browseContinuation is not null;

    public bool UserAcceptedSensitiveCopy
    {
        get => _userAcceptedSensitiveCopy;
        private set => this.RaiseAndSetIfChanged(ref _userAcceptedSensitiveCopy, value);
    }

    public bool ShowSendPanel
    {
        get => _showSendPanel;
        set => this.RaiseAndSetIfChanged(ref _showSendPanel, value);
    }

    public bool IsReceiveMode
    {
        get => _isReceiveMode;
        private set => this.RaiseAndSetIfChanged(ref _isReceiveMode, value);
    }

    public TimeSpan LockDuration
    {
        get => _lockDuration;
        set => this.RaiseAndSetIfChanged(ref _lockDuration, value);
    }
    public int MaxDeliveryCount
    {
        get => _maxDeliveryCount;
        set => this.RaiseAndSetIfChanged(ref _maxDeliveryCount, value);
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
    public bool EnableBatchedOperations
    {
        get => _enableBatchedOperations;
        set => this.RaiseAndSetIfChanged(ref _enableBatchedOperations, value);
    }
    public bool EnableDeadLetteringOnMessageExpiration
    {
        get => _enableDeadLetteringOnMessageExpiration;
        set => this.RaiseAndSetIfChanged(ref _enableDeadLetteringOnMessageExpiration, value);
    }
    public string? ForwardTo
    {
        get => _forwardTo;
        set => this.RaiseAndSetIfChanged(ref _forwardTo, value);
    }
    public string? ForwardDeadLetteredMessagesTo
    {
        get => _forwardDeadLetteredMessagesTo;
        set => this.RaiseAndSetIfChanged(ref _forwardDeadLetteredMessagesTo, value);
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

    public static IReadOnlyList<MessageSource> SourceOptions { get; } =
        Enum.GetValues<MessageSource>();

    public ReadOnlyObservableCollection<ObservedMessage> ObservedMessages { get; }
    public ReadOnlyObservableCollection<ReceivedMessage> Messages { get; }
    public RuleListViewModel Rules { get; }
    public SendMessageViewModel Send { get; }

    public IObservable<Unit> NavigateBackRequested => _navigateBack;
    public ReactiveCommand<Unit, Unit> NavigateBackCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshInfoCommand { get; }
    public ReactiveCommand<Unit, Unit> PeekCommand { get; }
    public ReactiveCommand<Unit, Unit> LoadMoreObservedCommand { get; }
    public ReactiveCommand<Unit, Unit> PurgeCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleSendPanelCommand { get; }
    public ReactiveCommand<Unit, Unit> UpdateCommand { get; }
    public ReactiveCommand<Unit, Unit> StartReceiveCommand { get; }
    public ReactiveCommand<Unit, Unit> StopReceiveCommand { get; }
    public ReactiveCommand<Unit, Unit> ReceiveBatchCommand { get; }
    public ReactiveCommand<Unit, Unit> CopyObservedBodyCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportObservedBodyCommand { get; }
    public ReactiveCommand<ReceivedMessage, Unit> CompleteCommand { get; }
    public ReactiveCommand<ReceivedMessage, Unit> AbandonCommand { get; }
    public ReactiveCommand<ReceivedMessage, Unit> DeadLetterCommand { get; }

    public SubscriptionDetailViewModel(
        ISubscriptionService subSvc,
        IQueueService queueSvc,
        IMessageBrowseService browseService,
        IMessageSendService sendService,
        IConfirmationService confirmationService,
        string topicName,
        string subscriptionName,
        Func<string, Task>? copyToClipboard = null)
    {
        _subSvc = subSvc;
        _queueSvc = queueSvc;
        _browseService = browseService;
        _confirmationService = confirmationService;
        _copyToClipboard = copyToClipboard ?? (_ => Task.CompletedTask);
        _topicName = topicName;
        _subscriptionName = subscriptionName;
        _entityPath = $"{topicName}/Subscriptions/{subscriptionName}";

        _observedSource.Connect().Bind(out var observedBound).Subscribe();
        ObservedMessages = observedBound;

        _receivedSource.Connect().Bind(out var receivedBound).Subscribe();
        Messages = receivedBound;

        Rules = new RuleListViewModel(subSvc, topicName, subscriptionName);
        Send = new SendMessageViewModel(
            sendService,
            new SendTargetContext(
                SendTargetKind.Subscription,
                _entityPath,
                topicName));

        NavigateBackCommand = ReactiveCommand.Create(() => _navigateBack.OnNext(Unit.Default));
        ToggleSendPanelCommand = ReactiveCommand.Create(() => { ShowSendPanel = !ShowSendPanel; });

        RefreshInfoCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            IsLoading = true;
            Error = null;
            try
            {
                Info = await subSvc.GetAsync(topicName, subscriptionName);
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

        PeekCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            await BrowseAsync(append: false);
        });

        LoadMoreObservedCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            await BrowseAsync(append: true);
        }, this.WhenAnyValue(x => x.HasMoreObservedMessages));

        PurgeCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (SelectedSource is not { } source)
            {
                Error = "Select a message source before purging.";
                return;
            }

            var confirmation = await _confirmationService.ConfirmAsync(
                new ConfirmationRequest(
                    _entityPath,
                    source,
                    "All messages in this source will be permanently removed.",
                    ConfirmationRisk.Irreversible));
            if (confirmation != ConfirmationResult.Confirmed)
                return;

            IsLoading = true;
            Error = null;
            try
            {
                await queueSvc.PurgeAsync(_entityPath, source);
                _observedSource.Clear();
                _receivedSource.Clear();
                _browseContinuation = null;
                this.RaisePropertyChanged(nameof(HasMoreObservedMessages));
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
            if (Info == null) return;
            IsSaving = true;
            SaveError = null;
            try
            {
                var updated = Info with
                {
                    LockDuration = LockDuration,
                    MaxDeliveryCount = MaxDeliveryCount,
                    DefaultMessageTimeToLive = DefaultMessageTimeToLive,
                    AutoDeleteOnIdle = AutoDeleteOnIdle,
                    EnableBatchedOperations = EnableBatchedOperations,
                    EnableDeadLetteringOnMessageExpiration = EnableDeadLetteringOnMessageExpiration,
                    ForwardTo = ForwardTo,
                    ForwardDeadLetteredMessagesTo = ForwardDeadLetteredMessagesTo,
                    UserMetadata = UserMetadata,
                };
                Info = await _subSvc.UpdateAsync(updated);
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

        var hasSession = this.WhenAnyValue(x => x.IsReceiveMode);
        var noSession = this.WhenAnyValue(x => x.IsReceiveMode, m => !m);

        StartReceiveCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            IsLoading = true;
            Error = null;
            try
            {
                if (SelectedSource is not { } source)
                {
                    Error = "Select a message source before receiving.";
                    return;
                }

                _activeSession = await queueSvc.OpenReceiveSessionAsync(_entityPath, source);
                _receivedSource.Clear();
                IsReceiveMode = true;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }, noSession);

        StopReceiveCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (_activeSession != null)
            {
                await _activeSession.DisposeAsync();
                _activeSession = null;
            }
            IsReceiveMode = false;
        }, hasSession);

        ReceiveBatchCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (_activeSession == null) return;
            IsLoading = true;
            Error = null;
            try
            {
                var msgs = await _activeSession.ReceiveBatchAsync(PeekCount);
                _receivedSource.AddRange(msgs);
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }, hasSession);

        CopyObservedBodyCommand = ReactiveCommand.CreateFromTask(CopySelectedObservedBodyAsync);
        ExportObservedBodyCommand = ReactiveCommand.CreateFromTask(ExportSelectedObservedBodyAsync);

        CompleteCommand = ReactiveCommand.CreateFromTask<ReceivedMessage>(async msg =>
        {
            if (_activeSession == null) return;
            try
            {
                await _activeSession.CompleteAsync(msg);
                _receivedSource.Remove(msg);
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
        });

        AbandonCommand = ReactiveCommand.CreateFromTask<ReceivedMessage>(async msg =>
        {
            if (_activeSession == null) return;
            try
            {
                await _activeSession.AbandonAsync(msg);
                _receivedSource.Remove(msg);
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
        });

        DeadLetterCommand = ReactiveCommand.CreateFromTask<ReceivedMessage>(async msg =>
        {
            if (_activeSession == null) return;
            try
            {
                await _activeSession.DeadLetterAsync(msg);
                _receivedSource.Remove(msg);
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
        });

        RefreshInfoCommand.Execute().Subscribe();
        Rules.RefreshCommand.Execute().Subscribe();
    }

    private async Task BrowseAsync(bool append)
    {
        IsLoading = true;
        Error = null;
        try
        {
            if (SelectedSource is not { } source)
            {
                Error = "Select a message source before peeking.";
                return;
            }

            var fromSequence = append ? _browseContinuation?.FromSequenceNumber : null;
            var result = await _browseService.PeekAsync(
                new EntityAddress(_entityPath),
                source,
                new PageRequest(PeekCount, fromSequence));

            BrowseAvailability = result.Availability;
            _browseContinuation = result.Continuation;
            this.RaisePropertyChanged(nameof(HasMoreObservedMessages));

            if (!append)
            {
                _observedSource.Edit(list =>
                {
                    list.Clear();
                    list.AddRange(result.Messages);
                });
                UserAcceptedSensitiveCopy = false;
            }
            else
            {
                _observedSource.AddRange(result.Messages);
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task CopySelectedObservedBodyAsync()
    {
        if (SelectedObservedMessage is not { } message)
            return;

        if (!await ConfirmSensitiveCopyAsync())
            return;

        var text = GetCopyableBodyText(message);
        if (text is null)
            return;

        await _copyToClipboard(text);
        UserAcceptedSensitiveCopy = true;
    }

    private async Task ExportSelectedObservedBodyAsync()
    {
        if (SelectedObservedMessage is not { } message)
            return;

        if (!await ConfirmSensitiveCopyAsync())
            return;

        var text = GetCopyableBodyText(message);
        if (text is null)
            return;

        await _copyToClipboard(text);
        UserAcceptedSensitiveCopy = true;
    }

    private async Task<bool> ConfirmSensitiveCopyAsync()
    {
        if (SelectedSource is not { } source)
            return false;

        return await SensitiveContentCopy.ConfirmAsync(
            _confirmationService,
            _entityPath,
            source);
    }

    private static string? GetCopyableBodyText(ObservedMessage message) =>
        message.Body.Kind switch
        {
            MessageBodyKind.Empty => message.Body.DisplayText,
            MessageBodyKind.Unavailable => null,
            MessageBodyKind.Binary => message.Body.DisplayText,
            MessageBodyKind.Truncated => message.Body.DisplayText,
            _ => message.Body.DisplayText
        };

    private void PopulateEditableFields(SubscriptionInfo s)
    {
        LockDuration = s.LockDuration;
        MaxDeliveryCount = s.MaxDeliveryCount;
        DefaultMessageTimeToLive = s.DefaultMessageTimeToLive;
        AutoDeleteOnIdle = s.AutoDeleteOnIdle;
        EnableBatchedOperations = s.EnableBatchedOperations;
        EnableDeadLetteringOnMessageExpiration = s.EnableDeadLetteringOnMessageExpiration;
        ForwardTo = s.ForwardTo;
        ForwardDeadLetteredMessagesTo = s.ForwardDeadLetteredMessagesTo;
        UserMetadata = s.UserMetadata;
    }
}
