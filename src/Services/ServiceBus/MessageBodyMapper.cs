using System.Text;

namespace ServiceBusExplorer.Services;

internal static class MessageBodyMapper
{
    public const int MaxDisplayBytes = 8192;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static MessageBodyRepresentation Map(ReadOnlyMemory<byte> body, string? contentType)
    {
        if (body.IsEmpty)
        {
            return new MessageBodyRepresentation(
                MessageBodyKind.Empty,
                DisplayText: "(empty)",
                ContentType: contentType);
        }

        var bytes = body.Span;
        if (!IsValidUtf8(bytes))
        {
            return new MessageBodyRepresentation(
                MessageBodyKind.Binary,
                DisplayText: $"[Binary content, {body.Length:N0} bytes]",
                FullLengthBytes: body.Length,
                ContentType: contentType);
        }

        var text = Encoding.UTF8.GetString(bytes);
        if (body.Length > MaxDisplayBytes)
        {
            var preview = text[..GetCharCountForByteLimit(text, MaxDisplayBytes)];
            return new MessageBodyRepresentation(
                MessageBodyKind.Truncated,
                DisplayText: preview,
                FullLengthBytes: body.Length,
                ContentType: contentType);
        }

        var kind = IsJsonContentType(contentType) ? MessageBodyKind.Json : MessageBodyKind.Text;
        return new MessageBodyRepresentation(kind, text, ContentType: contentType);
    }

    public static MessageBodyRepresentation Unavailable(string? contentType = null) =>
        new(MessageBodyKind.Unavailable, "(body unavailable)", ContentType: contentType);

    private static bool IsJsonContentType(string? contentType) =>
        contentType is not null &&
        contentType.Contains("json", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidUtf8(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Contains((byte)0))
            return false;

        try
        {
            _ = StrictUtf8.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static int GetCharCountForByteLimit(string text, int byteLimit)
    {
        var encoded = Encoding.UTF8.GetBytes(text);
        if (encoded.Length <= byteLimit)
            return text.Length;

        var slice = encoded.AsSpan(0, byteLimit);
        return Encoding.UTF8.GetString(slice).Length;
    }
}
