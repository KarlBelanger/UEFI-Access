// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Cryptography;
using System.Text;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Variables;
using static UefiAccess.Core.Tests.Fixtures.LoadOptionFixtures;

namespace UefiAccess.Core.Tests.Safety;

/// <summary>
/// A variable store shaped like the unit plus the entries the rules
/// need (section 7.1). Boot0002 and Boot0000 are the unit's own bytes, 300 each, copied from the
/// Boot#### files of the reference baseline 20260923-111550 (tools/sample-baseline is built from
/// the synthetic fixtures and holds neither): Boot0002 is the Windows Boot Manager on partition 1
/// and is BootCurrent; Boot0000 is a second Windows Boot Manager on partition 2 and is not in
/// BootOrder. BootOrder holds Boot0002 and a USB stick, Boot0003. The other entries are one of
/// each kind a rule looks at, built from <see cref="Fixtures.LoadOptionFixtures"/>. A load option
/// carries no machine identity, and no fixture carries a service tag.
/// </summary>
internal static class SafetyFixtures
{
    public static readonly Guid Global = KnownGuids.EfiGlobalVariable;

    /// <summary>Dell's vendor GUID for BootFFFB to BootFFFE (section 1a).</summary>
    public static readonly Guid DellBootGuid = new("5990c250-676b-4ff7-8a0d-529319d0b254");

    /// <summary>The GPT partition GUID of the unit's EFI system partition, partition 1, which Boot0002 names.</summary>
    public static readonly Guid UnitEspSignature = new("45d24df4-90d6-4580-9dd9-75a19f842203");

    /// <summary>The GPT partition GUID of the unit's partition 2, which Boot0000 names.</summary>
    public static readonly Guid UnitSecondPartitionSignature = new("3ee6207e-24b9-4724-899e-ffc5da70d55d");

    /// <summary>The partition signature of the removable-fallback loader on a third partition (Boot0007).</summary>
    public static readonly Guid ThirdPartitionSignature = new("7a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d");

    public const VariableAttributes NvBsRt = VariableAttributes.NonVolatile | VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess;
    public const VariableAttributes BsRt = VariableAttributes.BootServiceAccess | VariableAttributes.RuntimeAccess;
    public const VariableAttributes NvBsRtTa = NvBsRt | VariableAttributes.TimeBasedAuthenticatedWriteAccess;
    public const VariableAttributes BsRtAw = BsRt | VariableAttributes.AuthenticatedWriteAccess;

    public const ushort WindowsOnPartition1 = 0x0002;
    public const ushort WindowsOnPartition2 = 0x0000;
    public const ushort UsbNumber = 0x0003;
    public const ushort AppNumber = 0x0004;
    public const ushort HiddenNumber = 0x0005;
    public const ushort InactiveNumber = 0x0006;
    public const ushort FallbackNumber = 0x0007;
    public const ushort MalformedNumber = 0x0008;
    public const ushort WindowsCopyNumber = 0x0009;
    public const ushort FallbackSamePartitionNumber = 0x000A;
    public const ushort WindowsWithoutPartitionNumber = 0x000B;
    public const ushort PartitionWithoutFileNumber = 0x000C;
    public const ushort NetworkNumber = 0x000D;
    public const ushort MissingNumber = 0x0010;

    public static readonly DateTimeOffset PlanTime = new(2026, 9, 30, 14, 15, 2, TimeSpan.Zero);

    /// <summary>
    /// A made-up Windows machine ID the tests treat as the M3 test unit's. It is no real machine's:
    /// no test reads the registry, and none depends on the real unit's constant
    /// (<see cref="PlatformPolicy.EnabledUnitMachineIdSha256"/>).
    /// </summary>
    public const string TestUnitMachineId = "3f2504e0-4f89-41d3-9a0c-0305e82c3301";

    /// <summary>Another made-up machine ID: another OptiPlex 7020 on BIOS A18 (the tester's case).</summary>
    public const string OtherUnitMachineId = "7c9e6679-7425-40de-944b-e07fc1f90ae7";

    /// <summary>
    /// The hash the tests' unit check accepts: the SHA-256 of the UTF-8 bytes of
    /// <see cref="TestUnitMachineId"/> (already in its canonical form), computed here without the
    /// code under test.
    /// </summary>
    public static readonly string TestUnitMachineIdSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(TestUnitMachineId))).ToLowerInvariant();

    /// <summary>The unit's Boot0002, "Windows Boot Manager", HD(1,GPT,45d24df4-…,0x800,0x32000)/File(\EFI\Microsoft\Boot\bootmgfw.efi), with Windows' optional data.</summary>
    private const string UnitBoot0002Hex =
        "010000007400570069006e0064006f0077007300200042006f006f00740020004d0061006e00610067006500720000000401"
        + "2a000100000000080000000000000020030000000000f44dd245d69080459dd975a19f8422030202040446005c0045004600"
        + "49005c004d006900630072006f0073006f00660074005c0042006f006f0074005c0062006f006f0074006d00670066007700"
        + "2e0065006600690000007fff040057494e444f5753000100000088000000780000004200430044004f0042004a0045004300"
        + "54003d007b00390064006500610038003600320063002d0035006300640064002d0034006500370030002d00610063006300"
        + "31002d006600330032006200330034003400640034003700390035007d00000000000100000010000000040000007fff0400";

    /// <summary>The unit's Boot0000, "Windows Boot Manager", HD(2,GPT,3ee6207e-…,0x96800,0x32000)/File(\EFI\Microsoft\Boot\bootmgfw.efi), left by an earlier install.</summary>
    private const string UnitBoot0000Hex =
        "010000007400570069006e0064006f0077007300200042006f006f00740020004d0061006e00610067006500720000000401"
        + "2a0002000000006809000000000000200300000000007e20e63eb9242447899effc5da70d55d0202040446005c0045004600"
        + "49005c004d006900630072006f0073006f00660074005c0042006f006f0074005c0062006f006f0074006d00670066007700"
        + "2e0065006600690000007fff040057494e444f5753000100000088000000780000004200430044004f0042004a0045004300"
        + "54003d007b00390064006500610038003600320063002d0035006300640064002d0034006500370030002d00610063006300"
        + "31002d006600330032006200330034003400640034003700390035007d00000073000100000010000000040000007fff0400";

    public static FirmwareVariable Var(string name, byte[] data, VariableAttributes attributes = NvBsRt) => new(name, Global, attributes, data);

    /// <summary>The unit's Boot0002 as read on 23 September 2026 (300 bytes).</summary>
    public static byte[] UnitBoot0002() => Convert.FromHexString(UnitBoot0002Hex);

    /// <summary>The unit's Boot0000 as read on 23 September 2026 (300 bytes).</summary>
    public static byte[] UnitBoot0000() => Convert.FromHexString(UnitBoot0000Hex);

    /// <summary>The removable-media fallback loader on a third partition: "a bootx64.efi entry on another partition".</summary>
    public static byte[] FallbackLoader()
    {
        return BuildLoadOption(1, "UEFI OS", PathOf(HardDriveGpt(3, 0x100000, 0x32000, ThirdPartitionSignature), FilePath(@"\EFI\Boot\bootx64.efi")));
    }

    /// <summary>The removable-media fallback loader on the unit's own system partition, spelled in capitals as FAT allows.</summary>
    public static byte[] FallbackSamePartition()
    {
        return BuildLoadOption(1, "UEFI OS (system partition)", PathOf(HardDriveGpt(1, 0x800, 0x32000, UnitEspSignature), FilePath(@"\EFI\BOOT\BOOTX64.EFI")));
    }

    /// <summary>A second entry for the same boot manager on the unit's system partition, spelled in capitals as FAT allows.</summary>
    public static byte[] WindowsCopySamePartition()
    {
        return BuildLoadOption(1, "Windows Boot Manager (copy)", PathOf(HardDriveGpt(1, 0x800, 0x32000, UnitEspSignature), FilePath(@"\EFI\MICROSOFT\BOOT\BOOTMGFW.EFI")));
    }

    /// <summary>The boot manager named by a short-form path with no hard drive node: whatever disk the firmware finds it on.</summary>
    public static byte[] WindowsWithoutPartition() => BuildLoadOption(1, "Windows Boot Manager (short form)", PathOf(FilePath(@"\EFI\Microsoft\Boot\bootmgfw.efi")));

    /// <summary>Partition 2 named without a file: the boot manager loads the removable-media fallback from it.</summary>
    public static byte[] PartitionWithoutFile() => BuildLoadOption(1, "UEFI: partition 2", PathOf(HardDriveGpt(2, 0x96800, 0x32000, UnitSecondPartitionSignature)));

    public static byte[] AppEntry() => BuildLoadOption(0x101, "Shell", PathOf(FvFile(UnitEspSignature)));

    public static byte[] HiddenEntry() => BuildLoadOption(0x9, "Hidden tool", PathOf(HardDriveGpt(1, 0x800, 0x32000, UnitEspSignature), FilePath(@"\EFI\tools\tool.efi")));

    public static byte[] InactiveEntry() => BuildLoadOption(0, "Old system", PathOf(HardDriveGpt(1, 0x800, 0x32000, UnitEspSignature), FilePath(@"\EFI\old\loader.efi")));

    public static byte[] MalformedEntry() => new byte[] { 1, 0, 0, 0, 0, 0, 0x41 };

    /// <summary>Every variable of the default store except BootOrder, which <see cref="Store"/> adds.</summary>
    public static List<FirmwareVariable> Entries() => new()
    {
        Var("Boot0002", UnitBoot0002()),
        Var("Boot0000", UnitBoot0000()),
        Var("Boot0003", UsbStick()),
        Var("Boot0004", AppEntry()),
        Var("Boot0005", HiddenEntry()),
        Var("Boot0006", InactiveEntry()),
        Var("Boot0007", FallbackLoader()),
        Var("Boot0008", MalformedEntry()),
        Var("Boot0009", WindowsCopySamePartition()),
        Var("Boot000A", FallbackSamePartition()),
        Var("Boot000B", WindowsWithoutPartition()),
        Var("Boot000C", PartitionWithoutFile()),
        Var("Boot000D", Pxe()),
        Var("BootCurrent", U16(WindowsOnPartition1), BsRt),
        Var("Timeout", U16(0)),
        Var("PK", new byte[] { 0xA0, 0x59, 0xC1, 0x26 }, NvBsRtTa),
        Var("SecureBoot", new byte[] { 1 }, BsRtAw),
        new FirmwareVariable("db", KnownGuids.EfiImageSecurityDatabase, NvBsRtTa, new byte[] { 0xA0, 0x59 }),
        new FirmwareVariable("BootFFFB", DellBootGuid, NvBsRt, UsbStick()),
    };

    /// <summary>
    /// The default store: <see cref="Entries"/> with BootOrder [Boot0002, Boot0003]. Each override
    /// replaces the variable of the same name and GUID or adds one.
    /// </summary>
    public static VariableSet Store(params FirmwareVariable[] overrides)
    {
        Dictionary<(Guid Guid, string Name), FirmwareVariable> byKey = Entries().ToDictionary(v => (v.VendorGuid, v.Name));
        byKey[(Global, "BootOrder")] = Var("BootOrder", Concat(U16(WindowsOnPartition1), U16(UsbNumber)));
        foreach (FirmwareVariable variable in overrides)
        {
            byKey[(variable.VendorGuid, variable.Name)] = variable;
        }

        return new VariableSet(byKey.Values);
    }

    /// <summary>The default store with BootOrder set to the given entry numbers.</summary>
    public static VariableSet StoreWithOrder(params ushort[] order) => Store(Var("BootOrder", BootList(order)));

    /// <summary>A boot list's bytes: the entry numbers as little-endian UINT16s.</summary>
    public static byte[] BootList(params ushort[] entries) => Concat(entries.Select(U16).ToArray());

    /// <summary>The store without the named global variables.</summary>
    public static VariableSet Without(this VariableSet set, params string[] names)
    {
        return new VariableSet(set.All.Where(v => !(v.VendorGuid == Global && names.Contains(v.Name, StringComparer.Ordinal))));
    }

    /// <summary>A complete enumeration of the set: <see cref="EnumerationMethod.NtEnumerate"/>, no error, no warning (rule V15 accepts nothing else).</summary>
    public static EnumerationResult Complete(VariableSet set) => NtComplete(set);

    public static EnumerationResult NtComplete(VariableSet set) => new(set.All, EnumerationMethod.NtEnumerate, set.Count, null, Array.Empty<string>());

    /// <summary>The set as a baseline folder or an in-memory store reports it: no error and no warning, but not read from the firmware.</summary>
    public static EnumerationResult InMemory(VariableSet set) => new(set.All, EnumerationMethod.InMemory, set.Count, null, Array.Empty<string>());

    public static EnumerationResult Fallback(VariableSet set) => new(set.All, EnumerationMethod.KnownNamesFallback, set.Count, "NtEnumerateSystemEnvironmentValuesEx failed", Array.Empty<string>());

    public static EnumerationResult WithWarning(VariableSet set) => new(set.All, EnumerationMethod.NtEnumerate, set.Count, null, new[] { "one entry could not be parsed" });

    public static EnumerationResult StoppedEarly(VariableSet set) => new(set.All, EnumerationMethod.NtEnumerate, set.Count, "the buffer ended inside an entry", Array.Empty<string>());

    public static ReorderRequest Reorder(params ushort[] order) => new(ChangeRequest.CliHost, order);

    public static BootNextRequest Next(ushort target) => new(ChangeRequest.CliHost, target);

    public static ClearBootNextRequest Clear() => new(ChangeRequest.CliHost);

    public static CheckRequest Check(string name, Guid? guid = null) => new(ChangeRequest.CliHost, name, guid ?? Global);

    public static ValidationResult Validate(ChangeRequest request, VariableSet set, BitLockerState state = BitLockerState.On)
    {
        return ChangeValidator.Validate(request, set, Complete(set), state);
    }

    public static MachineIdentityResult Identity(bool confirmed)
    {
        return new MachineIdentityResult(
            "Dell Inc.",
            confirmed ? "OptiPlex 7020" : "Other model",
            null,
            confirmed ? "A18" : "1.0",
            "2019-05-30",
            null,
            null,
            "Intel(R) Core(TM) i5-4590",
            "Microsoft Windows 11 Pro",
            "10.0.26200",
            "26200",
            confirmed,
            confirmed ? "matches the planned 2014 OptiPlex 7020" : "not the planned model",
            Array.Empty<string>());
    }

    /// <summary>
    /// The unit's BitLocker state as the M1 inspector reads it (section 1a: on, protectors "TPM"
    /// and "Numerical password" as the inspector names them, PCR 0, 2, 4, 11), on this host's OS
    /// volume and assessed through <see cref="BitLockerPreflight.Assess(BitLockerResult)"/>, the
    /// same facts the transaction tests use (<see cref="TransactionFakes.BitLockerOn"/>). The plan
    /// stores the assessment and never reads it.
    /// </summary>
    public static BitLockerAssessment UnitBitLocker()
    {
        return BitLockerPreflight.Assess(TransactionFakes.BitLockerOn());
    }

    /// <summary>Policy findings for a plan test; the unit check agrees with <paramref name="machineConfirmed"/>.</summary>
    public static PolicyFindings Findings(bool machineConfirmed = true)
    {
        return new PolicyFindings(null, Identity(machineConfirmed), machineConfirmed, UnitBitLocker(), null, null, Array.Empty<string>())
        {
            Unit = machineConfirmed ? UnitMatch.Confirmed : UnitMatch.OtherUnit,
        };
    }

    /// <summary>
    /// A unit check over a fixed machine ID that accepts <see cref="TestUnitMachineId"/>: the
    /// default is the test unit, <see cref="OtherUnitMachineId"/> is another OptiPlex 7020, and
    /// null or text that is not a GUID is a machine ID that could not be read.
    /// </summary>
    /// <param name="machineId">What the seam returns.</param>
    /// <returns>The check; it never reads the registry.</returns>
    public static EnabledUnit Unit(string? machineId = TestUnitMachineId)
    {
        return new EnabledUnit(() => machineId, TestUnitMachineIdSha256);
    }
}
