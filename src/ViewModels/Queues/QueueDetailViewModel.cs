using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Subjects;
using DynamicData;
using ReactiveUI;

namespace ServiceBusExplorer.ViewModels;

public class QueueDetailViewModel : ReactiveObject
{
    private readonly IQueueService _svc;
    private readonly IMessageBrowseService _browseService;
    private readonly IMessageReceiveService _receiveService;
    private readonly IConfirmationService _confirmationService;
    private readonly Func<string, Task> _copyToClipboard;
    private readonly string _queueName;
    private readonly Subject<Unit> _navigateBack = new();
    private readonly SourceList<ObservedMessage> _observedSource = new();
    private readonly SourceList<ReceivedMessage> _receivedSource = new();
    private QueueInfo? _queue;
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
    private int _maxDeliveryCount;
    private TimeSpan _lockDuration;
    private TimeSpan _defaultMessageTimeToLive;
    private TimeSpan _autoDeleteOnIdle;
    private bool _enableBatchedOperations;
    private bool _enableDeadLetteringOnMessageExpiration;
    private string? _forwardTo;
    private string? _forwardDeadLetteredMessagesTo;
    private string? _userMetadata;
    private bool _isSaving;
    private string? _saveError;
    private string? _settlementStatus;

    public QueueInfo? Queue
    {
        get => _queue;
        private set
        {
            this.RaiseAndSetIfChanged(ref _queue, value);
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

    /// <summary>
    /// Last settlement outcome presentation (success, rejected ineligible, or failure).
    /// </summary>
    public string? SettlementStatus
    {
        get => _settlementStatus;
        private set => this.RaiseAndSetIfChanged(ref _settlementStatus, value);
    }

    /// <summary>
    /// True when the selected peek-locked message is currently eligible to settle.
    /// </summary>
    public bool CanSettleSelectedMessage =>
        _activeSession is not null
        && SelectedMessage is not null
        && SettlementStateMachine.CanSettle(_activeSession.GetSettlementState(SelectedMessage));

    /// <summary>
    /// Peeked browse messages are never settleable.
    /// </summary>
    public bool CanSettleSelectedObservedMessage =>
        SelectedObservedMessage is not null
        && SelectedObservedMessage.IsSettleableAt(DateTimeOffset.UtcNow);

    public ReceivedMessage? SelectedMessage
    {
        get => _selectedMessage;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedMessage, value);
            this.RaisePropertyChanged(nameof(CanSettleSelectedMessage));
        }
    }

    public ObservedMessage? SelectedObservedMessage
    {
        get => _selectedObservedMessage;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedObservedMessage, value);
            this.RaisePropertyChanged(nameof(CanSettleSelectedObservedMessage));
        }
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

    public int MaxDeliveryCount
    {
        get => _maxDeliveryCount;
        set => this.RaiseAndSetIfChanged(ref _maxDeliveryCount, value);
    }
    public TimeSpan LockDuration
    {
        get => _lockDuration;
        set => this.RaiseAndSetIfChanged(ref _lockDuration, value);
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
    public ReactiveCommand<Unit, Unit> ReceiveAndDeleteCommand { get; }
    public ReactiveCommand<Unit, Unit> CopyObservedBodyCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportObservedBodyCommand { get; }
    public ReactiveCommand<ReceivedMessage, Unit> CompleteCommand { get; }
    public ReactiveCommand<ReceivedMessage, Unit> AbandonCommand { get; }
    public ReactiveCommand<ReceivedMessage, Unit> DeferCommand { get; }
    public ReactiveCommand<ReceivedMessage, Unit> DeadLetterCommand { get; }

    public QueueDetailViewModel(
        IQueueService svc,
        IMessageBrowseService browseService,
        IMessageSendService sendService,
        IMessageReceiveService receiveService,
        IConfirmationService confirmationService,
        string queueName,
        Func<string, Task>? copyToClipboard = null)
    {
        _svc = svc;
        _browseService = browseService;
        _receiveService = receiveService;
        _confirmationService = confirmationService;
        _copyToClipboard = copyToClipboard ?? (_ => Task.CompletedTask);
        _queueName = queueName;

        _observedSource.Connect().Bind(out var observedBound).Subscribe();
        ObservedMessages = observedBound;

        _receivedSource.Connect().Bind(out var receivedBound).Subscribe();
        Messages = receivedBound;

        Send = new SendMessageViewModel(
            sendService,
            new SendTargetContext(SendTargetKind.Queue, queueName, queueName));

        NavigateBackCommand = ReactiveCommand.Create(() => _navigateBack.OnNext(Unit.Default));
        ToggleSendPanelCommand = ReactiveCommand.Create(() => { ShowSendPanel = !ShowSendPanel; });

        RefreshInfoCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            IsLoading = true;
            Error = null;
            try
            {
                Queue = await _svc.GetAsync(_queueName);
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
                    _queueName,
                    source,
                    "All messages in this source will be permanently removed.",
                    ConfirmationRisk.Irreversible));
            if (confirmation != ConfirmationResult.Confirmed)
                return;

            IsLoading = true;
            Error = null;
            try
            {
                await _svc.PurgeAsync(_queueName, source);
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
            if (Queue == null) return;
            IsSaving = true;
            SaveError = null;
            try
            {
                var updated = Queue with
                {
                    MaxDeliveryCount = MaxDeliveryCount,
                    LockDuration = LockDuration,
                    DefaultMessageTimeToLive = DefaultMessageTimeToLive,
                    AutoDeleteOnIdle = AutoDeleteOnIdle,
                    EnableBatchedOperations = EnableBatchedOperations,
                    EnableDeadLetteringOnMessageExpiration = EnableDeadLetteringOnMessageExpiration,
                    ForwardTo = ForwardTo,
                    ForwardDeadLetteredMessagesTo = ForwardDeadLetteredMessagesTo,
                    UserMetadata = UserMetadata,
                };
                Queue = await _svc.UpdateAsync(updated);
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

                _activeSession = await _receiveService.OpenPeekLockAsync(
                    new EntityAddress(_queueName),
                    source);
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

        ReceiveAndDeleteCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (SelectedSource is not { } source)
            {
                Error = "Select a message source before receive-and-delete.";
                return;
            }

            var address = new EntityAddress(_queueName);
            var decision = await _confirmationService.ConfirmAsync(
                new ConfirmationRequest(
                    _queueName,
                    source,
                    "Messages will be permanently removed from the broker and may not be fully displayable after deletion.",
                    ConfirmationRisk.Irreversible));

            if (!ReceiveAndDeleteConfirmation.TryCreate(decision, address, source, out var confirmation)
                || confirmation is null)
            {
                return;
            }

            IsLoading = true;
            Error = null;
            try
            {
                var result = await _receiveService.ReceiveAndDeleteAsync(
                    new ConfirmedReceiveAndDeleteRequest(confirmation, PeekCount));
                _receivedSource.Clear();
                _receivedSource.AddRange(result.Messages);
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

        CopyObservedBodyCommand = ReactiveCommand.CreateFromTask(CopySelectedObservedBodyAsync);
        ExportObservedBodyCommand = ReactiveCommand.CreateFromTask(ExportSelectedObservedBodyAsync);

        CompleteCommand = ReactiveCommand.CreateFromTask<ReceivedMessage>(msg =>
            SettleReceivedAsync(msg, SettlementAction.Complete));
        AbandonCommand = ReactiveCommand.CreateFromTask<ReceivedMessage>(msg =>
            SettleReceivedAsync(msg, SettlementAction.Abandon));
        DeferCommand = ReactiveCommand.CreateFromTask<ReceivedMessage>(msg =>
            SettleReceivedAsync(msg, SettlementAction.Defer));
        DeadLetterCommand = ReactiveCommand.CreateFromTask<ReceivedMessage>(msg =>
            SettleReceivedAsync(msg, SettlementAction.DeadLetter));

        RefreshInfoCommand.Execute().Subscribe();
    }

    private async Task SettleReceivedAsync(ReceivedMessage msg, SettlementAction action)
    {
        if (_activeSession is null)
        {
            SettlementStatus = "No active peek-lock session.";
            return;
        }

        if (SelectedObservedMessage is { } observed
            && (observed.ReceiveKind == MessageReceiveKind.Peeked
                || observed.SettlementState == SettlementState.Peeked)
            && string.Equals(observed.MessageId, msg.MessageId, StringComparison.Ordinal))
        {
            var rejected = _receiveService.RejectPeekedSettlement(observed, action);
            SettlementStatus = rejected.SafeMessage;
            this.RaisePropertyChanged(nameof(CanSettleSelectedObservedMessage));
            return;
        }

        try
        {
            var outcome = action switch
            {
                SettlementAction.Complete =>
                    await _receiveService.CompleteAsync(_activeSession, msg),
                SettlementAction.Abandon =>
                    await _receiveService.AbandonAsync(_activeSession, msg),
                SettlementAction.Defer =>
                    await _receiveService.DeferAsync(_activeSession, msg),
                SettlementAction.DeadLetter =>
                    await _receiveService.DeadLetterAsync(_activeSession, msg),
                _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
            };

            SettlementStatus = outcome.SafeMessage;
            if (outcome.Result == SettlementResultKind.Succeeded)
            {
                _receivedSource.Remove(msg);
            }

            this.RaisePropertyChanged(nameof(CanSettleSelectedMessage));
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            SettlementStatus = ex.Message;
        }
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
                new EntityAddress(_queueName),
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
            _queueName,
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

    private void PopulateEditableFields(QueueInfo q)
    {
        MaxDeliveryCount = q.MaxDeliveryCount;
        LockDuration = q.LockDuration;
        DefaultMessageTimeToLive = q.DefaultMessageTimeToLive;
        AutoDeleteOnIdle = q.AutoDeleteOnIdle;
        EnableBatchedOperations = q.EnableBatchedOperations;
        EnableDeadLetteringOnMessageExpiration = q.EnableDeadLetteringOnMessageExpiration;
        ForwardTo = q.ForwardTo;
        ForwardDeadLetteredMessagesTo = q.ForwardDeadLetteredMessagesTo;
        UserMetadata = q.UserMetadata;
    }
}
