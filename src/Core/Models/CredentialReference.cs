#nullable enable
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServiceBusExplorer;

/// <summary>
/// Opaque native-vault item key. Contains no namespace, entity, or credential-derived data.
/// </summary>
[JsonConverter(typeof(CredentialReferenceJsonConverter))]
public sealed class CredentialReference : IEquatable<CredentialReference>
{
    public const int ByteLength = 32;

    public CredentialReference(string value)
    {
        if (!IsOpaque(value))
        {
            throw new ArgumentException(
                "Credential references must be high-entropy opaque identifiers.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public static CredentialReference CreateNew()
    {
        Span<byte> bytes = stackalloc byte[ByteLength];
        RandomNumberGenerator.Fill(bytes);
        return new CredentialReference(Convert.ToHexString(bytes).ToLowerInvariant());
    }

    public static bool IsOpaque(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (value.Length != ByteLength * 2)
            return false;

        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character))
                return false;
        }

        return true;
    }

    public bool Equals(CredentialReference? other) =>
        other is not null &&
        string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) =>
        obj is CredentialReference other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(CredentialReference? left, CredentialReference? right) =>
        Equals(left, right);

    public static bool operator !=(CredentialReference? left, CredentialReference? right) =>
        !Equals(left, right);
}

file sealed class CredentialReferenceJsonConverter : JsonConverter<CredentialReference>
{
    public override CredentialReference? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : new CredentialReference(value);
    }

    public override void Write(
        Utf8JsonWriter writer,
        CredentialReference value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
