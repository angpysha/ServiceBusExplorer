using System.Text.Json;

namespace ServiceBusExplorer.App;

public class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public List<ConnectionProfile> ConnectionHistory { get; set; } = [];
    public int DefaultPeekCount { get; set; } = 20;
    public string Theme { get; set; } = "Light";
}

public sealed class SettingsSafetyException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);

public class SettingsService
{
    private static readonly string DefaultConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "sbexplorer");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] ForbiddenMarkers =
    [
        "SharedAccessKey",
        "SharedAccessSignature",
        "connectionString",
        "credentialReference",
        "accessToken"
    ];

    private readonly string _configPath;

    public SettingsService(string? configPath = null)
    {
        _configPath = configPath ?? Path.Combine(DefaultConfigDir, "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(_configPath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(_configPath);
            if (RequiresSanitization(json))
                return SanitizeUnsafeSettings();

            var settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
            if (settings is null || !IsSafe(settings))
                return SanitizeUnsafeSettings();

            return settings;
        }
        catch (SettingsSafetyException)
        {
            throw;
        }
        catch
        {
            return SanitizeUnsafeSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        if (!IsSafe(settings))
            throw new SettingsSafetyException("Unsafe connection history was rejected.");

        WriteAtomically(settings);
    }

    public AppSettings RecordConnection(ConnectionOptions options, AppSettings? existing = null)
    {
        var settings = existing ?? Load();
        var profile = CreateProfile(options);
        settings.ConnectionHistory.RemoveAll(item => item == profile);
        settings.ConnectionHistory.Insert(0, profile);
        if (settings.ConnectionHistory.Count > 10)
            settings.ConnectionHistory = settings.ConnectionHistory.Take(10).ToList();
        Save(settings);
        return settings;
    }

    private AppSettings SanitizeUnsafeSettings()
    {
        var sanitized = new AppSettings();
        WriteAtomically(sanitized);
        return sanitized;
    }

    private void WriteAtomically(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_configPath)
            ?? throw new SettingsSafetyException("The settings path has no parent directory.");
        var temporaryPath = $"{_configPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(settings, SerializerOptions));
            File.Move(temporaryPath, _configPath, overwrite: true);
        }
        catch (Exception exception)
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch
            {
                // Preserve the original fail-closed persistence error.
            }

            throw new SettingsSafetyException(
                "Connection history could not be sanitized and saved safely.",
                exception);
        }
    }

    private static ConnectionProfile CreateProfile(ConnectionOptions options)
    {
        var endpointValue = options.ConnectionString
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2)
            .FirstOrDefault(parts =>
                parts[0].Equals("Endpoint", StringComparison.OrdinalIgnoreCase))?[1];

        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint) ||
            !endpoint.Scheme.Equals("sb", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The connection endpoint is missing or invalid.",
                nameof(options));
        }

        var namespaceEndpoint = endpoint.AbsoluteUri;
        var label = endpoint.Host.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            ?? endpoint.Host;

        return new ConnectionProfile(
            label,
            namespaceEndpoint,
            options.AuthMode,
            Normalize(options.TenantId),
            Normalize(options.EntityPath));
    }

    private static bool RequiresSanitization(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty(
                    "connectionHistory",
                    out var history) &&
                history.ValueKind == JsonValueKind.Array &&
                history.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String))
            {
                return true;
            }
        }
        catch (JsonException)
        {
            return true;
        }

        return ForbiddenMarkers.Any(marker =>
            json.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSafe(AppSettings settings) =>
        settings.SchemaVersion == AppSettings.CurrentSchemaVersion &&
        settings.ConnectionHistory.All(profile =>
            profile.SchemaVersion == ConnectionProfile.CurrentSchemaVersion &&
            Uri.TryCreate(profile.NamespaceEndpoint, UriKind.Absolute, out var endpoint) &&
            endpoint.Scheme.Equals("sb", StringComparison.OrdinalIgnoreCase) &&
            !ContainsForbiddenMarker(profile.Label) &&
            !ContainsForbiddenMarker(profile.NamespaceEndpoint) &&
            !ContainsForbiddenMarker(profile.TenantId) &&
            !ContainsForbiddenMarker(profile.EntityPath));

    private static bool ContainsForbiddenMarker(string? value) =>
        value is not null &&
        ForbiddenMarkers.Any(marker =>
            value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
