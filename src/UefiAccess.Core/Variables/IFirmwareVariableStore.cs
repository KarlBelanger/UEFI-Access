// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Core.Variables;

/// <summary>How an enumeration result was obtained.</summary>
public enum EnumerationMethod
{
    /// <summary>NtEnumerateSystemEnvironmentValuesEx succeeded.</summary>
    NtEnumerate,

    /// <summary>Enumeration failed; the curated <see cref="KnownVariableNames"/> list was read one by one.</summary>
    KnownNamesFallback,

    /// <summary>An in-memory store supplied the variables (tests, design-time).</summary>
    InMemory,

    /// <summary>No method produced variables (for example legacy BIOS boot or a scaffold stub).</summary>
    None,
}

/// <summary>Result of enumerating the variable store.</summary>
/// <param name="Variables">The variables found; empty on failure.</param>
/// <param name="Method">How the variables were obtained.</param>
/// <param name="EntriesParsed">Number of entries parsed from the native buffer (or read in the fallback).</param>
/// <param name="Error">A one-line error when enumeration failed or stopped early; null on success.</param>
/// <param name="Warnings">Non-fatal observations, for example individual fallback reads that failed.</param>
public sealed record EnumerationResult(
    IReadOnlyList<FirmwareVariable> Variables,
    EnumerationMethod Method,
    int EntriesParsed,
    string? Error,
    IReadOnlyList<string> Warnings);

/// <summary>Outcome of reading a single variable.</summary>
public enum ReadStatus
{
    /// <summary>The variable was read (possibly with zero-length data).</summary>
    Success,

    /// <summary>ERROR_ENVVAR_NOT_FOUND (203).</summary>
    NotFound,

    /// <summary>ERROR_PRIVILEGE_NOT_HELD (1314): SeSystemEnvironmentPrivilege is missing.</summary>
    PrivilegeNotHeld,

    /// <summary>ERROR_INVALID_FUNCTION (1): legacy BIOS boot or no runtime variable services.</summary>
    NotUefi,

    /// <summary>ERROR_ACCESS_DENIED (5).</summary>
    AccessDenied,

    /// <summary>The variable exceeded the 4 MiB buffer ceiling.</summary>
    TooLarge,

    /// <summary>Any other failure; see the error text.</summary>
    Failed,

    /// <summary>The store cannot run on this operating system (non-Windows host).</summary>
    NotSupportedOnThisOs,
}

/// <summary>Result of reading a single variable.</summary>
/// <param name="Variable">The variable when <paramref name="Status"/> is <see cref="ReadStatus.Success"/>; otherwise null.</param>
/// <param name="Status">The outcome.</param>
/// <param name="Error">A one-line error description, or null on success.</param>
/// <param name="LastBufferSize">The last buffer size tried, useful when a variable is too large.</param>
public sealed record ReadResult(
    FirmwareVariable? Variable,
    ReadStatus Status,
    string? Error,
    int LastBufferSize);

/// <summary>
/// Read-only access to the firmware variable store. Implementations never throw; they report
/// failures through the result records.
/// </summary>
public interface IFirmwareVariableStore
{
    /// <summary>Enumerates every runtime-accessible variable.</summary>
    /// <returns>The enumeration result.</returns>
    EnumerationResult Enumerate();

    /// <summary>Reads one variable by name and vendor GUID.</summary>
    /// <param name="name">Variable name (case-sensitive).</param>
    /// <param name="vendorGuid">Vendor GUID.</param>
    /// <returns>The read result.</returns>
    ReadResult Read(string name, Guid vendorGuid);
}
