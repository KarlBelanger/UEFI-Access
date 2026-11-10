// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Platform;

namespace UefiAccess.Core.Tests.Fixtures;

/// <summary>
/// Dell Command | Monitor rows as WmiHelper.Query returns them (one case-insensitive dictionary
/// per DCIM_BIOSEnumeration instance), typed in code for the Dell settings tests. <see cref="UnitA18"/> holds the 72 attributes of the export of 23 September 2026
/// (BIOS A18, Dell Command | Monitor 10.13.2.56): each attribute's name, current codes, possible
/// codes and their names, nothing else; that export holds no identity, and the string class's file
/// beside it is not used. IsReadOnly was not in the export. It follows Dell's conversion rule for
/// enumeration-type attributes (deviation D8: true only for a name that contains "Computrace" or
/// "Trusted Platform Module Clear"); the integer-type rows (a range, or a minimum, maximum and
/// step, as Auto On Hour) carry the BIOS's own flag, which was not captured either, so they hold
/// false as a stand-in and no test asserts it. The variants change one thing each.
/// </summary>
internal static class DcmRowFixtures
{
    /// <summary>The name <see cref="WithUnmappedName"/> gives the integrated NIC's current value.</summary>
    public const string UnmappedNicName = "Enabled w/Cloud Desktop";

    /// <summary>The unit's current values of the ten agreed rows on 23 September 2026, as the BIOS setup words them.</summary>
    public static readonly string[] UnitA18Texts = { "UEFI", "Enabled", "AHCI", "Enabled", "Activated", "Enabled", "Enabled", "Enabled", "Disabled", "Enabled" };

    /// <summary>The 72 rows of the A18 export, in the export's order.</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> UnitA18()
    {
        return new[]
        {
            Row("AC Power Recovery Mode", Texts("1"), Texts("1", "2", "3"), Texts("Off", "Last", "On")),
            Row("Active State Power Management", Texts("1"), Texts("1", "2", "3"), Texts("Auto", "Disable", "L1 Only")),
            Row("Admin Setup Lockout", Texts("1"), Texts("1", "2"), Texts("Disable", "Enable")),
            Row("Allow BIOS Downgrade", Texts("1"), Texts("1", "2"), Texts("Enable", "Disable")),
            Row("Auto On", Texts("1"), Texts("1", "2", "3", "4"), Texts("Disable", "Everyday", "Weekdays", "Select days")),
            Row("Auto on Friday", Texts("2"), Texts("1", "2"), Texts("Enable", "Disable")),
            IntegerRow("Auto On Hour", Texts("0"), Texts("0-23"), Texts("0-23")),
            IntegerRow("Auto On Minute", Texts("0"), Texts("0-59"), Texts("0-59")),
            Row("Auto on Monday", Texts("2"), Texts("1", "2"), Texts("Enable", "Disable")),
            Row("Auto on Saturday", Texts("2"), Texts("1", "2"), Texts("Enable", "Disable")),
            Row("Auto on Sunday", Texts("2"), Texts("1", "2"), Texts("Enable", "Disable")),
            Row("Auto on Thursday", Texts("2"), Texts("1", "2"), Texts("Enable", "Disable")),
            Row("Auto on Tuesday", Texts("2"), Texts("1", "2"), Texts("Enable", "Disable")),
            Row("Auto on Wednesday", Texts("2"), Texts("1", "2"), Texts("Enable", "Disable")),
            Row("BlockS3", Texts("1"), Texts("1", "2"), Texts("Disable", "Enable")),
            Row("Boot Mode", Texts("2"), Texts("1", "2"), Texts("BIOS", "UEFI")),
            Row("Chassis Intrusion", Texts("1"), Texts("1", "2", "3"), Texts("Disable", "Enable", "Silent")),
            Row("Chassis Intrusion Status", Texts("3"), Texts("1", "2", "3", "4"), Texts("Tripped", "Door open", "Door closed", "Trip reset")),
            Row("Clear System Event Log", Texts("2"), Texts("1", "2"), Texts("Disable", "Enable")),
            Row("Computrace", Texts("1"), Texts("1", "2"), Texts("Disable", "Enable")),
            Row("Deep Sleep Control", Texts("1"), Texts("1", "2", "3"), Texts("S4andS5", "Disable", "S5Only")),
            Row("Embedded SATA Controller", Texts("4"), Texts("1", "3", "4"), Texts("Off", "ATA", "AHCI")),
            Row("Enable Legacy Option ROMs", Texts("1"), Texts("1", "2"), Texts("Disable", "Enable")),
            Row("Fan Control override", Texts("2"), Texts("1", "2"), Texts("Enable", "Disable")),
            Row("Force PXE on Next Boot", Texts("1"), Texts("1", "2"), Texts("Disable", "Enable")),
            Row("Front Panel USB Ports", Texts("2"), Texts("1", "2"), Texts("Disable", "Enable")),
            Row("Front USB 3 Port", Texts("2"), Texts("1", "2"), Texts("Disable", "Enable")),
            Row("HDD Protection", Texts("1"), Texts("1", "2"), Texts("Off", "On")),
            Row("Intel Rapid Start Technology", Texts("2"), Texts("1", "2"), Texts("Enable", "Disable")),
            IntegerRow("Intel Rapid Start Timer", Texts("30"), Texts("0", "999", "1"), Texts("Minimum Timer Value", "Maximum Timer Value", "Granularity")),
            Row("Intel Smart Connect Technology", Texts("2"), Texts("1", "2"), Texts("Enable", "Disable")),
            Row("Keyboard Error Reporting", Texts("1"), Texts("1", "2"), Texts("Disable", "Enable")),
            Row("Limit CPUID Value", Texts("1"), Texts("1", "2"), Texts("Disable", "Enable")),
            Row("Master Password Lockout", Texts("2"), Texts("1", "2"), Texts("Enable", "Disable")),
            Row("NIC", Texts("3"), Texts("1", "2", "3", "6"), Texts("Disable", "EnabledwithPXE", "Enable", "Enable image server")),
            Row("Num Lock", Texts("2"), Texts("1", "2"), Texts("Disable", "Enable")),
            Row("OROM Keyboard Access", Texts("2"), Texts("1", "2", "3"), Texts("Disable", "Enable", "One Time Enable")),
            Row("Password Bypass", Texts("1"), Texts("1", "2"), Texts("Off", "Reboot")),
            Row("Password Status Lock", Texts("2"), Texts("1", "2"), Texts("Locked", "Unlocked")),
            Row("POST MEBx Key Setting", Texts("2"), Texts("1", "2"), Texts("Off", "On")),
            IntegerRow("Primary Video Device Slot", Texts("0"), Texts("0", "1-15", "255"), Texts("Onboard video", "Slot number of Primary video device", "Auto")),
            Row("Processor C State Control", Texts("1"), Texts("1", "2"), Texts("Enable", "Disable")),
            Row("Processor CMP", Texts("1"), Texts("1", "2", "3", "4"), Texts("All", "1", "2", "4")),
            Row("Processor Core Based Turbo Mode", Texts("2"), Texts("1", "2"), Texts("Disable", "Enable")),
            Row("Processor Execute Disable", Texts("2"), Texts("1", "2"), Texts("Disable", "Enable")),
            Row("Processor Logical Processor (HyperThreading)", Texts("1"), Texts("1", "2"), Texts("Enable", "Disable")),
            Row("Processor Virtualization Technology", Texts("2"), Texts("1", "2"), Texts("Disable", "Enable")),
            Row("Rear Dual USB", Texts("2"), Texts("1", "2"), Texts("Disable", "Enable")),
            Row("Rear Dual USB 2nd stack", Texts("2"), Texts("1", "2"), Texts("Disable", "Enable")),
            Row("Rear USB 3 Port", Texts("2"), Texts("1", "2"), Texts("Disable", "Enable")),
            Row("SATA Port 0", Texts("2"), Texts("1", "2"), Texts("Off", "Auto")),
            Row("Secure Boot", Texts("2"), Texts("2"), Texts("Enable")),
            Row("Serial Port 1", Texts("3"), Texts("1", "3", "4", "5", "6"), Texts("Disable", "COM1", "COM2", "COM3", "COM4")),
            Row("SERR DMI Message", Texts("2"), Texts("1", "2"), Texts("Off", "On")),
            Row("Signed Firmware Update", Texts("1"), Texts("1", "2"), Texts("Enable", "Disable")),
            Row("SMART Errors", Texts("1"), Texts("1", "2"), Texts("Disable", "Enable")),
            Row("Sound Device", Texts("2"), Texts("1", "2"), Texts("Disable", "Enable")),
            Row("SpeedStep", Texts("4"), Texts("1", "4"), Texts("Disable", "Auto")),
            Row("Strong Password", Texts("1"), Texts("1", "2"), Texts("Disable", "Enable")),
            Row("TPM PPI ACPI Support", Texts("1"), Texts("1", "2"), Texts("Enable", "Disable")),
            Row("TPM PPI Deprovision Override", Texts("2"), Texts("1", "2"), Texts("Enable", "Disable")),
            Row("TPM PPI Provision Override", Texts("2"), Texts("1", "2"), Texts("Enable", "Disable")),
            Row("Trusted Execution", Texts("2"), Texts("1", "2"), Texts("On", "Off")),
            Row("Trusted Platform Module", Texts("1"), Texts("1", "2"), Texts("Enable", "Disable")),
            Row("Trusted Platform Module Activation", Texts("2"), Texts("1", "2"), Texts("Deactivate", "Activate")),
            Row("Uefi Boot Path Security", Texts("2"), Texts("0", "1", "2", "3"), Texts("Never", "Always", "Always Except Internal HDD", "Always Except Internal HDD and PXE")),
            Row("UEFI Network Stack", Texts("1"), Texts("1", "2"), Texts("Enable", "Disable")),
            Row("USB 3.0", Texts("2"), Texts("1", "2"), Texts("Disable", "Enable")),
            Row("USB Emulation", Texts("1"), Texts("1", "3"), Texts("Enable", "No boot")),
            Row("USB Wake Support", Texts("1"), Texts("1", "2"), Texts("Disable", "Enable")),
            Row("VT for Direct IO", Texts("2"), Texts("1", "2"), Texts("Disable", "Enable")),
            Row("Wake-On-LAN", Texts("1"), Texts("1", "4", "7"), Texts("Disable", "LAN", "LAN with PXE Boot")),
        };
    }

    /// <summary>Secure Boot while it is off: Dell Command | Monitor leaves CurrentValue out (DF13).</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> WithoutSecureBootCurrent() => Change("Secure Boot", row => row["CurrentValue"] = null);

    /// <summary>
    /// Every attribute with its codes in the opposite order and the current codes moved with them,
    /// so each name keeps its meaning while every code of more than one means something else.
    /// </summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> WithSwappedCodes()
    {
        return UnitA18().Select(original =>
        {
            Dictionary<string, object?> row = Copy(original);
            if (row["PossibleValues"] is string[] codes)
            {
                string[] swapped = codes.Reverse().ToArray();
                row["PossibleValues"] = swapped;
                if (row["CurrentValue"] is string[] current)
                {
                    row["CurrentValue"] = current.Select(code => Array.IndexOf(codes, code) is int index and >= 0 ? swapped[index] : code).ToArray();
                }
            }

            return (IReadOnlyDictionary<string, object?>)row;
        }).ToArray();
    }

    /// <summary>Num Lock with a current code that has no name (DF11).</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> WithUnknownCode() => Change("Num Lock", row => row["CurrentValue"] = Texts("9"));

    /// <summary>Num Lock with two current codes, both named (synthetic: the class allows several, the unit reports one).</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> WithSeveralCodes() => Change("Num Lock", row => row["CurrentValue"] = Texts("1", "2"));

    /// <summary>The integrated NIC's current value under a name the catalog does not know (DF12).</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> WithUnmappedName() =>
        Change("NIC", row => row["PossibleValuesDescription"] = Texts("Disable", "EnabledwithPXE", UnmappedNicName, "Enable image server"));

    /// <summary>Without the Num Lock row (DF10).</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> WithoutNumLock() =>
        UnitA18().Where(row => !string.Equals((string?)row["AttributeName"], "Num Lock", StringComparison.Ordinal)).ToArray();

    /// <summary>TPM security disabled (code 2, "Disable"), with TPM activation still reported as activated.</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> WithTpmDisabled() => Change("Trusted Platform Module", row => row["CurrentValue"] = Texts("2"));

    /// <summary>The rows as an answered bounded query.</summary>
    public static DcmQueryOutcome Answer(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) => new(new WmiQueryResult(rows, null, false), false);

    /// <summary>One enumeration-type row, its IsReadOnly by Dell's conversion rule (deviation D8).</summary>
    public static IReadOnlyDictionary<string, object?> Row(string name, string[]? current, string[]? possible, string[]? names)
    {
        bool readOnly = name.Contains("Computrace", StringComparison.Ordinal) || name.Contains("Trusted Platform Module Clear", StringComparison.Ordinal);
        return Make(name, current, possible, names, readOnly);
    }

    /// <summary>Text arrays as WMI returns them.</summary>
    public static string[] Texts(params string[] texts) => texts;

    /// <summary>One integer-type row; false stands in for the BIOS's own flag, which was not captured.</summary>
    private static IReadOnlyDictionary<string, object?> IntegerRow(string name, string[] current, string[] possible, string[] names) => Make(name, current, possible, names, false);

    private static IReadOnlyDictionary<string, object?> Make(string name, string[]? current, string[]? possible, string[]? names, bool readOnly)
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["AttributeName"] = name,
            ["CurrentValue"] = current,
            ["PossibleValues"] = possible,
            ["PossibleValuesDescription"] = names,
            ["IsReadOnly"] = readOnly,
        };
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> Change(string name, Action<Dictionary<string, object?>> change)
    {
        bool found = false;
        IReadOnlyDictionary<string, object?>[] rows = UnitA18().Select(original =>
        {
            Dictionary<string, object?> row = Copy(original);
            if (string.Equals((string?)row["AttributeName"], name, StringComparison.Ordinal))
            {
                change(row);
                found = true;
            }

            return (IReadOnlyDictionary<string, object?>)row;
        }).ToArray();
        if (!found)
        {
            throw new InvalidOperationException("no fixture row named " + name);
        }

        return rows;
    }

    private static Dictionary<string, object?> Copy(IReadOnlyDictionary<string, object?> row) => new(row, StringComparer.OrdinalIgnoreCase);
}
