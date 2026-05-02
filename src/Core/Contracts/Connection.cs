#nullable enable
namespace ServiceBusExplorer;

public enum ServiceBusAuthMode { Sas, Windows, AzureActiveDirectory }

public record ConnectionOptions(
    string ConnectionString,
    ServiceBusAuthMode AuthMode = ServiceBusAuthMode.Sas,
    string? TenantId = null,
    string? EntityPath = null);
