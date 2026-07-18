#nullable enable
using System.Net;
using System.Text.RegularExpressions;
using Azure;
using Azure.Messaging.ServiceBus;

namespace ServiceBusExplorer.Services;

/// <summary>
/// Classifies Service Bus connection failures and redacts secret material from safe messages.
/// </summary>
public static partial class ServiceBusFailureTranslator
{
    private static readonly Regex SharedAccessKeyPattern = SharedAccessKeyRegex();

    public static ConnectionFailureCategory Classify(Exception exception) =>
        exception switch
        {
            OperationCanceledException => ConnectionFailureCategory.Cancellation,
            ArgumentException => ConnectionFailureCategory.Validation,
            UnauthorizedAccessException => ConnectionFailureCategory.Authentication,
            RequestFailedException requestFailed => ClassifyRequestFailed(requestFailed),
            ServiceBusException serviceBus => ClassifyServiceBus(serviceBus),
            _ => ConnectionFailureCategory.Unknown,
        };

    public static string ToSafeMessage(Exception exception)
    {
        var message = exception switch
        {
            RequestFailedException requestFailed => requestFailed.Message,
            ServiceBusException serviceBus => serviceBus.Message,
            _ => exception.Message,
        };

        return RedactSecrets(message);
    }

    public static string RedactSecrets(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return string.Empty;

        var redacted = SharedAccessKeyPattern.Replace(message, "SharedAccessKey=[redacted]");
        redacted = Regex.Replace(
            redacted,
            @"SharedAccessSignature=[^;\s]+",
            "SharedAccessSignature=[redacted]",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return redacted;
    }

    private static ConnectionFailureCategory ClassifyRequestFailed(RequestFailedException exception) =>
        exception.Status switch
        {
            (int)HttpStatusCode.Unauthorized => ConnectionFailureCategory.Authentication,
            (int)HttpStatusCode.Forbidden => ConnectionFailureCategory.Authorization,
            (int)HttpStatusCode.TooManyRequests => ConnectionFailureCategory.Throttling,
            >= 500 => ConnectionFailureCategory.ServiceUnavailable,
            >= 400 => ConnectionFailureCategory.Validation,
            _ => ConnectionFailureCategory.Unknown,
        };

    private static ConnectionFailureCategory ClassifyServiceBus(ServiceBusException exception) =>
        exception.Reason switch
        {
            ServiceBusFailureReason.ServiceTimeout or ServiceBusFailureReason.ServiceCommunicationProblem
                => ConnectionFailureCategory.ServiceUnavailable,
            ServiceBusFailureReason.MessagingEntityNotFound or ServiceBusFailureReason.MessagingEntityAlreadyExists
                => ConnectionFailureCategory.Validation,
            ServiceBusFailureReason.QuotaExceeded => ConnectionFailureCategory.Throttling,
            ServiceBusFailureReason.ServiceBusy => ConnectionFailureCategory.Throttling,
            _ => ConnectionFailureCategory.Unknown,
        };

    [GeneratedRegex(
        @"SharedAccessKey=[^;\s]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SharedAccessKeyRegex();
}
