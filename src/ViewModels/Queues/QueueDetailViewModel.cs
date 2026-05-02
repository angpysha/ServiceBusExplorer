using System.Collections.ObjectModel;
using System.Reactive;
using DynamicData;
using ReactiveUI;

namespace ServiceBusExplorer.ViewModels;

public class QueueDetailViewModel : ReactiveObject
{
    private readonly IQueueService _svc;
    private readonly SourceList<ReceivedMessage> _messageSource = new();
    private QueueInfo? _queue;
    private bool _isLoading;
    private string? _error;

    public QueueInfo? Queue
    {
        get => _queue;
        set => this.RaiseAndSetIfChanged(ref _queue, value);
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

    public ReadOnlyObservableCollection<ReceivedMessage> Messages { get; }

    public ReactiveCommand<(int MaxCount, MessageSubQueue Sub), IReadOnlyList<ReceivedMessage>> PeekCommand { get; }
    public ReactiveCommand<OutboundMessage, Unit> SendCommand { get; }
    public ReactiveCommand<MessageSubQueue, Unit> PurgeCommand { get; }

    public QueueDetailViewModel(IQueueService svc, string queueName)
    {
        _svc = svc;

        _messageSource.Connect()
            .Bind(out var bound)
            .Subscribe();
        Messages = bound;

        PeekCommand = ReactiveCommand.CreateFromTask<(int, MessageSubQueue), IReadOnlyList<ReceivedMessage>>(
            async args =>
            {
                IsLoading = true;
                Error = null;
                try
                {
                    var msgs = await _svc.PeekAsync(queueName, args.Item1, args.Item2);
                    _messageSource.Edit(list =>
                    {
                        list.Clear();
                        list.AddRange(msgs);
                    });
                    return msgs;
                }
                catch (Exception ex)
                {
                    Error = ex.Message;
                    return Array.Empty<ReceivedMessage>();
                }
                finally
                {
                    IsLoading = false;
                }
            });

        SendCommand = ReactiveCommand.CreateFromTask<OutboundMessage, Unit>(async msg =>
        {
            await _svc.SendAsync(queueName, msg);
            return Unit.Default;
        });

        PurgeCommand = ReactiveCommand.CreateFromTask<MessageSubQueue, Unit>(async sub =>
        {
            await _svc.PurgeAsync(queueName, sub);
            _messageSource.Clear();
            return Unit.Default;
        });
    }
}
