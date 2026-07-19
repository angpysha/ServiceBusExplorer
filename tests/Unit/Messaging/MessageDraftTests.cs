using System.Text;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceBusExplorer.Services;
using Xunit;

namespace ServiceBusExplorer.UnitTests.Messaging;

public class MessageDraftTests
{
    [Fact]
    public void Validate_RejectsEmptyBody()
    {
        var draft = new MessageDraft();
        draft.SetBodyText("   ");

        var result = draft.Validate();

        Assert.False(result.IsValid);
        Assert.Equal(MessageDraft.ErrorEmptyBody, result.ErrorCode);
    }

    [Fact]
    public void Validate_RejectsReservedPropertyName_CaseInsensitive()
    {
        var draft = CreateValidDraft();
        draft.CustomProperties.Add(new TypedMessageProperty("messageId", MessagePropertyType.String, "x"));

        var result = draft.Validate();

        Assert.False(result.IsValid);
        Assert.Equal(MessageDraft.ErrorReservedPropertyName, result.ErrorCode);
        Assert.Contains("reserved", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsDuplicatePropertyNames_CaseInsensitive()
    {
        var draft = CreateValidDraft();
        draft.CustomProperties.Add(new TypedMessageProperty("Region", MessagePropertyType.String, "west"));
        draft.CustomProperties.Add(new TypedMessageProperty("region", MessagePropertyType.String, "east"));

        var result = draft.Validate();

        Assert.False(result.IsValid);
        Assert.Equal(MessageDraft.ErrorDuplicatePropertyName, result.ErrorCode);
    }

    [Fact]
    public void Validate_RejectsMismatchedTypedPropertyValue()
    {
        var draft = CreateValidDraft();
        draft.CustomProperties.Add(new TypedMessageProperty("Count", MessagePropertyType.Int64, "not-a-number"));

        var result = draft.Validate();

        Assert.False(result.IsValid);
        Assert.Equal(MessageDraft.ErrorInvalidPropertyValue, result.ErrorCode);
    }

    [Fact]
    public void Validate_AcceptsTypedPropertiesAndFullPrecisionTtl_WithoutDayCap()
    {
        var draft = CreateValidDraft();
        draft.TimeToLive = DurationValue.Create(400, 3, 4, 5, 6);
        draft.CustomProperties.Add(new TypedMessageProperty("Priority", MessagePropertyType.Int64, 3L));
        draft.CustomProperties.Add(new TypedMessageProperty("Flag", MessagePropertyType.Boolean, true));
        draft.ReplyTo = "reply-queue";
        draft.ReplyToSessionId = "reply-session";
        draft.Subject = "orders";
        draft.PartitionKey = "pk-1";
        draft.SessionId = "session-1";
        draft.CorrelationId = "corr-1";

        var result = draft.Validate();

        Assert.True(result.IsValid);
        Assert.Equal(400, draft.TimeToLive.Value.Days);
        Assert.Equal(6, draft.TimeToLive.Value.Milliseconds);
        Assert.Null(DurationConstraint.ScheduledEnqueueDelay.Validate(DurationValue.Create(0, 0, 1, 0, 0)));
        Assert.NotNull(DurationConstraint.ScheduledEnqueueDelay.Validate(draft.TimeToLive.Value));
    }

    [Fact]
    public void Validate_PreservesDraftFieldsAfterFailure()
    {
        var draft = CreateValidDraft();
        draft.SetBodyText("keep-me");
        draft.MessageId = "draft-id";
        draft.CustomProperties.Add(new TypedMessageProperty("MessageId", MessagePropertyType.String, "conflict"));

        var beforeBody = draft.GetBodyText();
        var beforeId = draft.MessageId;
        var beforeProps = draft.CustomProperties.Count;

        var result = draft.Validate();

        Assert.False(result.IsValid);
        Assert.Equal(beforeBody, draft.GetBodyText());
        Assert.Equal(beforeId, draft.MessageId);
        Assert.Equal(beforeProps, draft.CustomProperties.Count);
    }

    [Fact]
    public async Task SendAsync_ValidationFailure_PreservesDraftAndDoesNotCallBackend()
    {
        var queue = new RecordingQueueService();
        var service = CreateSendService(queue);
        var target = new SendTargetContext(SendTargetKind.Queue, "orders", "orders");
        var draft = new MessageDraft { DestinationPath = "orders" };
        draft.SetBodyText("   ");
        draft.MessageId = "still-here";

        var result = await service.SendAsync(target, draft);

        Assert.Equal(MessageSendStatus.ValidationFailed, result.Status);
        Assert.Equal(MessageDraft.ErrorEmptyBody, result.ValidationErrorCode);
        Assert.Equal(ConnectionFailureCategory.Validation, result.FailureCategory);
        Assert.Equal("still-here", draft.MessageId);
        Assert.Equal("   ", draft.GetBodyText());
        Assert.Empty(queue.SendCalls);
        Assert.DoesNotContain("still-here", result.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_MapsRichFieldsAndTypedProperties_ToCurrentPath()
    {
        var queue = new RecordingQueueService();
        var service = CreateSendService(queue);
        var target = new SendTargetContext(SendTargetKind.Subscription, "sales/Subscriptions/regional", "sales");
        var draft = CreateValidDraft();
        draft.DestinationPath = "sales";
        draft.Subject = "invoice";
        draft.ReplyTo = "acks";
        draft.ReplyToSessionId = "ack-session";
        draft.PartitionKey = "tenant-a";
        draft.TimeToLive = DurationValue.Create(0, 1, 2, 3, 4);
        draft.ScheduleDelay = DurationValue.Create(0, 0, 2, 0, 0);
        draft.CustomProperties.Add(new TypedMessageProperty("Amount", MessagePropertyType.Double, 12.5));

        var result = await service.SendAsync(target, draft);

        Assert.Equal(MessageSendStatus.Succeeded, result.Status);
        Assert.Contains("parent topic", result.SafeMessage);
        var call = Assert.Single(queue.SendCalls);
        Assert.Equal("sales", call.EntityPath);
        Assert.Equal("invoice", call.Message.Subject);
        Assert.Equal("acks", call.Message.ReplyTo);
        Assert.Equal("ack-session", call.Message.ReplyToSessionId);
        Assert.Equal("tenant-a", call.Message.PartitionKey);
        Assert.Equal(draft.TimeToLive.Value.ToTimeSpan(), call.Message.TimeToLive);
        Assert.NotNull(call.Message.ScheduledEnqueueTime);
        Assert.True(call.Message.Properties!.TryGetValue("Amount", out var amount));
        Assert.Equal(12.5, amount);
    }

    [Fact]
    public async Task SendAsync_BackendFailure_ReturnsSecretSafeOutcome_AndPreservesDraft()
    {
        var secret = "Endpoint=sb://x.servicebus.windows.net/;SharedAccessKeyName=Root;SharedAccessKey=SuperSecretValue==";
        var queue = new RecordingQueueService
        {
            SendFailure = new ServiceBusException(secret, ServiceBusFailureReason.GeneralError)
        };
        var service = CreateSendService(queue);
        var target = new SendTargetContext(SendTargetKind.Topic, "sales", "sales");
        var draft = CreateValidDraft();
        draft.SetBodyText("sensitive-body-content");
        draft.CustomProperties.Add(new TypedMessageProperty("Token", MessagePropertyType.String, "prop-secret"));

        var result = await service.SendAsync(target, draft);

        Assert.Equal(MessageSendStatus.Failed, result.Status);
        Assert.Equal(ConnectionFailureCategory.Unknown, result.FailureCategory);
        Assert.DoesNotContain("SuperSecretValue", result.SafeMessage, StringComparison.Ordinal);
        Assert.Contains("[redacted]", result.SafeMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-body-content", result.SafeMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("prop-secret", result.SafeMessage, StringComparison.Ordinal);
        Assert.Equal("sensitive-body-content", draft.GetBodyText());
        Assert.Single(draft.CustomProperties);
    }

    [Fact]
    public async Task SendAsync_DoesNotLogBodyOrProperties()
    {
        var queue = new RecordingQueueService();
        var logger = new CapturingLogger();
        var service = new MessageSendService(queue, logger);
        var draft = CreateValidDraft();
        draft.SetBodyText("must-not-appear-in-logs");
        draft.CustomProperties.Add(new TypedMessageProperty("SecretProp", MessagePropertyType.String, "must-not-log"));

        await service.SendAsync(
            new SendTargetContext(SendTargetKind.Queue, "orders", "orders"),
            draft);

        Assert.NotEmpty(logger.Messages);
        Assert.All(
            logger.Messages,
            message =>
            {
                Assert.DoesNotContain("must-not-appear-in-logs", message, StringComparison.Ordinal);
                Assert.DoesNotContain("must-not-log", message, StringComparison.Ordinal);
                Assert.DoesNotContain("SecretProp", message, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task SendAsync_ScheduleDelayOutsideComposerRange_IsValidationFailure()
    {
        var queue = new RecordingQueueService();
        var service = CreateSendService(queue);
        var draft = CreateValidDraft();
        draft.ScheduleDelay = new DurationValue(30_000);

        var result = await service.SendAsync(
            new SendTargetContext(SendTargetKind.Queue, "orders", "orders"),
            draft);

        Assert.Equal(MessageSendStatus.ValidationFailed, result.Status);
        Assert.Equal(MessageDraft.ErrorInvalidScheduleDelay, result.ValidationErrorCode);
        Assert.Empty(queue.SendCalls);
        Assert.Equal("{\"ok\":true}", draft.GetBodyText());
    }

    private static MessageDraft CreateValidDraft()
    {
        var draft = new MessageDraft { DestinationPath = "orders", ContentType = "application/json" };
        draft.SetBodyText("{\"ok\":true}", MessageBodyKind.Json);
        return draft;
    }

    private static MessageSendService CreateSendService(IQueueService queue) =>
        new(queue, NullLogger<MessageSendService>.Instance);

    private sealed class RecordingQueueService : IQueueService
    {
        public List<(string EntityPath, OutboundMessage Message)> SendCalls { get; } = [];
        public Exception? SendFailure { get; init; }

        public Task<IReadOnlyList<QueueInfo>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<QueueInfo>>([]);

        public Task<QueueInfo> GetAsync(string name, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<QueueInfo> CreateAsync(CreateQueueOptions opts, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<QueueInfo> UpdateAsync(QueueInfo updated, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string name, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ReceivedMessage>> PeekAsync(
            string name,
            int maxCount,
            MessageSource source,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SendAsync(string name, OutboundMessage message, CancellationToken ct = default)
        {
            if (SendFailure is not null)
                return Task.FromException(SendFailure);

            SendCalls.Add((name, message));
            return Task.CompletedTask;
        }

        public Task PurgeAsync(string name, MessageSource source, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReceiveSession> OpenReceiveSessionAsync(
            string name,
            MessageSource source,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class CapturingLogger : ILogger<MessageSendService>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
