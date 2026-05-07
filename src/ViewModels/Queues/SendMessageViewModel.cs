using System.Reactive;
using System.Text.Json;
using ReactiveUI;

namespace ServiceBusExplorer.ViewModels;

public class SendMessageViewModel : ReactiveObject
{
    private string _body = "";
    private string _contentType = "application/json";
    private string? _messageId;
    private string? _correlationId;
    private string? _sessionId;
    private string? _to;
    private string _propertiesJson = "";
    private bool _isSending;
    private string? _error;
    private int _sendCount = 1;
    private bool _useScheduledTime;
    private int _scheduleDelayMinutes = 5;

    public string Body
    {
        get => _body;
        set => this.RaiseAndSetIfChanged(ref _body, value);
    }

    public string ContentType
    {
        get => _contentType;
        set => this.RaiseAndSetIfChanged(ref _contentType, value);
    }

    public string? MessageId
    {
        get => _messageId;
        set => this.RaiseAndSetIfChanged(ref _messageId, value);
    }

    public string? CorrelationId
    {
        get => _correlationId;
        set => this.RaiseAndSetIfChanged(ref _correlationId, value);
    }

    public string? SessionId
    {
        get => _sessionId;
        set => this.RaiseAndSetIfChanged(ref _sessionId, value);
    }

    public string? To
    {
        get => _to;
        set => this.RaiseAndSetIfChanged(ref _to, value);
    }

    public string PropertiesJson
    {
        get => _propertiesJson;
        set => this.RaiseAndSetIfChanged(ref _propertiesJson, value);
    }

    public bool IsSending
    {
        get => _isSending;
        private set => this.RaiseAndSetIfChanged(ref _isSending, value);
    }

    public string? Error
    {
        get => _error;
        private set => this.RaiseAndSetIfChanged(ref _error, value);
    }

    public int SendCount
    {
        get => _sendCount;
        set => this.RaiseAndSetIfChanged(ref _sendCount, value);
    }

    public bool UseScheduledTime
    {
        get => _useScheduledTime;
        set => this.RaiseAndSetIfChanged(ref _useScheduledTime, value);
    }

    public int ScheduleDelayMinutes
    {
        get => _scheduleDelayMinutes;
        set => this.RaiseAndSetIfChanged(ref _scheduleDelayMinutes, value);
    }

    public ReactiveCommand<Unit, Unit> SendCommand { get; }

    public SendMessageViewModel(IQueueService svc, string entityPath)
    {
        var canSend = this.WhenAnyValue(x => x.IsSending, sending => !sending);
        SendCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            IsSending = true;
            Error = null;
            try
            {
                IReadOnlyDictionary<string, object>? props = null;
                if (!string.IsNullOrWhiteSpace(PropertiesJson))
                {
                    try
                    {
                        props = JsonSerializer.Deserialize<Dictionary<string, object>>(PropertiesJson);
                    }
                    catch
                    {
                        Error = "Invalid JSON in Application Properties — send cancelled.";
                        return;
                    }
                }

                var baseMsg = new OutboundMessage(
                    Body: Body,
                    ContentType: string.IsNullOrWhiteSpace(ContentType) ? "application/json" : ContentType,
                    MessageId: string.IsNullOrWhiteSpace(MessageId) ? null : MessageId,
                    CorrelationId: string.IsNullOrWhiteSpace(CorrelationId) ? null : CorrelationId,
                    SessionId: string.IsNullOrWhiteSpace(SessionId) ? null : SessionId,
                    To: string.IsNullOrWhiteSpace(To) ? null : To,
                    Properties: props,
                    ScheduledEnqueueTime: UseScheduledTime
                        ? DateTimeOffset.Now.AddMinutes(ScheduleDelayMinutes)
                        : null);

                var count = Math.Max(1, SendCount);
                for (var i = 0; i < count; i++)
                {
                    await svc.SendAsync(entityPath, baseMsg);
                    if (i < count - 1)
                        await Task.Delay(50); // brief pause between batch sends
                }
                Body = "";
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
            finally
            {
                IsSending = false;
            }
        }, canSend);
    }
}
