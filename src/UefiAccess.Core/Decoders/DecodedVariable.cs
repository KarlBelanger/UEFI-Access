// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Core.Decoders;

/// <summary>A labelled value inside a decoded variable.</summary>
/// <param name="Name">Field label, for example "Description".</param>
/// <param name="Value">Field value as text.</param>
public sealed record DecodedField(string Name, string Value);

/// <summary>Human-readable decoding of one variable.</summary>
/// <param name="Key">The variable key ("guid:name").</param>
/// <param name="Kind">Decoder kind, for example "BootOrder", "LoadOption", "SecureBootState", "OsIndications".</param>
/// <param name="Summary">One-line summary suitable for a screen reader.</param>
/// <param name="Fields">Ordered list of labelled fields.</param>
/// <param name="Json">An object suitable for JSON serialization (decoded.json), or null.</param>
/// <param name="Error">Description of malformed data, or null when decoding succeeded.</param>
public sealed record DecodedVariable(
    string Key,
    string Kind,
    string Summary,
    IReadOnlyList<DecodedField> Fields,
    object? Json,
    string? Error);
