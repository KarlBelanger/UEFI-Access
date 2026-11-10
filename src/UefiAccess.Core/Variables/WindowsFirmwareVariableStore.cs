// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Serilog;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Core.Native;
using UefiAccess.Core.Platform;

namespace UefiAccess.Core.Variables;

/// <summary>
/// The two native entry points the Windows store depends on, behind a seam so the store's buffer,
/// retry, fallback and error-mapping logic can be exercised with a fake on any host. The
/// production implementation (<see cref="WindowsNativeFirmwareApi"/>) forwards to the P/Invoke
/// declarations under Native/; no other implementation may call native code.
/// </summary>
internal interface INativeFirmwareApi
{
    /// <summary>
    /// True when the entry points can be called on this host. The production implementation
    /// answers <see cref="OperatingSystem.IsWindows"/>; the store reports "not supported on this OS"
    /// without calling anything else when this is false.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>NtEnumerateSystemEnvironmentValuesEx: enumerates the runtime-visible variables.</summary>
    /// <param name="informationClass">1 names only, 2 names and values.</param>
    /// <param name="buffer">Output buffer, or <see cref="IntPtr.Zero"/> to query the required size.</param>
    /// <param name="length">On input the buffer size; on output the bytes required or written.</param>
    /// <returns>The NTSTATUS value.</returns>
    uint EnumerateValues(uint informationClass, IntPtr buffer, ref uint length);

    /// <summary>GetFirmwareEnvironmentVariableExW: reads one variable and its attributes.</summary>
    /// <param name="name">Variable name.</param>
    /// <param name="guid">Vendor GUID as braced uppercase text.</param>
    /// <param name="buffer">Buffer that receives the data.</param>
    /// <param name="size">Size of <paramref name="buffer"/> in bytes.</param>
    /// <param name="attributes">Receives the variable attributes.</param>
    /// <returns>The number of bytes stored, or 0 on failure or for a zero-length variable (see <see cref="GetLastError"/>).</returns>
    uint ReadVariable(string name, string guid, IntPtr buffer, uint size, out uint attributes);

    /// <summary>The Win32 error code left by the last <see cref="ReadVariable"/> call (0 when it succeeded).</summary>
    /// <returns>The Win32 error code.</returns>
    int GetLastError();
}

/// <summary>Production seam: forwards to the P/Invoke declarations in <see cref="NtDll"/> and <see cref="Kernel32"/>.</summary>
internal sealed class WindowsNativeFirmwareApi : INativeFirmwareApi
{
    /// <summary>The shared instance.</summary>
    public static WindowsNativeFirmwareApi Instance { get; } = new();

    private WindowsNativeFirmwareApi()
    {
    }

    /// <inheritdoc />
    public bool IsAvailable => OperatingSystem.IsWindows();

    /// <inheritdoc />
    public uint EnumerateValues(uint informationClass, IntPtr buffer, ref uint length)
    {
        return NtDll.NtEnumerateSystemEnvironmentValuesEx(informationClass, buffer, ref length);
    }

    /// <inheritdoc />
    public uint ReadVariable(string name, string guid, IntPtr buffer, uint size, out uint attributes)
    {
        return Kernel32.GetFirmwareEnvironmentVariableExW(name, guid, buffer, size, out attributes);
    }

    /// <inheritdoc />
    public int GetLastError() => Marshal.GetLastWin32Error();
}

/// <summary>
/// Firmware variable store backed by NtEnumerateSystemEnvironmentValuesEx (information class 2)
/// with the curated-name fallback, and GetFirmwareEnvironmentVariableExW with the 64 KiB to 4 MiB
/// buffer strategy. Never throws; every failure is reported through the result records. On a
/// non-Windows host every call reports "not supported on this OS" without touching native code.
/// </summary>
public sealed class WindowsFirmwareVariableStore : IFirmwareVariableStore
{
    /// <summary>Initial read buffer: 64 KiB.</summary>
    public const int InitialReadBufferSize = 64 * 1024;

    /// <summary>Largest read buffer tried before a variable is reported as too large: 4 MiB.</summary>
    public const int MaximumReadBufferSize = 4 * 1024 * 1024;

    /// <summary>Consecutive Boot#### misses that end the fallback probe.</summary>
    public const int BootProbeMissLimit = 3;

    /// <summary>
    /// Sanity ceiling for the enumeration buffer the kernel asks for (256 MiB). NVRAM stores are a
    /// few MiB at most; a larger request is treated as a failure instead of an allocation attempt.
    /// </summary>
    private const uint MaximumEnumerationBufferSize = 256u * 1024 * 1024;

    private const string NotSupportedOnThisOs = "not supported on this OS: firmware variable access requires Windows";

    private readonly IFirmwareTypeDetector _detector;
    private readonly ILogger _logger;
    private readonly INativeFirmwareApi _api;

    /// <summary>Creates the store over the real native entry points.</summary>
    /// <param name="detector">Firmware type detector used to decide whether the fallback applies.</param>
    /// <param name="logger">Optional Serilog logger for native call tracing; null disables logging.</param>
    public WindowsFirmwareVariableStore(IFirmwareTypeDetector detector, ILogger? logger = null)
        : this(detector, logger, WindowsNativeFirmwareApi.Instance)
    {
    }

    /// <summary>Creates the store over an explicit native seam (tests supply a fake).</summary>
    /// <param name="detector">Firmware type detector used to decide whether the fallback applies.</param>
    /// <param name="logger">Optional Serilog logger for native call tracing; null disables logging.</param>
    /// <param name="api">The native seam; null selects <see cref="WindowsNativeFirmwareApi.Instance"/>.</param>
    internal WindowsFirmwareVariableStore(IFirmwareTypeDetector detector, ILogger? logger, INativeFirmwareApi? api)
    {
        _detector = detector;
        _logger = logger ?? Serilog.Core.Logger.None;
        _api = api ?? WindowsNativeFirmwareApi.Instance;
    }

    /// <inheritdoc />
    public EnumerationResult Enumerate()
    {
        if (!_api.IsAvailable)
        {
            _logger.Debug("NtEnumerateSystemEnvironmentValuesEx skipped: not running on Windows");
            return new EnumerationResult(Array.Empty<FirmwareVariable>(), EnumerationMethod.None, 0, NotSupportedOnThisOs, Array.Empty<string>());
        }

        string failure;
        try
        {
            EnumerationResult? native = EnumerateNative(out failure);
            if (native is not null)
            {
                return native;
            }
        }
        catch (Exception ex)
        {
            failure = "NtEnumerateSystemEnvironmentValuesEx threw " + ex.GetType().Name + ": " + ex.Message;
            _logger.Warning(ex, "NtEnumerateSystemEnvironmentValuesEx threw");
        }

        _logger.Warning("Enumeration failed: {Failure}", failure);
        return Fallback(failure);
    }

    /// <inheritdoc />
    public ReadResult Read(string name, Guid vendorGuid)
    {
        if (!_api.IsAvailable)
        {
            _logger.Debug("GetFirmwareEnvironmentVariableExW skipped: not running on Windows");
            return new ReadResult(null, ReadStatus.NotSupportedOnThisOs, NotSupportedOnThisOs, 0);
        }

        if (string.IsNullOrEmpty(name))
        {
            return new ReadResult(null, ReadStatus.Failed, "a variable name is required", 0);
        }

        try
        {
            return ReadCore(name, vendorGuid);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "GetFirmwareEnvironmentVariableExW threw for {Name} {Guid}", name, vendorGuid);
            return new ReadResult(null, ReadStatus.Failed, "GetFirmwareEnvironmentVariableExW threw " + ex.GetType().Name + ": " + ex.Message, 0);
        }
    }

    /// <summary>
    /// Runs the native enumeration. Returns a result on success (including a partial parse that
    /// produced at least one variable); returns null and sets <paramref name="failure"/> when the
    /// call or the parse produced nothing usable, so the caller can try the fallback.
    /// </summary>
    private EnumerationResult? EnumerateNative(out string failure)
    {
        const uint informationClass = NativeConstants.SYSTEM_ENVIRONMENT_VALUE_INFORMATION;
        uint required = 0;
        uint status = CallEnumerate(informationClass, IntPtr.Zero, ref required);

        if (status == NativeConstants.STATUS_SUCCESS)
        {
            // No buffer was supplied and the kernel reported success: the store is empty.
            failure = string.Empty;
            _logger.Information("NtEnumerateSystemEnvironmentValuesEx reported success with no buffer; treating the variable store as empty");
            return new EnumerationResult(
                Array.Empty<FirmwareVariable>(),
                EnumerationMethod.NtEnumerate,
                0,
                null,
                new[] { "NtEnumerateSystemEnvironmentValuesEx succeeded with a zero-length buffer; the firmware exposes no runtime variables" });
        }

        if (status != NativeConstants.STATUS_BUFFER_TOO_SMALL)
        {
            failure = "NtEnumerateSystemEnvironmentValuesEx failed: " + NativeError.DescribeNtStatus(status);
            return null;
        }

        if (required == 0)
        {
            failure = "NtEnumerateSystemEnvironmentValuesEx reported STATUS_BUFFER_TOO_SMALL with a required length of 0";
            return null;
        }

        // Allocate the reported size; if the store grew between calls, grow once more and retry, then fail.
        uint allocated = required;
        const int maxAttempts = 2;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (allocated > MaximumEnumerationBufferSize)
            {
                failure = $"NtEnumerateSystemEnvironmentValuesEx asked for {allocated} bytes, above the {MaximumEnumerationBufferSize}-byte sanity ceiling";
                return null;
            }

            IntPtr buffer = IntPtr.Zero;
            try
            {
                buffer = Marshal.AllocHGlobal((nint)allocated);
                uint length = allocated;
                status = CallEnumerate(informationClass, buffer, ref length);

                if (status == NativeConstants.STATUS_SUCCESS)
                {
                    // On success the length argument reports the bytes used; guard against odd values.
                    uint used = length == 0 || length > allocated ? allocated : length;
                    byte[] managed = new byte[used];
                    Marshal.Copy(buffer, managed, 0, (int)used);
                    return BuildResult(managed, out failure);
                }

                if (status == NativeConstants.STATUS_BUFFER_TOO_SMALL && attempt < maxAttempts)
                {
                    uint grown = Math.Max(length, allocated * 2);
                    _logger.Information("NtEnumerateSystemEnvironmentValuesEx still too small with {Allocated} bytes; retrying once with {Grown} bytes", allocated, grown);
                    allocated = grown;
                    continue;
                }

                failure = status == NativeConstants.STATUS_BUFFER_TOO_SMALL
                    ? $"NtEnumerateSystemEnvironmentValuesEx still reported STATUS_BUFFER_TOO_SMALL after growing the buffer to {allocated} bytes (required {length})"
                    : "NtEnumerateSystemEnvironmentValuesEx failed: " + NativeError.DescribeNtStatus(status);
                return null;
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }

        failure = "NtEnumerateSystemEnvironmentValuesEx did not produce a buffer";
        return null;
    }

    private EnumerationResult? BuildResult(byte[] managed, out string failure)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        ParsedEnumeration parsed = EnumerationBufferParser.Parse(managed);
        _logger.Debug("EnumerationBufferParser.Parse({Length} bytes) -> {Count} entries, error {Error}, {Elapsed} ms", managed.Length, parsed.EntriesParsed, parsed.Error ?? "none", stopwatch.Elapsed.TotalMilliseconds);

        if (parsed.Error is not null && parsed.EntriesParsed == 0)
        {
            failure = "the enumeration buffer could not be parsed: " + parsed.Error;
            return null;
        }

        failure = string.Empty;
        List<string> warnings = new();
        if (parsed.Error is not null)
        {
            warnings.Add("enumeration stopped early at a malformed entry; the variables before it are reported: " + parsed.Error);
        }

        _logger.Information("NtEnumerateSystemEnvironmentValuesEx produced {Count} variables from {Length} bytes", parsed.EntriesParsed, managed.Length);
        return new EnumerationResult(parsed.Variables, EnumerationMethod.NtEnumerate, parsed.EntriesParsed, parsed.Error, warnings);
    }

    private uint CallEnumerate(uint informationClass, IntPtr buffer, ref uint length)
    {
        uint requested = length;
        Stopwatch stopwatch = Stopwatch.StartNew();
        uint status = _api.EnumerateValues(informationClass, buffer, ref length);
        _logger.Debug(
            "NtEnumerateSystemEnvironmentValuesEx(class={Class}, buffer={Buffer}, length={Requested}) -> 0x{Status:X8} ({Description}), length {Returned}, {Elapsed} ms",
            informationClass,
            buffer == IntPtr.Zero ? "null" : "allocated",
            requested,
            status,
            NativeError.DescribeNtStatus(status),
            length,
            stopwatch.Elapsed.TotalMilliseconds);
        return status;
    }

    private EnumerationResult Fallback(string failure)
    {
        FirmwareType firmwareType = _detector.Detect();
        if (firmwareType != FirmwareType.Uefi)
        {
            string error = failure + "; the firmware type is " + firmwareType + ", so the known-names fallback does not apply";
            return new EnumerationResult(Array.Empty<FirmwareVariable>(), EnumerationMethod.None, 0, error, Array.Empty<string>());
        }

        List<string> warnings = new()
        {
            failure + "; falling back to reading the curated KnownVariableNames list one by one",
            "the fallback only sees the curated names and Boot#### entries; vendor-specific variables are not listed by this path",
        };
        List<FirmwareVariable> variables = new();
        _logger.Information("Falling back to the curated KnownVariableNames list ({Count} fixed names plus Boot#### probing)", KnownVariableNames.Fixed.Count);

        foreach ((string name, Guid guid) in KnownVariableNames.Fixed)
        {
            ReadResult read = Read(name, guid);
            if (read.Status == ReadStatus.Success && read.Variable is not null)
            {
                variables.Add(read.Variable);
                continue;
            }

            if (IsFatalForFallback(read.Status))
            {
                return AbortFallback(variables, warnings, name, read);
            }

            if (read.Status != ReadStatus.NotFound)
            {
                warnings.Add(name + ": " + (read.Error ?? read.Status.ToString()));
            }
        }

        int misses = 0;
        for (int index = 0; index <= 0xFFFF && misses < BootProbeMissLimit; index++)
        {
            string name = KnownVariableNames.BootName(index);
            ReadResult read = Read(name, KnownGuids.EfiGlobalVariable);
            if (read.Status == ReadStatus.Success && read.Variable is not null)
            {
                variables.Add(read.Variable);
                misses = 0;
                continue;
            }

            if (IsFatalForFallback(read.Status))
            {
                return AbortFallback(variables, warnings, name, read);
            }

            if (read.Status != ReadStatus.NotFound)
            {
                warnings.Add(name + ": " + (read.Error ?? read.Status.ToString()));
            }

            misses++;
        }

        _logger.Information("Known-names fallback read {Count} variables with {Warnings} warnings", variables.Count, warnings.Count);
        return new EnumerationResult(variables, EnumerationMethod.KnownNamesFallback, variables.Count, null, warnings);
    }

    private static bool IsFatalForFallback(ReadStatus status)
    {
        return status is ReadStatus.NotUefi or ReadStatus.PrivilegeNotHeld or ReadStatus.NotSupportedOnThisOs;
    }

    private EnumerationResult AbortFallback(List<FirmwareVariable> variables, List<string> warnings, string name, ReadResult read)
    {
        string error = "known-names fallback aborted while reading " + name + ": " + (read.Error ?? read.Status.ToString());
        _logger.Warning("Known-names fallback aborted: {Error}", error);
        return new EnumerationResult(variables, EnumerationMethod.KnownNamesFallback, variables.Count, error, warnings);
    }

    private ReadResult ReadCore(string name, Guid vendorGuid)
    {
        string guidText = KnownGuids.ToBracedUpper(vendorGuid);
        int size = InitialReadBufferSize;
        while (true)
        {
            IntPtr buffer = IntPtr.Zero;
            try
            {
                buffer = Marshal.AllocHGlobal(size);
                Stopwatch stopwatch = Stopwatch.StartNew();
                uint stored = _api.ReadVariable(name, guidText, buffer, (uint)size, out uint attributes);
                int error = _api.GetLastError();
                _logger.Debug(
                    "GetFirmwareEnvironmentVariableExW({Name}, {Guid}, size={Size}) -> {Stored} bytes, attributes 0x{Attributes:X}, error {Error}, {Elapsed} ms",
                    name,
                    guidText,
                    size,
                    stored,
                    attributes,
                    error,
                    stopwatch.Elapsed.TotalMilliseconds);

                if (stored > 0)
                {
                    int count = stored > (uint)size ? size : (int)stored;
                    byte[] data = new byte[count];
                    Marshal.Copy(buffer, data, 0, count);
                    return new ReadResult(new FirmwareVariable(name, vendorGuid, (VariableAttributes)attributes, data), ReadStatus.Success, null, size);
                }

                if (error == 0)
                {
                    // A zero-length variable is legal: the attributes come back and there is no data.
                    return new ReadResult(new FirmwareVariable(name, vendorGuid, (VariableAttributes)attributes, Array.Empty<byte>()), ReadStatus.Success, null, size);
                }

                if (error == NativeConstants.ERROR_INSUFFICIENT_BUFFER)
                {
                    if (size >= MaximumReadBufferSize)
                    {
                        string tooLarge = $"variable {name} {guidText} exceeds the {MaximumReadBufferSize}-byte buffer ceiling (last size tried {size}): " + NativeError.DescribeWin32(error);
                        _logger.Warning("{Message}", tooLarge);
                        return new ReadResult(null, ReadStatus.TooLarge, tooLarge, size);
                    }

                    size = Math.Min(size * 2, MaximumReadBufferSize);
                    continue;
                }

                ReadStatus status = MapReadError(error);
                string description = NativeError.DescribeWin32(error);
                if (status == ReadStatus.NotFound)
                {
                    _logger.Debug("Variable {Name} {Guid} not found", name, guidText);
                }
                else
                {
                    _logger.Warning("Reading {Name} {Guid} failed: {Description}", name, guidText, description);
                }

                return new ReadResult(null, status, description, size);
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }
    }

    private static ReadStatus MapReadError(int error)
    {
        return error switch
        {
            NativeConstants.ERROR_INVALID_FUNCTION => ReadStatus.NotUefi,
            NativeConstants.ERROR_ENVVAR_NOT_FOUND => ReadStatus.NotFound,
            NativeConstants.ERROR_PRIVILEGE_NOT_HELD => ReadStatus.PrivilegeNotHeld,
            NativeConstants.ERROR_ACCESS_DENIED => ReadStatus.AccessDenied,
            _ => ReadStatus.Failed,
        };
    }
}
