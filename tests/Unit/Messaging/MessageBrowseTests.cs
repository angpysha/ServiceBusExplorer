using System.Reactive.Threading.Tasks;
using System.Text;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceBusExplorer.Services;
using ServiceBusExplorer.ViewModels;
using Xunit;

namespace ServiceBusExplorer.UnitTests.Messaging;

public class MessageBrowseTests
{
    [Fact]
    public async Task PeekAsync_RequiresExplicitSource_OnApi()
    {
        var adapter = new RecordingPeekAdapter();
        var service = CreateService(adapter);

        await service.PeekAsync(
            new EntityAddress("orders"),
            MessageSource.Active,
            new PageRequest(10));

        Assert.Equal(SubQueue.None, adapter.LastSubQueue);
    }

    [Fact]
    public async Task PeekAsync_EmptySource_ReturnsEmptyAvailability()
    {
        var adapter = new RecordingPeekAdapter();
        var service = CreateService(adapter);

        var result = await service.PeekAsync(
            new EntityAddress("orders"),
            MessageSource.DeadLetter,
            new PageRequest(10));

        Assert.Empty(result.Messages);
        Assert.Equal(SourceAvailability.Empty, result.Availability);
        Assert.Null(result.Continuation);
    }

    [Fact]
    public async Task PeekAsync_UnavailableSource_ReturnsUnavailableWithoutThrowing()
    {
        var adapter = new RecordingPeekAdapter
        {
            Exception = new ServiceBusException("not found", ServiceBusFailureReason.MessagingEntityNotFound)
        };
        var service = CreateService(adapter);

        var result = await service.PeekAsync(
            new EntityAddress("orders"),
            MessageSource.TransferDeadLetter,
            new PageRequest(10));

        Assert.Empty(result.Messages);
        Assert.Equal(SourceAvailability.Unavailable, result.Availability);
    }

    [Fact]
    public async Task PeekAsync_PreservesSourceTagOnEachMessage()
    {
        var adapter = new RecordingPeekAdapter
        {
            Messages =
            [
                CreateMessage("a", 1),
                CreateMessage("b", 2)
            ]
        };
        var service = CreateService(adapter);

        var result = await service.PeekAsync(
            new EntityAddress("orders"),
            MessageSource.DeadLetter,
            new PageRequest(10));

        Assert.All(result.Messages, message => Assert.Equal(MessageSource.DeadLetter, message.Source));
    }

    [Fact]
    public async Task PeekAsync_BinaryBody_MapsToBinaryRepresentation()
    {
        var adapter = new RecordingPeekAdapter
        {
            Messages = [CreateMessage("bin", 1, body: new byte[] { 0x00, 0x01, 0xFF })]
        };
        var service = CreateService(adapter);

        var message = Assert.Single((await service.PeekAsync(
            new EntityAddress("orders"),
            MessageSource.Active,
            new PageRequest(10))).Messages);

        Assert.Equal(MessageBodyKind.Binary, message.Body.Kind);
        Assert.Contains("Binary content", message.Body.DisplayText);
    }

    [Fact]
    public async Task PeekAsync_LargeTextBody_MapsToTruncatedWithFullLength()
    {
        var large = new string('x', 10_000);
        var adapter = new RecordingPeekAdapter
        {
            Messages = [CreateMessage("big", 1, body: Encoding.UTF8.GetBytes(large), contentType: "text/plain")]
        };
        var service = CreateService(adapter);

        var message = Assert.Single((await service.PeekAsync(
            new EntityAddress("orders"),
            MessageSource.Active,
            new PageRequest(10))).Messages);

        Assert.Equal(MessageBodyKind.Truncated, message.Body.Kind);
        Assert.True(message.Body.FullLengthBytes > 8_192);
        Assert.True(message.Body.DisplayText!.Length < large.Length);
    }

    [Fact]
    public async Task PeekAsync_JsonContentType_MapsToJsonRepresentation()
    {
        var adapter = new RecordingPeekAdapter
        {
            Messages = [CreateMessage("json", 1, body: "{\"a\":1}"u8.ToArray(), contentType: "application/json")]
        };
        var service = CreateService(adapter);

        var message = Assert.Single((await service.PeekAsync(
            new EntityAddress("orders"),
            MessageSource.Active,
            new PageRequest(10))).Messages);

        Assert.Equal(MessageBodyKind.Json, message.Body.Kind);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(500, 100)]
    [InlineData(25, 25)]
    public async Task PeekAsync_BoundsPageRequestMaxCount(int requested, int expected)
    {
        var adapter = new RecordingPeekAdapter();
        var service = CreateService(adapter);

        await service.PeekAsync(
            new EntityAddress("orders"),
            MessageSource.Active,
            new PageRequest(requested));

        Assert.Equal(expected, adapter.LastMaxCount);
    }

    [Fact]
    public async Task PeekAsync_PassesContinuationFromSequenceNumber()
    {
        var adapter = new RecordingPeekAdapter();
        var service = CreateService(adapter);

        await service.PeekAsync(
            new EntityAddress("orders"),
            MessageSource.Active,
            new PageRequest(10, FromSequenceNumber: 42));

        Assert.Equal(42, adapter.LastFromSequenceNumber);
    }

    [Fact]
    public async Task PeekAsync_FullPage_ReturnsContinuationFromLastSequencePlusOne()
    {
        var adapter = new RecordingPeekAdapter
        {
            Messages =
            [
                CreateMessage("a", 10),
                CreateMessage("b", 11)
            ]
        };
        var service = CreateService(adapter);

        var result = await service.PeekAsync(
            new EntityAddress("orders"),
            MessageSource.Active,
            new PageRequest(2));

        Assert.NotNull(result.Continuation);
        Assert.Equal(12, result.Continuation!.FromSequenceNumber);
    }

    [Fact]
    public async Task Peek_WithNoSelectedSource_DoesNotCallBrowseService()
    {
        var browse = new RecordingBrowseService();
        var viewModel = new QueueDetailViewModel(
            new StubQueueService(),
            browse,
            new NoOpConfirmationService(),
            "orders");

        await viewModel.PeekCommand.Execute().ToTask();

        Assert.Empty(browse.Calls);
    }

    [Fact]
    public async Task Peek_WhenConfirmed_UsesBrowseServiceAndObservedMessages()
    {
        var browse = new RecordingBrowseService
        {
            Result = new MessageBrowseResult(
                [CreateObserved("m1", MessageSource.Active, 1)],
                null,
                SourceAvailability.Available)
        };
        var viewModel = new QueueDetailViewModel(
            new StubQueueService(),
            browse,
            new NoOpConfirmationService(),
            "orders")
        {
            SelectedSource = MessageSource.Active,
            PeekCount = 5
        };

        await viewModel.PeekCommand.Execute().ToTask();

        var call = Assert.Single(browse.Calls);
        Assert.Equal("orders", call.Address.Path);
        Assert.Equal(MessageSource.Active, call.Source);
        Assert.Equal(5, call.Page.MaxCount);
        Assert.Single(viewModel.ObservedMessages);
    }

    [Fact]
    public async Task CopyObservedBody_WhenCancelled_DoesNotCopy()
    {
        var copied = new List<string>();
        var viewModel = await CreateViewModelWithObservedMessageAsync(
            new NoOpConfirmationService(ConfirmationResult.Cancelled),
            text => copied.Add(text));

        await viewModel.CopyObservedBodyCommand.Execute().ToTask();

        Assert.Empty(copied);
        Assert.False(viewModel.UserAcceptedSensitiveCopy);
    }

    [Fact]
    public async Task CopyObservedBody_WhenConfirmed_CopiesDisplayText()
    {
        var copied = new List<string>();
        var viewModel = await CreateViewModelWithObservedMessageAsync(
            new NoOpConfirmationService(ConfirmationResult.Confirmed),
            text => copied.Add(text));

        await viewModel.CopyObservedBodyCommand.Execute().ToTask();

        Assert.Equal("hello", Assert.Single(copied));
        Assert.True(viewModel.UserAcceptedSensitiveCopy);
    }

    [Fact]
    public async Task SensitiveContentCopy_RequiresExplicitConfirmation()
    {
        var confirmation = new RecordingConfirmationService(ConfirmationResult.Confirmed);

        var accepted = await SensitiveContentCopy.ConfirmAsync(
            confirmation,
            "orders",
            MessageSource.Active);

        Assert.True(accepted);
        var request = Assert.Single(confirmation.Requests);
        Assert.Contains("sensitive", request.Consequence, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<QueueDetailViewModel> CreateViewModelWithObservedMessageAsync(
        IConfirmationService confirmation,
        Action<string> onCopy)
    {
        var browse = new RecordingBrowseService
        {
            Result = new MessageBrowseResult(
                [CreateObserved("m1", MessageSource.Active, 1, "hello")],
                null,
                SourceAvailability.Available)
        };
        var viewModel = new QueueDetailViewModel(
            new StubQueueService(),
            browse,
            confirmation,
            "orders",
            text => { onCopy(text); return Task.CompletedTask; })
        {
            SelectedSource = MessageSource.Active
        };

        await viewModel.PeekCommand.Execute().ToTask();
        viewModel.SelectedObservedMessage = viewModel.ObservedMessages.FirstOrDefault();
        return viewModel;
    }

    private static MessageBrowseService CreateService(RecordingPeekAdapter adapter) =>
        new(adapter, NullLogger<MessageBrowseService>.Instance);

    private static ServiceBusReceivedMessage CreateMessage(
        string messageId,
        long sequenceNumber,
        byte[]? body = null,
        string contentType = "text/plain")
    {
        body ??= Encoding.UTF8.GetBytes("sample");
        return ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData(body),
            messageId: messageId,
            sequenceNumber: sequenceNumber,
            contentType: contentType);
    }

    private static ObservedMessage CreateObserved(
        string messageId,
        MessageSource source,
        long sequenceNumber,
        string body = "sample") =>
        new(
            messageId,
            source,
            MessageReceiveKind.Peeked,
            sequenceNumber,
            0,
            DateTimeOffset.UtcNow,
            null,
            new MessageBodyRepresentation(MessageBodyKind.Text, body),
            new Dictionary<string, object>(),
            null);

    private sealed class RecordingPeekAdapter : IServiceBusPeekAdapter
    {
        public IReadOnlyList<ServiceBusReceivedMessage> Messages { get; init; } = [];
        public Exception? Exception { get; init; }
        public int LastMaxCount { get; private set; }
        public long? LastFromSequenceNumber { get; private set; }
        public SubQueue LastSubQueue { get; private set; }

        public Task<IReadOnlyList<ServiceBusReceivedMessage>> PeekMessagesAsync(
            string entityPath,
            SubQueue subQueue,
            int maxCount,
            long? fromSequenceNumber,
            CancellationToken cancellationToken)
        {
            LastSubQueue = subQueue;
            LastMaxCount = maxCount;
            LastFromSequenceNumber = fromSequenceNumber;

            if (Exception is not null)
                throw Exception;

            return Task.FromResult(Messages);
        }
    }

    private sealed class RecordingBrowseService : IMessageBrowseService
    {
        public MessageBrowseResult Result { get; init; } =
            new([], null, SourceAvailability.Empty);

        public List<(EntityAddress Address, MessageSource Source, PageRequest Page)> Calls { get; } = [];

        public Task<MessageBrowseResult> PeekAsync(
            EntityAddress address,
            MessageSource source,
            PageRequest page,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((address, source, page));
            return Task.FromResult(Result);
        }
    }

    private sealed class RecordingConfirmationService(ConfirmationResult result) : IConfirmationService
    {
        public List<ConfirmationRequest> Requests { get; } = [];

        public Task<ConfirmationResult> ConfirmAsync(
            ConfirmationRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(result);
        }
    }

    private sealed class NoOpConfirmationService(ConfirmationResult result = ConfirmationResult.Cancelled)
        : IConfirmationService
    {
        public Task<ConfirmationResult> ConfirmAsync(
            ConfirmationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class StubQueueService : IQueueService
    {
        public Task<IReadOnlyList<QueueInfo>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<QueueInfo>>([]);

        public Task<QueueInfo> GetAsync(string name, CancellationToken ct = default) =>
            Task.FromResult(CreateQueue(name));

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
            Task.FromResult<IReadOnlyList<ReceivedMessage>>([]);

        public Task SendAsync(string name, OutboundMessage message, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task PurgeAsync(string name, MessageSource source, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReceiveSession> OpenReceiveSessionAsync(
            string name,
            MessageSource source,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        private static QueueInfo CreateQueue(string name) =>
            new(
                name,
                ActiveMessageCount: 0,
                DeadLetterCount: 0,
                ScheduledMessageCount: 0,
                LockDuration: TimeSpan.FromMinutes(1),
                RequiresDuplicateDetection: false,
                RequiresSession: false,
                DefaultMessageTimeToLive: TimeSpan.FromDays(14),
                Status: EntityStatus.Active,
                AutoDeleteOnIdle: TimeSpan.MaxValue,
                MaxDeliveryCount: 10,
                MaxSizeInMegabytes: 1024,
                EnableBatchedOperations: true,
                ForwardTo: null,
                ForwardDeadLetteredMessagesTo: null,
                UserMetadata: null,
                DuplicateDetectionHistoryTimeWindow: TimeSpan.FromMinutes(10),
                SizeInBytes: 0,
                EnableDeadLetteringOnMessageExpiration: false);
    }
}
