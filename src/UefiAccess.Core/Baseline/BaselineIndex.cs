// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Variables;

namespace UefiAccess.Core.Baseline;

/// <summary>Everything the exporter needs to write one baseline folder.</summary>
/// <param name="RootDirectory">Parent directory; the export goes into RootDirectory/RunId. Null or blank selects <see cref="BaselineExporter.DefaultRootDirectory"/>.</param>
/// <param name="RunId">Folder name, normally the timestamp "yyyyMMdd-HHmmss". Null or blank selects <see cref="BaselineExporter.DefaultRunId"/> of <paramref name="Timestamp"/>.</param>
/// <param name="Variables">The variables to export; null entries are skipped.</param>
/// <param name="FirmwareType">Detected firmware type.</param>
/// <param name="Privilege">Privilege state at export time.</param>
/// <param name="Identity">Machine identity, or null when unavailable (written as JSON null).</param>
/// <param name="Decoded">Object serialised to decoded.json; null writes the JSON literal "null".</param>
/// <param name="Platform">Object serialised to platform.json; null writes the JSON literal "null".</param>
/// <param name="LogText">Run log text written at the start of log.txt; the exporter appends its own outcome lines (files written, errors) after it. Null writes the outcome only.</param>
/// <param name="ToolVersion">Tool version written into index.json.</param>
/// <param name="Timestamp">Export time; index.json carries it as UTC ("yyyy-MM-ddTHH:mm:ssZ") and with its own offset ("yyyy-MM-ddTHH:mm:sszzz").</param>
public sealed record BaselineRequest(
    string RootDirectory,
    string RunId,
    IReadOnlyList<FirmwareVariable> Variables,
    FirmwareType FirmwareType,
    PrivilegeState Privilege,
    MachineIdentityResult? Identity,
    object? Decoded,
    object? Platform,
    string? LogText,
    string ToolVersion,
    DateTimeOffset Timestamp)
{
    /// <summary>
    /// The Dell BIOS settings of this baseline, as
    /// <see cref="DellSettingsFile.ToDocument"/> made them from one read; written as
    /// dell-settings.json after platform.json and before log.txt. Null, as in every request made
    /// before Alpha 2 and in the snapshot store's export, writes no such file.
    /// </summary>
    public DellSettingsFileDocument? DellSettings { get; init; }
}

/// <summary>One variable entry in index.json. Property order is fixed and there are no per-entry timestamps.</summary>
/// <param name="Key">Variable key ("guid:name").</param>
/// <param name="Name">Variable name.</param>
/// <param name="Guid">Vendor GUID in lowercase "D" format.</param>
/// <param name="Attributes">Raw attribute value.</param>
/// <param name="AttributeNames">Short flag text such as "NV BS RT" (see <see cref="VariableAttributeFlags.ToShortFlags"/>).</param>
/// <param name="Size">Data size in bytes.</param>
/// <param name="Sha256">Lowercase hex SHA-256 of the data.</param>
/// <param name="File">Relative path of the .bin file with forward slashes, for example "vars/8be4df61-..._BootOrder.bin".</param>
public sealed record BaselineEntry(
    string Key,
    string Name,
    string Guid,
    uint Attributes,
    string AttributeNames,
    int Size,
    string Sha256,
    string File);

/// <summary>The index.json document. Entries are sorted by key (ordinal) so the file is deterministic.</summary>
/// <param name="ToolVersion">Tool version.</param>
/// <param name="TimestampUtc">Export time in UTC, "yyyy-MM-ddTHH:mm:ssZ".</param>
/// <param name="TimestampLocal">Export time with its offset, "yyyy-MM-ddTHH:mm:sszzz".</param>
/// <param name="FirmwareType">Firmware type name.</param>
/// <param name="PrivilegeState">Privilege state name.</param>
/// <param name="Identity">Machine identity, or null.</param>
/// <param name="VariableCount">Number of entries.</param>
/// <param name="Variables">The entries, sorted by key (ordinal).</param>
public sealed record BaselineIndex(
    string ToolVersion,
    string TimestampUtc,
    string TimestampLocal,
    string FirmwareType,
    string PrivilegeState,
    MachineIdentityResult? Identity,
    int VariableCount,
    IReadOnlyList<BaselineEntry> Variables);

/// <summary>Outcome of an export.</summary>
/// <param name="Directory">The full path of the folder that was written (RootDirectory/RunId); empty when the request was null.</param>
/// <param name="VariableCount">Number of variables indexed (entries in index.json).</param>
/// <param name="FilesWritten">Relative paths (forward slashes) of every file written, in write order: vars/*.bin, index.json, decoded.json, platform.json, dell-settings.json (only when the request carries Dell settings), log.txt.</param>
/// <param name="Errors">Errors encountered; empty on success.</param>
public sealed record BaselineExportResult(
    string Directory,
    int VariableCount,
    IReadOnlyList<string> FilesWritten,
    IReadOnlyList<string> Errors);
