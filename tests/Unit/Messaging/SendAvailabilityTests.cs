using System.Reactive.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceBusExplorer.Services;
using ServiceBusExplorer.ViewModels;
using Xunit;

namespace ServiceBusExplorer.UnitTests.Messaging;

public class SendAvailabilityTests
{
    [Theory]
    [InlineData(SendTargetKind.Queue, "orders", "orders")]
    [InlineData(SendTargetKind.Topic, "sales", "sales")]
    [InlineData(
        SendTargetKind.Subscription,
        "sales/Subscriptions/regional",
        "sales")]
    public async Task Send_UsesActualDestinationForEveryRequestedContext(
        SendTargetKind kind,
        string requestedPath,
        string actualDestination)
    {
        var service = new RecordingQueueService();
        var target = new SendTargetContext(kind, requestedPath, actualDestination);
        var viewModel = new SendMessageViewModel(CreateSendService(service), target)
        {
            Body = "draft"
        };

        await viewModel.SendCommand.Execute().ToTask();

        var call = Assert.Single(service.SendCalls);
        Assert.Equal(actualDestination, call.EntityPath);
        Assert.Contains(actualDestination, viewModel.Outcome);
        if (kind == SendTargetKind.Subscription)
        {
            Assert.Contains("parent topic", viewModel.DestinationDescription);
            Assert.Contains("parent topic", viewModel.Outcome);
        }
    }

    [Fact]
    public async Task Send_WhenBackendFails_PreservesDraftAndNamesActualDestination()
    {
        var service = new RecordingQueueService
        {
            SendFailure = new InvalidOperationException("unavailable")
        };
        var target = new SendTargetContext(
            SendTargetKind.Subscription,
            "sales/Subscriptions/regional",
            "sales");
        var viewModel = new SendMessageViewModel(CreateSendService(service), target)
        {
            Body = "keep this draft"
        };

        await viewModel.SendCommand.Execute().ToTask();

        Assert.Equal("keep this draft", viewModel.Body);
        Assert.Contains("sales", viewModel.Error);
        Assert.Contains("parent topic", viewModel.Error);
    }

    [Fact]
    public async Task Send_WhenValidationFails_PreservesDraft()
    {
        var service = new RecordingQueueService();
        var viewModel = new SendMessageViewModel(
            CreateSendService(service),
            new SendTargetContext(SendTargetKind.Queue, "orders", "orders"))
        {
            Body = "keep this draft",
            PropertiesJson = "{invalid"
        };

        await viewModel.SendCommand.Execute().ToTask();

        Assert.Equal("keep this draft", viewModel.Body);
        Assert.Empty(service.SendCalls);
        Assert.Contains("orders", viewModel.Error);
    }

    [Fact]
    public async Task Send_WhenBodyIsEmpty_BlocksSubmissionAndPreservesDraft()
    {
        var service = new RecordingQueueService();
        var viewModel = new SendMessageViewModel(
            CreateSendService(service),
            new SendTargetContext(SendTargetKind.Queue, "orders", "orders"))
        {
            Body = "   ",
            ContentType = "text/plain",
            MessageId = "draft-id"
        };

        await viewModel.SendCommand.Execute().ToTask();

        Assert.Empty(service.SendCalls);
        Assert.Equal("   ", viewModel.Body);
        Assert.Equal("text/plain", viewModel.ContentType);
        Assert.Equal("draft-id", viewModel.MessageId);
        Assert.Contains("Body is required", viewModel.Error);
    }

    [Fact]
    public async Task Send_WithRelativeSchedule_UsesWholeDurationValue()
    {
        var service = new RecordingQueueService();
        var viewModel = new SendMessageViewModel(
            CreateSendService(service),
            new SendTargetContext(SendTargetKind.Queue, "orders", "orders"))
        {
            Body = "scheduled draft",
            UseScheduledTime = true,
            ScheduleDelay = TimeSpan.FromSeconds(90)
        };
        var earliest = DateTimeOffset.Now.AddSeconds(89);

        await viewModel.SendCommand.Execute().ToTask();

        var scheduled = Assert.Single(service.SendCalls).Message.ScheduledEnqueueTime;
        Assert.NotNull(scheduled);
        Assert.InRange(scheduled.Value, earliest, DateTimeOffset.Now.AddSeconds(91));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public async Task Send_WhenMessageCountIsOutsideComposerRange_BlocksSubmission(int count)
    {
        var service = new RecordingQueueService();
        var viewModel = new SendMessageViewModel(
            CreateSendService(service),
            new SendTargetContext(SendTargetKind.Queue, "orders", "orders"))
        {
            Body = "draft",
            SendCount = count
        };

        await viewModel.SendCommand.Execute().ToTask();

        Assert.Empty(service.SendCalls);
        Assert.Equal("draft", viewModel.Body);
        Assert.Contains("Message count must be between 1 and 1000", viewModel.Error);
    }

    [Theory]
    [InlineData(59999)]
    [InlineData(604800001)]
    public async Task Send_WhenScheduleDelayIsOutsideComposerRange_BlocksSubmission(long milliseconds)
    {
        var service = new RecordingQueueService();
        var viewModel = new SendMessageViewModel(
            CreateSendService(service),
            new SendTargetContext(SendTargetKind.Queue, "orders", "orders"))
        {
            Body = "scheduled draft",
            UseScheduledTime = true,
            ScheduleDelay = TimeSpan.FromMilliseconds(milliseconds)
        };

        await viewModel.SendCommand.Execute().ToTask();

        Assert.Empty(service.SendCalls);
        Assert.Equal("scheduled draft", viewModel.Body);
        Assert.Contains("Schedule delay", viewModel.Error);
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

        public Task<EntityLifecycleResult<QueueInfo>> CreateAsync(CreateQueueOptions opts, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<EntityLifecycleResult<QueueInfo>> UpdateAsync(QueueInfo updated, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<EntityLifecycleResult<QueueInfo?>> DeleteAsync(string name, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ReceivedMessage>> PeekAsync(
            string name,
            int maxCount,
            MessageSource source,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SendAsync(
            string name,
            OutboundMessage message,
            CancellationToken ct = default)
        {
            if (SendFailure is not null)
                return Task.FromException(SendFailure);

            SendCalls.Add((name, message));
            return Task.CompletedTask;
        }

        public Task PurgeAsync(
            string name,
            MessageSource source,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReceiveSession> OpenReceiveSessionAsync(
            string name,
            MessageSource source,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
