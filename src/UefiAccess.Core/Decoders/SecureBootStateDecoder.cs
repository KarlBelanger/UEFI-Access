// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Variables;
using static UefiAccess.Core.Decoders.DecoderText;

namespace UefiAccess.Core.Decoders;

/// <summary>Secure Boot state assembled from the global variables and the signature databases.</summary>
/// <param name="SecureBoot">SecureBoot variable (single byte), or null when absent.</param>
/// <param name="SetupMode">SetupMode variable (single byte), or null when absent.</param>
/// <param name="AuditMode">AuditMode variable (single byte), or null when absent.</param>
/// <param name="DeployedMode">DeployedMode variable (single byte), or null when absent.</param>
/// <param name="PkPresent">True when PK exists.</param>
/// <param name="PkSize">Size of PK in bytes (0 when absent).</param>
/// <param name="KekPresent">True when KEK exists.</param>
/// <param name="KekSize">Size of KEK in bytes (0 when absent).</param>
/// <param name="Db">Summary of db, or null when absent.</param>
/// <param name="Dbx">Summary of dbx, or null when absent.</param>
/// <param name="Dbt">Summary of dbt, or null when absent.</param>
public sealed record SecureBootState(
    bool? SecureBoot,
    bool? SetupMode,
    bool? AuditMode,
    bool? DeployedMode,
    bool PkPresent,
    int PkSize,
    bool KekPresent,
    int KekSize,
    SignatureDbInfo? Db,
    SignatureDbInfo? Dbx,
    SignatureDbInfo? Dbt);

/// <summary>
/// The Secure Boot mode of UEFI 2.10 section 32.3, derived from the SetupMode, AuditMode and
/// DeployedMode variables (see <see cref="SecureBootStateReader.DeriveMode(bool?, bool?, bool?)"/>).
/// </summary>
public enum SecureBootMode
{
    /// <summary>SetupMode is absent or unreadable, so the mode cannot be named.</summary>
    Unknown = 0,

    /// <summary>Setup Mode: SetupMode=1 and AuditMode is not 1; no Platform Key is enrolled.</summary>
    Setup,

    /// <summary>User Mode: SetupMode=0 and DeployedMode is 0 or absent (firmware before UEFI 2.5 has no DeployedMode).</summary>
    User,

    /// <summary>Audit Mode: AuditMode=1 (the specification pairs it with SetupMode=1).</summary>
    Audit,

    /// <summary>Deployed Mode: SetupMode=0 and DeployedMode=1.</summary>
    Deployed,
}

/// <summary>
/// Builds a <see cref="SecureBootState"/> from a variable snapshot: SecureBoot, SetupMode, AuditMode,
/// DeployedMode, PK and KEK under the EFI global variable GUID; db, dbx and dbt under the image
/// security database GUID. A single-byte variable is true when its first byte is non-zero; a
/// zero-length one counts as absent. Never throws.
/// </summary>
public static class SecureBootStateReader
{
    /// <summary>The state when nothing is present.</summary>
    public static SecureBootState Empty { get; } = new(null, null, null, null, false, 0, false, 0, null, null, null);

    /// <summary>Reads the Secure Boot state from the snapshot.</summary>
    /// <param name="lookup">The snapshot.</param>
    /// <returns>The state; absent variables are null or false.</returns>
    public static SecureBootState Read(IVariableLookup lookup)
    {
        if (lookup is null)
        {
            return Empty;
        }

        try
        {
            Guid global = KnownGuids.EfiGlobalVariable;
            Guid security = KnownGuids.EfiImageSecurityDatabase;
            FirmwareVariable? pk = lookup.Find("PK", global);
            FirmwareVariable? kek = lookup.Find("KEK", global);
            return new SecureBootState(
                ReadFlag(lookup.Find("SecureBoot", global)),
                ReadFlag(lookup.Find("SetupMode", global)),
                ReadFlag(lookup.Find("AuditMode", global)),
                ReadFlag(lookup.Find("DeployedMode", global)),
                pk is not null,
                pk?.Size ?? 0,
                kek is not null,
                kek?.Size ?? 0,
                ReadDatabase(lookup.Find("db", security)),
                ReadDatabase(lookup.Find("dbx", security)),
                ReadDatabase(lookup.Find("dbt", security)));
        }
        catch (Exception)
        {
            return Empty;
        }
    }

    /// <summary>
    /// Names the Secure Boot mode of UEFI 2.10 section 32.3 from the three mode variables:
    /// AuditMode=1 is Audit Mode; otherwise SetupMode=1 is Setup Mode; otherwise SetupMode=0 is
    /// Deployed Mode when DeployedMode=1 and User Mode when DeployedMode is 0 or absent. With
    /// SetupMode unknown the mode is <see cref="SecureBootMode.Unknown"/>, unless DeployedMode=1
    /// (which the specification allows only with SetupMode=0).
    /// </summary>
    /// <param name="setupMode">The SetupMode flag, or null when absent.</param>
    /// <param name="auditMode">The AuditMode flag, or null when absent.</param>
    /// <param name="deployedMode">The DeployedMode flag, or null when absent.</param>
    /// <returns>The mode.</returns>
    public static SecureBootMode DeriveMode(bool? setupMode, bool? auditMode, bool? deployedMode)
    {
        if (auditMode == true)
        {
            return SecureBootMode.Audit;
        }

        if (setupMode == true)
        {
            return SecureBootMode.Setup;
        }

        if (deployedMode == true)
        {
            return SecureBootMode.Deployed;
        }

        return setupMode == false ? SecureBootMode.User : SecureBootMode.Unknown;
    }

    /// <summary>Names the Secure Boot mode of a state (see <see cref="DeriveMode(bool?, bool?, bool?)"/>).</summary>
    /// <param name="state">The state, or null.</param>
    /// <returns>The mode; <see cref="SecureBootMode.Unknown"/> for null.</returns>
    public static SecureBootMode DeriveMode(SecureBootState? state)
    {
        return state is null ? SecureBootMode.Unknown : DeriveMode(state.SetupMode, state.AuditMode, state.DeployedMode);
    }

    /// <summary>Lower-case name of a mode as the SetupMode decoding words it: "setup mode", "user mode", "audit mode", "deployed mode", or "unknown mode".</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>The phrase.</returns>
    public static string DescribeMode(SecureBootMode mode)
    {
        return mode switch
        {
            SecureBootMode.Setup => "setup mode",
            SecureBootMode.User => "user mode",
            SecureBootMode.Audit => "audit mode",
            SecureBootMode.Deployed => "deployed mode",
            _ => "unknown mode",
        };
    }

    /// <summary>Interprets a single-byte flag variable: null when absent or empty, otherwise first byte non-zero.</summary>
    /// <param name="variable">The variable, or null.</param>
    /// <returns>The flag value, or null.</returns>
    public static bool? ReadFlag(FirmwareVariable? variable)
    {
        return variable?.Data is { Length: > 0 } data ? data[0] != 0 : null;
    }

    private static SignatureDbInfo? ReadDatabase(FirmwareVariable? variable)
    {
        return variable is null ? null : SignatureListReader.Read(variable.Data ?? Array.Empty<byte>());
    }
}

/// <summary>
/// Decodes the Secure Boot variables individually: SecureBoot, SetupMode, AuditMode and DeployedMode
/// as single bytes; PK, KEK (global GUID) and db, dbx, dbt (image security database GUID) as
/// signature databases through <see cref="SignatureListReader"/>. Kind is "SecureBootState". Never throws.
/// </summary>
public sealed class SecureBootStateDecoder : IVariableDecoder
{
    private const string Kind = "SecureBootState";

    private static readonly string[] FlagNames = { "SecureBoot", "SetupMode", "AuditMode", "DeployedMode" };
    private static readonly string[] GlobalDatabaseNames = { "PK", "KEK" };
    private static readonly string[] SecurityDatabaseNames = { "db", "dbx", "dbt" };

    /// <inheritdoc />
    public bool CanDecode(FirmwareVariable variable)
    {
        if (variable is null || variable.Name is null)
        {
            return false;
        }

        if (variable.VendorGuid == KnownGuids.EfiGlobalVariable)
        {
            return Array.IndexOf(FlagNames, variable.Name) >= 0 || Array.IndexOf(GlobalDatabaseNames, variable.Name) >= 0;
        }

        return variable.VendorGuid == KnownGuids.EfiImageSecurityDatabase && Array.IndexOf(SecurityDatabaseNames, variable.Name) >= 0;
    }

    /// <inheritdoc />
    /// <remarks>
    /// SetupMode is worded with the mode it implies (UEFI 2.10 section 32.3), which needs AuditMode
    /// and DeployedMode from <paramref name="lookup"/>: SetupMode=0 is "user mode" unless
    /// DeployedMode=1 ("deployed mode"), and AuditMode=1 makes it "audit mode".
    /// </remarks>
    public DecodedVariable Decode(FirmwareVariable variable, IVariableLookup lookup)
    {
        if (variable is null)
        {
            return new DecodedVariable(string.Empty, Kind, "no variable", Array.Empty<DecodedField>(), null, "variable is null");
        }

        byte[] data = variable.Data ?? Array.Empty<byte>();
        return Array.IndexOf(FlagNames, variable.Name) >= 0
            ? DecodeFlag(variable, data, lookup)
            : DecodeDatabase(variable, data);
    }

    /// <summary>
    /// The SetupMode meaning: the mode named by <see cref="SecureBootStateReader.DeriveMode(bool?, bool?, bool?)"/>
    /// from this value and the AuditMode and DeployedMode variables in the snapshot, then whether a
    /// Platform Key is enrolled (SetupMode=0) or not (SetupMode=1).
    /// </summary>
    private static string DescribeSetupMode(bool set, IVariableLookup? lookup)
    {
        bool? auditMode = FindGlobalFlag(lookup, "AuditMode");
        bool? deployedMode = FindGlobalFlag(lookup, "DeployedMode");
        SecureBootMode mode = SecureBootStateReader.DeriveMode(set, auditMode, deployedMode);
        string key = set ? "the platform has no Platform Key enrolled" : "a Platform Key is enrolled";
        return SecureBootStateReader.DescribeMode(mode) + ": " + key;
    }

    private static bool? FindGlobalFlag(IVariableLookup? lookup, string name)
    {
        try
        {
            return lookup is null ? null : SecureBootStateReader.ReadFlag(lookup.Find(name, KnownGuids.EfiGlobalVariable));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static DecodedVariable DecodeFlag(FirmwareVariable variable, byte[] data, IVariableLookup? lookup)
    {
        string name = variable.Name;
        if (data.Length == 0)
        {
            string emptyError = Invariant($"{name} is empty; expected 1 byte");
            return new DecodedVariable(variable.Key, Kind, name + ": no data", new[] { new DecodedField("Error", emptyError) }, new { Name = name, Value = (int?)null, Set = (bool?)null, Error = emptyError }, emptyError);
        }

        byte value = data[0];
        bool set = value != 0;
        string? error = data.Length == 1 ? null : Invariant($"{name} is {data.Length} bytes; expected 1 (first byte decoded)");
        string meaning = name switch
        {
            "SecureBoot" => set ? "Secure Boot is enabled" : "Secure Boot is disabled",
            "SetupMode" => DescribeSetupMode(set, lookup),
            "AuditMode" => set ? "audit mode is on" : "audit mode is off",
            _ => set ? "deployed mode is on" : "deployed mode is off",
        };

        List<DecodedField> fields = new()
        {
            new DecodedField("Value", Invariant($"{value}")),
            new DecodedField("Meaning", meaning),
        };
        if (value > 1)
        {
            fields.Add(new DecodedField("Note", Invariant($"value {value} is not 0 or 1; treated as set")));
        }

        if (error is not null)
        {
            fields.Add(new DecodedField("Error", error));
        }

        string summary = Invariant($"{name}={value}: {meaning}");
        object json = new { Name = name, Value = (int?)value, Set = (bool?)set, Error = error };
        return new DecodedVariable(variable.Key, Kind, summary, fields, json, error);
    }

    private static DecodedVariable DecodeDatabase(FirmwareVariable variable, byte[] data)
    {
        string name = variable.Name;
        SignatureDbInfo info = SignatureListReader.Read(data);
        string types = info.SignatureTypes.Count == 0 ? "none" : string.Join(", ", info.SignatureTypes);

        List<DecodedField> fields = new()
        {
            new DecodedField("Size", Plural(info.Size, "byte")),
            new DecodedField("Signature lists", Invariant($"{info.ListCount}")),
            new DecodedField("Signatures", Invariant($"{info.SignatureCount}")),
            new DecodedField("Signature types", types),
        };
        if (info.Error is not null)
        {
            fields.Add(new DecodedField("Error", info.Error));
        }

        string summary = data.Length == 0
            ? Invariant($"{name}: present but empty (0 bytes)")
            : Invariant($"{name}: {Plural(info.Size, "byte")}, {Plural(info.ListCount, "signature list")}, {Plural(info.SignatureCount, "signature")} ({types})");
        if (info.Error is not null)
        {
            summary += "; malformed: " + info.Error;
        }

        object json = new { Name = name, info.Size, info.ListCount, info.SignatureTypes, info.SignatureCount, info.Error };
        return new DecodedVariable(variable.Key, Kind, summary, fields, json, info.Error);
    }
}
