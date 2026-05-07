using System.Text.Json;

namespace ServiceBusExplorer.App;

public class AppSettings
{
    public List<string> ConnectionHistory { get; set; } = new();
    public int DefaultPeekCount { get; set; } = 20;
    public string Theme { get; set; } = "Light";
}

public class SettingsService
{
    private static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "sbexplorer");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "settings.json");

    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<AppSettings>(json, _opts) ?? new AppSettings();
            }
        }
        catch { /* first run or corrupted — return defaults */ }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(settings, _opts));
        }
        catch { /* non-fatal — just skip persistence */ }
    }

    public AppSettings AddToHistory(string connectionString, AppSettings? existing = null)
    {
        var settings = existing ?? Load();
        // Mask/trim before storing — remove the key value for display, but keep full string for reconnect
        var trimmed = connectionString.Trim();
        settings.ConnectionHistory.Remove(trimmed);              // deduplicate
        settings.ConnectionHistory.Insert(0, trimmed);           // most-recent first
        if (settings.ConnectionHistory.Count > 10)
            settings.ConnectionHistory = settings.ConnectionHistory.Take(10).ToList();
        Save(settings);
        return settings;
    }

    /// Returns a display-safe label (namespace name only) from a connection string.
    public static string GetDisplayLabel(string connectionString)
    {
        try
        {
            // Endpoint=sb://namespace.servicebus.windows.net/;...
            var match = System.Text.RegularExpressions.Regex.Match(
                connectionString, @"Endpoint=sb://([^.]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups[1].Value;
        }
        catch { }
        // Fallback: first 60 chars with key redacted
        var idx = connectionString.IndexOf("SharedAccessKey=", StringComparison.OrdinalIgnoreCase);
        return idx > 0 ? connectionString[..Math.Min(idx, 60)] + "…" : connectionString[..Math.Min(60, connectionString.Length)];
    }
}
