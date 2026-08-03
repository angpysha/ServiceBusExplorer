#nullable enable
using Azure.Core;
using ServiceBusExplorer.Services;
using Xunit;

namespace ServiceBusExplorer.ContractTests.Connections;

public sealed class ConnectionContextFactoryTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_MissingEndpoint_ReturnsValidationFailure(string endpoint)
    {
        var factory = CreateFactory(new FakeCredentialVault(), new FakeConnectionProbe());

        var result = await factory.CreateAsync(CreateSasRequest(endpoint: endpoint));

        Assert.False(result.Success);
        Assert.Equal(ConnectionFailureCategory.Validation, result.FailureCategory);
    }

    [Fact]
    public async Task CreateAsync_EntityScopeWithoutPath_ReturnsValidationFailure()
    {
        var factory = CreateFactory(new FakeCredentialVault(), new FakeConnectionProbe());

        var result = await factory.CreateAsync(
            CreateSasRequest(scope: ConnectionScope.Entity, entityPath: null));

        Assert.False(result.Success);
        Assert.Equal(ConnectionFailureCategory.Validation, result.FailureCategory);
        Assert.Contains("entity path", result.FailureMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_NamespaceScopeWithEntityPath_ReturnsValidationFailure()
    {
        var factory = CreateFactory(new FakeCredentialVault(), new FakeConnectionProbe());

        var result = await factory.CreateAsync(
            CreateSasRequest(entityPath: "orders"));

        Assert.False(result.Success);
        Assert.Equal(ConnectionFailureCategory.Validation, result.FailureCategory);
    }

    [Fact]
    public async Task CreateAsync_SasWithoutCredentialOrReference_ReturnsValidationFailure()
    {
        var factory = CreateFactory(new FakeCredentialVault(), new FakeConnectionProbe());

        var result = await factory.CreateAsync(
            CreateSasRequest(includeCredential: false));

        Assert.False(result.Success);
        Assert.Equal(ConnectionFailureCategory.Validation, result.FailureCategory);
    }

    [Fact]
    public async Task CreateAsync_SasNamespaceScope_SucceedsWithFakeProbe()
    {
        var probe = new FakeConnectionProbe { AdminProbeResult = true };
        var factory = CreateFactory(new FakeCredentialVault(), probe);

        using var credential = new SensitiveCredential(CreateConnectionString());
        var result = await factory.CreateAsync(
            CreateSasRequest(credential: credential));

        Assert.True(result.Success);
        Assert.NotNull(result.Context);
        Assert.Equal(ConnectionScope.Namespace, result.Context!.Scope);
        Assert.True(result.Context.Capabilities.CanAdministerNamespace);
        Assert.True(result.Context.Capabilities.CanBrowseEntities);
        Assert.Equal(1, probe.AdminProbeCount);
    }

    [Fact]
    public async Task CreateAsync_EntityScope_OmitsAdminCapability()
    {
        var probe = new FakeConnectionProbe();
        var factory = CreateFactory(new FakeCredentialVault(), probe);

        using var credential = new SensitiveCredential(CreateConnectionString());
        var result = await factory.CreateAsync(
            CreateSasRequest(
                scope: ConnectionScope.Entity,
                entityPath: "orders",
                credential: credential));

        Assert.True(result.Success);
        Assert.NotNull(result.Context);
        Assert.Equal(ConnectionScope.Entity, result.Context!.Scope);
        Assert.Equal("orders", result.Context.EntityPath);
        Assert.False(result.Context.Capabilities.CanAdministerNamespace);
        Assert.False(result.Context.Capabilities.CanBrowseEntities);
        Assert.Equal(0, probe.AdminProbeCount);
    }

    [Fact]
    public async Task CreateAsync_VaultRetrieveSuccess_ConnectsWithoutManualSas()
    {
        var vault = new FakeCredentialVault();
        var reference = CredentialReference.CreateNew();
        await vault.StoreAsync(reference, new SensitiveCredential(CreateConnectionString()));
        var factory = CreateFactory(vault, new FakeConnectionProbe { AdminProbeResult = true });

        var result = await factory.CreateAsync(
            CreateSasRequest(includeCredential: false, credentialReference: reference));

        Assert.True(result.Success);
        Assert.False(result.RequiresManualSas);
        Assert.Equal(1, vault.RetrieveCount);
    }

    [Fact]
    public async Task CreateAsync_VaultRetrieveFailure_RequiresManualSasAndPreservesReference()
    {
        var vault = new FakeCredentialVault();
        var reference = CredentialReference.CreateNew();
        var factory = CreateFactory(vault, new FakeConnectionProbe());

        var result = await factory.CreateAsync(
            CreateSasRequest(includeCredential: false, credentialReference: reference));

        Assert.False(result.Success);
        Assert.True(result.RequiresManualSas);
        Assert.Equal(reference, result.PreservedCredentialReference);
        Assert.Equal(1, vault.RetrieveCount);
        Assert.Equal(0, vault.DeleteCount);
    }

    [Fact]
    public async Task CreateAsync_Entra_DoesNotInvokeVault()
    {
        var vault = new FakeCredentialVault();
        var factory = CreateFactory(
            vault,
            new FakeConnectionProbe { AdminProbeResult = true },
            _ => new FakeTokenCredential());

        var result = await factory.CreateAsync(
            new ConnectionRequest
            {
                NamespaceEndpoint = "example.servicebus.windows.net",
                AuthMode = ServiceBusAuthMode.AzureActiveDirectory,
                Scope = ConnectionScope.Namespace,
                EntraInteraction = EntraInteractionMode.Default,
            });

        Assert.True(result.Success);
        Assert.Equal(0, vault.RetrieveCount);
        Assert.Equal(0, vault.StoreCount);
    }

    [Fact]
    public async Task CreateAsync_EntraWithCredentialReference_ReturnsValidationFailureWithoutVaultCall()
    {
        var vault = new FakeCredentialVault();
        var factory = CreateFactory(vault, new FakeConnectionProbe(), _ => new FakeTokenCredential());

        var result = await factory.CreateAsync(
            new ConnectionRequest
            {
                NamespaceEndpoint = "example.servicebus.windows.net",
                AuthMode = ServiceBusAuthMode.AzureActiveDirectory,
                Scope = ConnectionScope.Namespace,
                EntraInteraction = EntraInteractionMode.Default,
                CredentialReference = CredentialReference.CreateNew(),
            });

        Assert.False(result.Success);
        Assert.Equal(ConnectionFailureCategory.Validation, result.FailureCategory);
        Assert.Equal(0, vault.RetrieveCount);
    }

    [Fact]
    public async Task CreateAsync_LocalhostEmulatorEndpoint_SucceedsWithFakeProbe()
    {
        var probe = new FakeConnectionProbe { AdminProbeResult = true };
        var factory = CreateFactory(new FakeCredentialVault(), probe);

        using var credential = new SensitiveCredential(CreateEmulatorConnectionString());
        var result = await factory.CreateAsync(
            CreateSasRequest(endpoint: "localhost", credential: credential));

        Assert.True(result.Success);
        Assert.NotNull(result.Context);
        Assert.Equal("localhost", result.Context!.NamespaceEndpoint);
        Assert.True(result.Context.Capabilities.CanAdministerNamespace);
        Assert.Equal(1, probe.AdminProbeCount);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("host.docker.internal")]
    [InlineData("sb://localhost:5300/")]
    public async Task CreateAsync_WellKnownEmulatorHosts_PassValidation(string endpoint)
    {
        var factory = CreateFactory(new FakeCredentialVault(), new FakeConnectionProbe());

        using var credential = new SensitiveCredential(CreateEmulatorConnectionString());
        var result = await factory.CreateAsync(
            CreateSasRequest(endpoint: endpoint, credential: credential));

        Assert.True(result.Success, result.FailureMessage);
    }

    [Fact]
    public async Task CreateAsync_NonServiceBusHostWithoutEmulatorFlag_ReturnsValidationFailure()
    {
        var factory = CreateFactory(new FakeCredentialVault(), new FakeConnectionProbe());

        using var credential = new SensitiveCredential(CreateConnectionString());
        var result = await factory.CreateAsync(
            CreateSasRequest(endpoint: "not-a-servicebus-host.example", credential: credential));

        Assert.False(result.Success);
        Assert.Equal(ConnectionFailureCategory.Validation, result.FailureCategory);
        Assert.Contains("canonical", result.FailureMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_CancelledBeforeProbe_ThrowsOperationCanceledException()
    {
        var probe = new FakeConnectionProbe
        {
            OnAdminProbe = (_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(true);
            },
        };
        var factory = CreateFactory(new FakeCredentialVault(), probe);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        using var credential = new SensitiveCredential(CreateConnectionString());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            factory.CreateAsync(CreateSasRequest(credential: credential), cts.Token));
    }

    [Fact]
    public void ServiceBusFailureTranslator_RedactsSharedAccessKeyFromMessages()
    {
        const string keyField = "SharedAccess" + "Key";
        var message = $"Endpoint=sb://example.servicebus.windows.net/;{keyField}=super-secret-value";

        var safe = ServiceBusFailureTranslator.RedactSecrets(message);

        Assert.DoesNotContain("super-secret-value", safe, StringComparison.Ordinal);
        Assert.Contains("[redacted]", safe, StringComparison.Ordinal);
    }

    private static ConnectionContextFactory CreateFactory(
        ICredentialVault vault,
        IConnectionProbe probe,
        Func<ConnectionRequest, TokenCredential>? tokenCredentialFactory = null) =>
        new(vault, probe, tokenCredentialFactory);

    private static ConnectionRequest CreateSasRequest(
        string endpoint = "example.servicebus.windows.net",
        ConnectionScope scope = ConnectionScope.Namespace,
        string? entityPath = null,
        SensitiveCredential? credential = null,
        bool includeCredential = true,
        CredentialReference? credentialReference = null)
    {
        SensitiveCredential? sasCredential = null;
        if (includeCredential)
            sasCredential = credential ?? new SensitiveCredential(CreateConnectionString());

        return new ConnectionRequest
        {
            NamespaceEndpoint = endpoint,
            AuthMode = ServiceBusAuthMode.Sas,
            Scope = scope,
            EntityPath = entityPath,
            SasCredential = sasCredential,
            CredentialReference = credentialReference,
        };
    }

    private static string CreateConnectionString(string secret = "test-only-secret")
    {
        const string keyField = "SharedAccess" + "Key";
        return $"Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=test;{keyField}={secret}";
    }

    private static string CreateEmulatorConnectionString(string secret = "SAS_KEY_VALUE")
    {
        const string keyField = "SharedAccess" + "Key";
        return $"Endpoint=sb://localhost/;SharedAccessKeyName=RootManageSharedAccessKey;{keyField}={secret};UseDevelopmentEmulator=true;";
    }

    private sealed class FakeCredentialVault : ICredentialVault
    {
        private readonly Dictionary<string, SensitiveCredential> _entries = new(StringComparer.Ordinal);

        public int RetrieveCount { get; private set; }

        public int StoreCount { get; private set; }

        public int DeleteCount { get; private set; }

        public Task<CredentialVaultAvailabilityResult> GetAvailabilityAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CredentialVaultAvailabilityResult(
                CredentialVaultStatus.Available,
                "Available."));

        public Task<CredentialVaultMutationResult> StoreAsync(
            CredentialReference reference,
            SensitiveCredential credential,
            CancellationToken cancellationToken = default)
        {
            StoreCount++;
            _entries[reference.Value] = new SensitiveCredential(credential.Reveal());
            return Task.FromResult(new CredentialVaultMutationResult(
                CredentialVaultStatus.Available,
                "Stored."));
        }

        public Task<CredentialVaultRetrieveResult> RetrieveAsync(
            CredentialReference reference,
            CancellationToken cancellationToken = default)
        {
            RetrieveCount++;
            if (!_entries.TryGetValue(reference.Value, out _))
            {
                return Task.FromResult(new CredentialVaultRetrieveResult(
                    CredentialVaultStatus.NotFound,
                    "Credential was not found.",
                    null));
            }

            return Task.FromResult(new CredentialVaultRetrieveResult(
                CredentialVaultStatus.Available,
                "Retrieved.",
                new SensitiveCredential(_entries[reference.Value].Reveal())));
        }

        public Task<CredentialVaultMutationResult> DeleteAsync(
            CredentialReference reference,
            CancellationToken cancellationToken = default)
        {
            DeleteCount++;
            _entries.Remove(reference.Value);
            return Task.FromResult(new CredentialVaultMutationResult(
                CredentialVaultStatus.Available,
                "Deleted."));
        }
    }

    private sealed class FakeConnectionProbe : IConnectionProbe
    {
        public bool AdminProbeResult { get; init; } = true;

        public int AdminProbeCount { get; private set; }

        public Func<string, CancellationToken, Task<bool>>? OnAdminProbe { get; init; }

        public Task<bool> ProbeNamespaceAdminAsync(
            string fullyQualifiedNamespace,
            CancellationToken cancellationToken = default)
        {
            AdminProbeCount++;
            if (OnAdminProbe is not null)
                return OnAdminProbe(fullyQualifiedNamespace, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(AdminProbeResult);
        }

        public Task ProbeMessagingAsync(
            string fullyQualifiedNamespace,
            string? entityPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            new("fake-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            new(GetToken(requestContext, cancellationToken));
    }
}
