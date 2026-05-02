namespace ServiceBusExplorer;

public enum RelayType { NetTcp, Http }

public record RelayInfo(
    string Name,
    RelayType Type,
    bool IsDynamic,
    int ListenerCount);

public record CreateRelayOptions(string Name, RelayType Type);
