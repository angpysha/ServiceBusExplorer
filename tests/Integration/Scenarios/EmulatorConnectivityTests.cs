using ServiceBusExplorer.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceBusExplorer.IntegrationTests.Scenarios;

/// <summary>
/// Smoke tests proving the Compose emulator is reachable before T032 scenario coverage.
/// </summary>
public sealed class EmulatorConnectivityTests(ServiceBusEmulatorFixture fixture)
    : IClassFixture<ServiceBusEmulatorFixture>
{
    [IntegrationFact]
    public async Task AdministrationClient_lists_predeclared_mvp_entities()
    {
        var admin = fixture.CreateAdministrationClient();
        var queues = new List<string>();
        await foreach (var q in admin.GetQueuesAsync())
        {
            queues.Add(q.Name);
        }

        Assert.Contains("mvp.queue.active", queues);
        Assert.Contains("mvp.queue.sessions", queues);

        var topics = new List<string>();
        await foreach (var t in admin.GetTopicsAsync())
        {
            topics.Add(t.Name);
        }

        Assert.Contains("mvp.topic", topics);
    }

    [IntegrationFact]
    public async Task MessagingClient_can_send_and_receive_on_active_queue()
    {
        await using var client = fixture.CreateMessagingClient();
        await using var sender = client.CreateSender("mvp.queue.active");
        await using var receiver = client.CreateReceiver("mvp.queue.active");

        var body = $"integration-smoke-{Guid.NewGuid():N}";
        await sender.SendMessageAsync(new Azure.Messaging.ServiceBus.ServiceBusMessage(body));

        // Drain leftover messages from prior runs; match on the body we just sent.
        Azure.Messaging.ServiceBus.ServiceBusReceivedMessage? received = null;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var next = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5));
            if (next is null)
            {
                continue;
            }

            if (next.Body.ToString() == body)
            {
                received = next;
                break;
            }

            await receiver.CompleteMessageAsync(next);
        }

        Assert.NotNull(received);
        Assert.Equal(body, received.Body.ToString());
        await receiver.CompleteMessageAsync(received);
    }
}
