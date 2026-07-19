using System.Reactive;
using System.Text.Json;
using ReactiveUI;
using ServiceBusExplorer;

namespace ServiceBusExplorer.ViewModels;

public class SendMessageViewModel : ReactiveObject
{
    private string _body = "";
    private string _contentType = "application/json";
    private string? _messageId;
    private string? _correlationId;
    private string? _sessionId;
    private string? _to;
    private string? _subject;
    private string? _replyTo;
    private string? _replyToSessionId;
    private string? _partitionKey;
    private string _propertiesJson = "";
    private bool _isSending;
    private string? _error;
    private string? _outcome;
    private int _sendCount = 1;
    private bool _useScheduledTime;
    private TimeSpan _scheduleDelay = TimeSpan.FromMinutes(5);
    private bool _useTimeToLive;
    private TimeSpan _timeToLive = TimeSpan.FromHours(1);

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

    public string? Subject
    {
        get => _subject;
        set => this.RaiseAndSetIfChanged(ref _subject, value);
    }

    public string? ReplyTo
    {
        get => _replyTo;
        set => this.RaiseAndSetIfChanged(ref _replyTo, value);
    }

    public string? ReplyToSessionId
    {
        get => _replyToSessionId;
        set => this.RaiseAndSetIfChanged(ref _replyToSessionId, value);
    }

    public string? PartitionKey
    {
        get => _partitionKey;
        set => this.RaiseAndSetIfChanged(ref _partitionKey, value);
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

    public string? Outcome
    {
        get => _outcome;
        private set => this.RaiseAndSetIfChanged(ref _outcome, value);
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

    public TimeSpan ScheduleDelay
    {
        get => _scheduleDelay;
        set => this.RaiseAndSetIfChanged(ref _scheduleDelay, value);
    }

    public bool UseTimeToLive
    {
        get => _useTimeToLive;
        set => this.RaiseAndSetIfChanged(ref _useTimeToLive, value);
    }

    public TimeSpan TimeToLive
    {
        get => _timeToLive;
        set => this.RaiseAndSetIfChanged(ref _timeToLive, value);
    }

    public ReactiveCommand<Unit, Unit> SendCommand { get; }
    public SendTargetContext Target { get; }
    public string DestinationDescription => Target.DestinationDescription;

    public SendMessageViewModel(IMessageSendService sendService, SendTargetContext target)
    {
        Target = target;
        var canSend = this.WhenAnyValue(x => x.IsSending, sending => !sending);
        SendCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            IsSending = true;
            Error = null;
            Outcome = null;
            try
            {
                if (!TryBuildDraft(out var draft, out var buildError))
                {
                    Error = buildError;
                    return;
                }

                var result = await sendService.SendAsync(Target, draft!, SendCount);
                if (result.Status == MessageSendStatus.Succeeded)
                {
                    Body = "";
                    Outcome = result.SafeMessage;
                }
                else
                {
                    Error = result.SafeMessage;
                }
            }
            finally
            {
                IsSending = false;
            }
        }, canSend);
    }

    private bool TryBuildDraft(out MessageDraft? draft, out string? error)
    {
        draft = null;
        error = null;

        var built = new MessageDraft
        {
            DestinationPath = Target.ActualDestinationPath,
            ContentType = ContentType,
            MessageId = MessageId,
            CorrelationId = CorrelationId,
            SessionId = SessionId,
            To = To,
            Subject = Subject,
            ReplyTo = ReplyTo,
            ReplyToSessionId = ReplyToSessionId,
            PartitionKey = PartitionKey
        };
        built.SetBodyText(
            Body,
            string.Equals(ContentType, "application/json", StringComparison.OrdinalIgnoreCase)
                ? MessageBodyKind.Json
                : MessageBodyKind.Text);

        if (UseScheduledTime)
        {
            try
            {
                built.ScheduleDelay = DurationValue.FromTimeSpan(ScheduleDelay);
            }
            catch (ArgumentException)
            {
                error = $"{Target.FailurePrefix}: Schedule delay must be a non-negative whole-millisecond duration.";
                return false;
            }
        }

        if (UseTimeToLive)
        {
            try
            {
                built.TimeToLive = DurationValue.FromTimeSpan(TimeToLive);
            }
            catch (ArgumentException)
            {
                error = $"{Target.FailurePrefix}: Time to live must be a non-negative whole-millisecond duration.";
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(PropertiesJson))
        {
            try
            {
                using var document = JsonDocument.Parse(PropertiesJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    error = $"{Target.FailurePrefix}: Application properties must be a JSON object.";
                    return false;
                }

                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (!TryMapJsonProperty(property, out var typed, out var mapError))
                    {
                        error = $"{Target.FailurePrefix}: {mapError}";
                        return false;
                    }

                    built.CustomProperties.Add(typed!);
                }
            }
            catch (JsonException)
            {
                error = $"{Target.FailurePrefix}: invalid JSON in Application Properties.";
                return false;
            }
        }

        draft = built;
        return true;
    }

    private static bool TryMapJsonProperty(
        JsonProperty property,
        out TypedMessageProperty? typed,
        out string? error)
    {
        typed = null;
        error = null;

        switch (property.Value.ValueKind)
        {
            case JsonValueKind.String:
                typed = new TypedMessageProperty(property.Name, MessagePropertyType.String, property.Value.GetString());
                return true;
            case JsonValueKind.True:
            case JsonValueKind.False:
                typed = new TypedMessageProperty(property.Name, MessagePropertyType.Boolean, property.Value.GetBoolean());
                return true;
            case JsonValueKind.Number when property.Value.TryGetInt64(out var int64):
                typed = new TypedMessageProperty(property.Name, MessagePropertyType.Int64, int64);
                return true;
            case JsonValueKind.Number:
                typed = new TypedMessageProperty(property.Name, MessagePropertyType.Double, property.Value.GetDouble());
                return true;
            case JsonValueKind.Null:
                typed = new TypedMessageProperty(property.Name, MessagePropertyType.String, null);
                return true;
            default:
                error = $"Application property '{property.Name}' must be a string, number, boolean, or null.";
                return false;
        }
    }
}
