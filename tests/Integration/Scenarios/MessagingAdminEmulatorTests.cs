using Microsoft.Extensions.Logging.Abstractions;
using ServiceBusExplorer.IntegrationTests.Fixtures;
using ServiceBusExplorer.Services;
using Xunit;

namespace ServiceBusExplorer.IntegrationTests.Scenarios;

/// <summary>
/// T032 focus slice: list queues/topics/subscriptions via admin adapters and send + peek-browse
/// via app messaging services against the Compose emulator (admin :5300, AMQP :5672).
/// </summary>
public sealed class MessagingAdminEmulatorTests(ServiceBusEmulatorFixture fixture)
    : IClassFixture<ServiceBusEmulatorFixture>, IAsyncLifetime
{
    private const string ActiveQueue = "mvp.queue.active";
    private const string TopicName = "mvp.topic";
    private const string SubscriptionName = "mvp.subscription";

    private ServiceBusClientFactory? _clients;

    public async ValueTask InitializeAsync()
    {
        if (!IntegrationTestGate.IsEnabled)
        {
            return;
        }

        await fixture.WaitForHealthyAsync();
        _clients = new ServiceBusClientFactory(fixture);
    }

    public async ValueTask DisposeAsync()
    {
        if (_clients is not null)
        {
            await _clients.DisposeAsync();
        }
    }

    [IntegrationFact]
    public async Task AdminAdapters_list_predeclared_queues_topics_and_subscriptions()
    {
        var clients = RequireClients();
        var queueService = new QueueService(
            clients.AdminAdapter,
            clients.MessagingClient,
            NullLogger<QueueService>.Instance);
        var topicService = new TopicService(
            clients.AdminAdapter,
            NullLogger<TopicService>.Instance);
        var subscriptionService = new SubscriptionService(
            clients.SubscriptionAdapter,
            NullLogger<SubscriptionService>.Instance);

        var queues = await queueService.ListAsync();
        Assert.Contains(queues, q => q.Name == ActiveQueue);
        Assert.Contains(queues, q => q.Name == "mvp.queue.sessions");

        var topics = await topicService.ListAsync();
        Assert.Contains(topics, t => t.Name == TopicName);

        var subscriptions = await subscriptionService.ListAsync(TopicName);
        Assert.Contains(subscriptions, s => s.Name == SubscriptionName);
    }

    [IntegrationFact]
    public async Task SendService_posts_to_queue_and_BrowseService_peeks_body()
    {
        var clients = RequireClients();
        var send = new MessageSendService(
            new QueueService(
                clients.AdminAdapter,
                clients.MessagingClient,
                NullLogger<QueueService>.Instance),
            NullLogger<MessageSendService>.Instance);
        var browse = new MessageBrowseService(
            new ServiceBusPeekAdapter(clients.MessagingClient),
            NullLogger<MessageBrowseService>.Instance);

        var body = $"emulator-send-{Guid.NewGuid():N}";
        var draft = new MessageDraft
        {
            DestinationPath = ActiveQueue,
            Subject = "integration-emulator",
            ContentType = "text/plain"
        };
        draft.SetBodyText(body);

        var sendResult = await send.SendAsync(
            new SendTargetContext(SendTargetKind.Queue, ActiveQueue, ActiveQueue),
            draft);
        Assert.Equal(MessageSendStatus.Succeeded, sendResult.Status);

        // Peek is eventual; allow a short retry window against the emulator.
        MessageBrowseResult? browseResult = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            browseResult = await browse.PeekAsync(
                new EntityAddress(ActiveQueue),
                MessageSource.Active,
                new PageRequest(MaxCount: 50));
            if (browseResult.Messages.Any(m => m.Body.DisplayText == body))
            {
                break;
            }

            await Task.Delay(500);
        }

        Assert.NotNull(browseResult);
        Assert.Equal(SourceAvailability.Available, browseResult.Availability);
        Assert.Contains(browseResult.Messages, m => m.Body.DisplayText == body);
    }

    [IntegrationFact]
    public async Task SendService_publishes_to_topic_and_subscription_can_peek()
    {
        var clients = RequireClients();
        var send = new MessageSendService(
            new QueueService(
                clients.AdminAdapter,
                clients.MessagingClient,
                NullLogger<QueueService>.Instance),
            NullLogger<MessageSendService>.Instance);
        var browse = new MessageBrowseService(
            new ServiceBusPeekAdapter(clients.MessagingClient),
            NullLogger<MessageBrowseService>.Instance);

        var body = $"emulator-topic-{Guid.NewGuid():N}";
        var draft = new MessageDraft { DestinationPath = TopicName };
        draft.SetBodyText(body);

        var sendResult = await send.SendAsync(
            new SendTargetContext(SendTargetKind.Topic, TopicName, TopicName),
            draft);
        Assert.Equal(MessageSendStatus.Succeeded, sendResult.Status);

        var subscriptionPath = $"{TopicName}/Subscriptions/{SubscriptionName}";
        MessageBrowseResult? browseResult = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            browseResult = await browse.PeekAsync(
                new EntityAddress(subscriptionPath),
                MessageSource.Active,
                new PageRequest(MaxCount: 50));
            if (browseResult.Messages.Any(m => m.Body.DisplayText == body))
            {
                break;
            }

            await Task.Delay(500);
        }

        Assert.NotNull(browseResult);
        Assert.Contains(browseResult.Messages, m => m.Body.DisplayText == body);
    }

    private ServiceBusClientFactory RequireClients() =>
        _clients ?? throw new InvalidOperationException("Emulator clients were not initialized.");

    /// <summary>Shared messaging + admin clients for one test class run.</summary>
    private sealed class ServiceBusClientFactory : IAsyncDisposable
    {
        public Azure.Messaging.ServiceBus.ServiceBusClient MessagingClient { get; }
        public ServiceBusAdminAdapter AdminAdapter { get; }
        public ServiceBusSubscriptionAdministrationAdapter SubscriptionAdapter { get; }

        public ServiceBusClientFactory(ServiceBusEmulatorFixture fixture)
        {
            MessagingClient = fixture.CreateMessagingClient();
            var admin = fixture.CreateAdministrationClient();
            AdminAdapter = new ServiceBusAdminAdapter(admin);
            SubscriptionAdapter = new ServiceBusSubscriptionAdministrationAdapter(
                admin,
                NullLogger<ServiceBusSubscriptionAdministrationAdapter>.Instance);
        }

        public async ValueTask DisposeAsync() => await MessagingClient.DisposeAsync();
    }
}
