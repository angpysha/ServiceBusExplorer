#nullable enable
namespace ServiceBusExplorer;

public record NotificationHubInfo(
    string Name,
    string Path,
    string? ApnsCredential,
    string? GcmCredential,
    int RegistrationCount);
