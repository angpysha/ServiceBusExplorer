namespace ServiceBusExplorer.IntegrationTests.Fixtures;

using Xunit;

/// <summary>
/// Skips unless <see cref="IntegrationTestGate.EnabledEnvironmentVariable"/> is set to <c>1</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (!IntegrationTestGate.IsEnabled)
        {
            Skip = $"{IntegrationTestGate.EnabledEnvironmentVariable}=1 required. Run scripts/run-integration-tests.ps1.";
        }
    }
}

/// <summary>
/// Opt-in gate for Docker Compose emulator tests.
/// </summary>
internal static class IntegrationTestGate
{
    public const string EnabledEnvironmentVariable = "SBE_INTEGRATION";

    public static bool IsEnabled =>
        string.Equals(
            Environment.GetEnvironmentVariable(EnabledEnvironmentVariable),
            "1",
            StringComparison.Ordinal);
}
