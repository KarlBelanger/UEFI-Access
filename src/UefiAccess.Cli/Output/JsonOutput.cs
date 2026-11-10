// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UefiAccess.Cli.Output;

/// <summary>
/// Serialises command results as one indented JSON document (camelCase properties, enums as
/// strings). A command that was given --json writes exactly one document to standard output and
/// nothing else there; every diagnostic goes to standard error. The write commands use
/// <see cref="KeyPreservingOptions"/>, the same settings with dictionary keys left as they are, so
/// the Boot#### names a write record is keyed by read as the record in write-log.jsonl has them.
/// </summary>
internal static class JsonOutput
{
    /// <summary>Serializer options shared by every command and by gate.json.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions(camelCaseDictionaryKeys: true);

    /// <summary>
    /// The settings of <see cref="Options"/> except that dictionary keys keep their spelling: the
    /// write documents embed the write record, whose device node types are keyed by Boot#### name,
    /// and a camel-cased key (boot0003) would no longer name the entry.
    /// </summary>
    public static JsonSerializerOptions KeyPreservingOptions { get; } = CreateOptions(camelCaseDictionaryKeys: false);

    /// <summary>Serialises <paramref name="value"/> to indented JSON text (no trailing newline).</summary>
    /// <param name="value">The object to serialise; null produces "null".</param>
    /// <returns>The JSON text.</returns>
    public static string Serialize(object? value)
    {
        return Serialize(value, Options);
    }

    /// <summary>Serialises <paramref name="value"/> to indented JSON text with the given settings (no trailing newline).</summary>
    /// <param name="value">The object to serialise; null produces "null".</param>
    /// <param name="options"><see cref="Options"/> or <see cref="KeyPreservingOptions"/>.</param>
    /// <returns>The JSON text.</returns>
    public static string Serialize(object? value, JsonSerializerOptions options)
    {
        return JsonSerializer.Serialize(value, value?.GetType() ?? typeof(object), options);
    }

    /// <summary>Writes one JSON document followed by a newline.</summary>
    /// <param name="writer">Destination, normally standard output.</param>
    /// <param name="value">The object to serialise.</param>
    public static void Write(TextWriter writer, object? value)
    {
        Write(writer, value, Options);
    }

    /// <summary>Writes one JSON document with the given settings, followed by a newline.</summary>
    /// <param name="writer">Destination, normally standard output.</param>
    /// <param name="value">The object to serialise.</param>
    /// <param name="options"><see cref="Options"/> or <see cref="KeyPreservingOptions"/>.</param>
    public static void Write(TextWriter writer, object? value, JsonSerializerOptions options)
    {
        writer.WriteLine(Serialize(value, options));
        writer.Flush();
    }

    private static JsonSerializerOptions CreateOptions(bool camelCaseDictionaryKeys)
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.General)
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = camelCaseDictionaryKeys ? JsonNamingPolicy.CamelCase : null,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            NumberHandling = JsonNumberHandling.Strict,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
