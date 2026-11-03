using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gameya.Application.Security;

/// <summary>
/// A phone number that is already safe to show to a given viewer (full or masked). It can only be created by
/// <see cref="PhoneDisplay"/>, and serializes as a plain JSON string.
/// </summary>
[JsonConverter(typeof(DisplayPhoneJsonConverter))]
public readonly record struct DisplayPhone
{
    internal DisplayPhone(string value) => Value = value;

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>Round-trip safe: reads and writes a plain string.</summary>
public sealed class DisplayPhoneJsonConverter : JsonConverter<DisplayPhone>
{
    public override DisplayPhone Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetString() ?? throw new JsonException("A display phone cannot be null."));

    public override void Write(Utf8JsonWriter writer, DisplayPhone value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
