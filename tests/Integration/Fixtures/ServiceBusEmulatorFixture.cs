using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Xunit;

namespace ServiceBusExplorer.IntegrationTests.Fixtures;

/// <summary>
/// Shared emulator connection strings and client factory. Assumes Compose is up and healthy.
/// </summary>
public sealed class ServiceBusEmulatorFixture : IAsyncLifetime
{
    public const string MessagingConnectionString =
        "Endpoint=sb://localhost;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    public const string AdministrationConnectionString =
        "Endpoint=sb://localhost:5300;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

    public const string HealthUrl = "http://localhost:5300/health";

    public static readonly TimeSpan HealthPollInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan HealthTimeout = TimeSpan.FromMinutes(3);

    public ServiceBusClient CreateMessagingClient() =>
        new(MessagingConnectionString);

    public ServiceBusAdministrationClient CreateAdministrationClient() =>
        new(AdministrationConnectionString);

    public async Task WaitForHealthyAsync(CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTimeOffset.UtcNow.Add(HealthTimeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var response = await http.GetAsync(HealthUrl, ct);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
            }

            await Task.Delay(HealthPollInterval, ct);
        }

        throw new InvalidOperationException(
            $"Service Bus emulator health endpoint did not respond at {HealthUrl} within {HealthTimeout.TotalSeconds:F0}s.");
    }

    public async ValueTask InitializeAsync()
    {
        if (!IntegrationTestGate.IsEnabled)
        {
            return;
        }

        await WaitForHealthyAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
