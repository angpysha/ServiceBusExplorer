using System.Reactive.Threading.Tasks;
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
        var viewModel = new SendMessageViewModel(service, target)
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
        var viewModel = new SendMessageViewModel(service, target)
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
            service,
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
