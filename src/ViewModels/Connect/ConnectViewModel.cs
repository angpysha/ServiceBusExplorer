using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;

namespace ServiceBusExplorer.ViewModels;

public class ConnectViewModel : ReactiveObject
{
    private readonly ICredentialVault? _credentialVault;
    private readonly IConnectionProfileStore? _profileStore;

    private string _connectionString = "";
    private ServiceBusAuthMode _authMode = ServiceBusAuthMode.Sas;
    private string? _tenantId;
    private string? _entityPath;
    private bool _loadQueues = true;
    private bool _loadTopics = true;
    private bool _loadEventHubs = true;
    private bool _loadRelays = true;
    private bool _loadNotificationHubs = true;
    private bool _isConnecting;
    private string? _errorMessage;
    private bool _saveSasToVault;
    private bool _requiresManualSas;
    private bool _isVaultAvailable;
    private string? _vaultStatusMessage;
    private string? _statusMessage;
    private ConnectionProfile? _selectedProfile;
    private CredentialReference? _activeCredentialReference;

    public ConnectViewModel(
        ICredentialVault? credentialVault = null,
        IConnectionProfileStore? profileStore = null)
    {
        _credentialVault = credentialVault;
        _profileStore = profileStore;

        var canConnect = this.WhenAnyValue(
            x => x.ConnectionString,
            x => x.AuthMode,
            x => x.SelectedProfile,
            x => x.ActiveCredentialReference,
            x => x.RequiresManualSas,
            x => x.IsConnecting,
            (connectionString, authMode, profile, reference, requiresManualSas, connecting) =>
                !connecting && HasRequiredConnectInputs(
                    connectionString,
                    authMode,
                    profile,
                    reference,
                    requiresManualSas));

        ConnectCommand = ReactiveCommand.Create(BuildConnectionRequest, canConnect);

        this.WhenAnyValue(x => x.AuthMode)
            .Subscribe(mode =>
            {
                if (mode != ServiceBusAuthMode.Sas)
                {
                    SaveSasToVault = false;
                    RequiresManualSas = false;
                    VaultStatusMessage = null;
                }

                this.RaisePropertyChanged(nameof(IsSasAuthMode));
                this.RaisePropertyChanged(nameof(ShowSaveSasOption));
            });
    }

    public string ConnectionString
    {
        get => _connectionString;
        set => this.RaiseAndSetIfChanged(ref _connectionString, value);
    }

    public ServiceBusAuthMode AuthMode
    {
        get => _authMode;
        set => this.RaiseAndSetIfChanged(ref _authMode, value);
    }

    public string? TenantId
    {
        get => _tenantId;
        set => this.RaiseAndSetIfChanged(ref _tenantId, value);
    }

    public string? EntityPath
    {
        get => _entityPath;
        set => this.RaiseAndSetIfChanged(ref _entityPath, value);
    }

    public bool LoadQueues
    {
        get => _loadQueues;
        set => this.RaiseAndSetIfChanged(ref _loadQueues, value);
    }

    public bool LoadTopics
    {
        get => _loadTopics;
        set => this.RaiseAndSetIfChanged(ref _loadTopics, value);
    }

    public bool LoadEventHubs
    {
        get => _loadEventHubs;
        set => this.RaiseAndSetIfChanged(ref _loadEventHubs, value);
    }

    public bool LoadRelays
    {
        get => _loadRelays;
        set => this.RaiseAndSetIfChanged(ref _loadRelays, value);
    }

    public bool LoadNotificationHubs
    {
        get => _loadNotificationHubs;
        set => this.RaiseAndSetIfChanged(ref _loadNotificationHubs, value);
    }

    public bool IsConnecting
    {
        get => _isConnecting;
        set => this.RaiseAndSetIfChanged(ref _isConnecting, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    /// <summary>
    /// Opt-in vault save toggle. Defaults false for every new or edited profile.
    /// </summary>
    public bool SaveSasToVault
    {
        get => _saveSasToVault;
        set => this.RaiseAndSetIfChanged(ref _saveSasToVault, value);
    }

    public bool RequiresManualSas
    {
        get => _requiresManualSas;
        private set => this.RaiseAndSetIfChanged(ref _requiresManualSas, value);
    }

    public bool IsVaultAvailable
    {
        get => _isVaultAvailable;
        private set => this.RaiseAndSetIfChanged(ref _isVaultAvailable, value);
    }

    public string? VaultStatusMessage
    {
        get => _vaultStatusMessage;
        private set => this.RaiseAndSetIfChanged(ref _vaultStatusMessage, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public ConnectionProfile? SelectedProfile
    {
        get => _selectedProfile;
        private set => this.RaiseAndSetIfChanged(ref _selectedProfile, value);
    }

    public CredentialReference? ActiveCredentialReference
    {
        get => _activeCredentialReference;
        private set => this.RaiseAndSetIfChanged(ref _activeCredentialReference, value);
    }

    public bool IsSasAuthMode => AuthMode == ServiceBusAuthMode.Sas;

    public bool ShowSaveSasOption => IsSasAuthMode && IsVaultAvailable;

    public static IReadOnlyList<ServiceBusAuthMode> AuthModes { get; } =
        Enum.GetValues<ServiceBusAuthMode>();

    public ObservableCollection<ConnectionProfile> ConnectionHistory { get; } = [];

    public ReactiveCommand<Unit, ConnectionRequest> ConnectCommand { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_credentialVault is null)
        {
            IsVaultAvailable = false;
            VaultStatusMessage = "Credential vault is not configured.";
            return;
        }

        var availability = await _credentialVault
            .GetAvailabilityAsync(cancellationToken)
            .ConfigureAwait(false);
        IsVaultAvailable = availability.Status == CredentialVaultStatus.Available;
        VaultStatusMessage = availability.RecoveryGuidance;
        this.RaisePropertyChanged(nameof(ShowSaveSasOption));
    }

    public void ApplyProfile(ConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        ConnectionString = string.Empty;
        AuthMode = profile.AuthMode;
        TenantId = profile.TenantId;
        EntityPath = profile.EntityPath;
        SaveSasToVault = false;
        RequiresManualSas = false;
        StatusMessage = null;
        ErrorMessage = null;
        SelectedProfile = profile;
        ActiveCredentialReference = profile.CredentialReference;
    }

    public void SetVaultRetrieveFailure(CredentialReference reference, string message)
    {
        RequiresManualSas = true;
        ActiveCredentialReference = reference;
        VaultStatusMessage = message;
    }

    public async Task ApplyProfileAsync(
        ConnectionProfile profile,
        CancellationToken cancellationToken = default)
    {
        ApplyProfile(profile);

        if (_profileStore is null ||
            profile.AuthMode != ServiceBusAuthMode.Sas ||
            profile.CredentialReference is null)
        {
            return;
        }

        var retrieve = await _profileStore
            .RetrieveCredentialAsync(profile.Id, cancellationToken)
            .ConfigureAwait(false);

        if (retrieve.Status == CredentialVaultStatus.Available && retrieve.Credential is not null)
        {
            ConnectionString = retrieve.Credential.Reveal();
            retrieve.Credential.Dispose();
            RequiresManualSas = false;
            VaultStatusMessage = "Saved SAS retrieved from the credential vault.";
            return;
        }

        RequiresManualSas = true;
        ActiveCredentialReference = profile.CredentialReference;
        VaultStatusMessage = retrieve.RecoveryGuidance;
    }

    public async Task SaveCredentialAsync(CancellationToken cancellationToken = default)
    {
        EnsureProfileStore();
        var profileId = RequireSelectedProfileId();

        if (AuthMode != ServiceBusAuthMode.Sas || string.IsNullOrWhiteSpace(ConnectionString))
            throw new InvalidOperationException("A SAS connection string is required to save credentials.");

        using var credential = new SensitiveCredential(ConnectionString);
        var result = await _profileStore!
            .SaveCredentialAsync(profileId, credential, cancellationToken)
            .ConfigureAwait(false);

        SelectedProfile = result.Profile;
        ActiveCredentialReference = result.Profile.CredentialReference;
        StatusMessage = result.Status == CredentialVaultStatus.Available
            ? "Reconnect credential saved in the native vault."
            : $"Connected, but reconnect was not saved. {result.RecoveryGuidance}";
    }

    public async Task ReplaceCredentialAsync(CancellationToken cancellationToken = default)
    {
        EnsureProfileStore();
        var profileId = RequireSelectedProfileId();

        if (AuthMode != ServiceBusAuthMode.Sas || string.IsNullOrWhiteSpace(ConnectionString))
            throw new InvalidOperationException("A SAS connection string is required to replace credentials.");

        using var credential = new SensitiveCredential(ConnectionString);
        var result = await _profileStore!
            .ReplaceCredentialAsync(profileId, credential, cancellationToken)
            .ConfigureAwait(false);

        SelectedProfile = result.Profile;
        ActiveCredentialReference = result.Profile.CredentialReference;
        StatusMessage = result.Status == CredentialVaultStatus.Available
            ? "Saved SAS replaced in the native vault."
            : $"Replace outcome is uncertain. {result.RecoveryGuidance}";
    }

    public async Task RemoveProfileAsync(
        string profileId,
        bool deleteVaultItem,
        CancellationToken cancellationToken = default)
    {
        EnsureProfileStore();

        var result = await _profileStore!
            .RemoveAsync(
                profileId,
                deleteVaultItem,
                allowMetadataOnlyAfterVaultFailure: false,
                cancellationToken)
            .ConfigureAwait(false);

        if (result.Status != CredentialVaultStatus.Available)
        {
            StatusMessage = $"Cleanup failed. {result.RecoveryGuidance}";
            return;
        }

        var removed = ConnectionHistory.FirstOrDefault(item => item.Id == profileId);
        if (removed is not null)
            ConnectionHistory.Remove(removed);

        if (SelectedProfile?.Id == profileId)
        {
            SelectedProfile = null;
            ActiveCredentialReference = null;
        }

        StatusMessage = result.RecoveryGuidance;
    }

    public async Task HandlePostConnectVaultAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        if (_profileStore is null || AuthMode != ServiceBusAuthMode.Sas)
            return;

        if (!SaveSasToVault)
        {
            StatusMessage = "Connected. Reconnect was not saved because vault save is off.";
            return;
        }

        using var credential = new SensitiveCredential(ConnectionString);
        ProfileCredentialMutationResult result;
        var profile = SelectedProfile ?? ConnectionHistory.FirstOrDefault(item => item.Id == profileId);
        if (profile?.CredentialReference is not null)
            result = await _profileStore.ReplaceCredentialAsync(profileId, credential, cancellationToken)
                .ConfigureAwait(false);
        else
            result = await _profileStore.SaveCredentialAsync(profileId, credential, cancellationToken)
                .ConfigureAwait(false);

        SelectedProfile = result.Profile;
        ActiveCredentialReference = result.Profile.CredentialReference;
        StatusMessage = result.Status == CredentialVaultStatus.Available
            ? "Connected and reconnect credential saved."
            : $"Connected, but reconnect was not saved. {result.RecoveryGuidance}";
    }

    public ConnectionRequest BuildConnectionRequest()
    {
        var namespaceEndpoint = ResolveNamespaceEndpoint();
        SensitiveCredential? sasCredential = null;
        CredentialReference? credentialReference = null;

        if (AuthMode == ServiceBusAuthMode.Sas)
        {
            if (!string.IsNullOrWhiteSpace(ConnectionString))
                sasCredential = new SensitiveCredential(ConnectionString);
            else
                credentialReference = ActiveCredentialReference ?? SelectedProfile?.CredentialReference;
        }

        return new ConnectionRequest
        {
            NamespaceEndpoint = namespaceEndpoint,
            AuthMode = AuthMode,
            Scope = ConnectionScope.Namespace,
            EntityPath = EntityPath,
            TenantId = TenantId,
            EntraInteraction = AuthMode == ServiceBusAuthMode.AzureActiveDirectory
                ? EntraInteractionMode.Default
                : null,
            SasCredential = sasCredential,
            CredentialReference = credentialReference,
            ProfileId = SelectedProfile?.Id,
        };
    }

    private static bool HasRequiredConnectInputs(
        string connectionString,
        ServiceBusAuthMode authMode,
        ConnectionProfile? profile,
        CredentialReference? reference,
        bool requiresManualSas)
    {
        return authMode switch
        {
            ServiceBusAuthMode.Sas when !string.IsNullOrWhiteSpace(connectionString) => true,
            ServiceBusAuthMode.Sas when reference is not null && !requiresManualSas => true,
            ServiceBusAuthMode.AzureActiveDirectory =>
                !string.IsNullOrWhiteSpace(ResolveNamespaceEndpoint(connectionString, profile)),
            _ => false,
        };
    }

    private string ResolveNamespaceEndpoint() =>
        ResolveNamespaceEndpoint(ConnectionString, SelectedProfile)
        ?? throw new InvalidOperationException("A Service Bus namespace endpoint is required.");

    private static string? ResolveNamespaceEndpoint(string connectionString, ConnectionProfile? profile)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            var endpoint = connectionString
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => part.Split('=', 2, StringSplitOptions.TrimEntries))
                .FirstOrDefault(parts =>
                    parts.Length == 2 &&
                    parts[0].Equals("Endpoint", StringComparison.OrdinalIgnoreCase))?[1];

            if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
                return uri.Host;
        }

        if (profile is not null &&
            Uri.TryCreate(profile.NamespaceEndpoint, UriKind.Absolute, out var profileEndpoint))
        {
            return profileEndpoint.Host;
        }

        return null;
    }

    private string RequireSelectedProfileId() =>
        SelectedProfile?.Id
        ?? throw new InvalidOperationException("Select a connection profile first.");

    private void EnsureProfileStore()
    {
        if (_profileStore is null)
            throw new InvalidOperationException("Connection profile store is not configured.");
    }
}
