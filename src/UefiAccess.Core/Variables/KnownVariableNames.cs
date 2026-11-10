// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Core.Variables;

/// <summary>
/// The curated list of variable names read one by one when enumeration is unavailable, plus
/// helpers for the Boot#### naming convention.
/// </summary>
public static class KnownVariableNames
{
    /// <summary>
    /// Fixed names with their vendor GUIDs: the EFI global variables followed by the image security
    /// databases. Boot#### entries are probed separately from 0000 upward until three consecutive misses.
    /// </summary>
    public static IReadOnlyList<(string Name, Guid Guid)> Fixed { get; } = new[]
    {
        ("BootOrder", KnownGuids.EfiGlobalVariable),
        ("BootNext", KnownGuids.EfiGlobalVariable),
        ("BootCurrent", KnownGuids.EfiGlobalVariable),
        ("Timeout", KnownGuids.EfiGlobalVariable),
        ("SecureBoot", KnownGuids.EfiGlobalVariable),
        ("SetupMode", KnownGuids.EfiGlobalVariable),
        ("AuditMode", KnownGuids.EfiGlobalVariable),
        ("DeployedMode", KnownGuids.EfiGlobalVariable),
        ("PK", KnownGuids.EfiGlobalVariable),
        ("KEK", KnownGuids.EfiGlobalVariable),
        ("OsIndications", KnownGuids.EfiGlobalVariable),
        ("OsIndicationsSupported", KnownGuids.EfiGlobalVariable),
        ("PlatformLang", KnownGuids.EfiGlobalVariable),
        ("ConIn", KnownGuids.EfiGlobalVariable),
        ("ConOut", KnownGuids.EfiGlobalVariable),
        ("ErrOut", KnownGuids.EfiGlobalVariable),
        ("db", KnownGuids.EfiImageSecurityDatabase),
        ("dbx", KnownGuids.EfiImageSecurityDatabase),
        ("dbt", KnownGuids.EfiImageSecurityDatabase),
    };

    /// <summary>Builds a Boot#### name such as "Boot0001". The index is masked to 16 bits.</summary>
    /// <param name="index">The boot option number.</param>
    /// <returns>"Boot" followed by four uppercase hexadecimal digits.</returns>
    public static string BootName(int index) => "Boot" + (index & 0xFFFF).ToString("X4", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Tests whether a name is exactly "Boot" followed by four uppercase hexadecimal digits
    /// ("Boot0001" matches; "boot0001", "Boot001" and "Boot00G1" do not).
    /// </summary>
    /// <param name="name">The name to test.</param>
    /// <param name="index">Receives the option number when the name matches; otherwise -1.</param>
    /// <returns>True when the name is a Boot#### entry name.</returns>
    public static bool IsBootEntryName(string name, out int index)
    {
        index = -1;
        if (name is null || name.Length != 8 || !name.StartsWith("Boot", StringComparison.Ordinal))
        {
            return false;
        }

        int value = 0;
        for (int i = 4; i < 8; i++)
        {
            char c = name[i];
            int digit = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'A' and <= 'F' => c - 'A' + 10,
                _ => -1,
            };
            if (digit < 0)
            {
                return false;
            }

            value = (value << 4) | digit;
        }

        index = value;
        return true;
    }
}
