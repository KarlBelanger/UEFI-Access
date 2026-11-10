// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Settings.Dell;

namespace UefiAccess.Core.Tests.Fixtures;

/// <summary>
/// What Dell Command | Configure prints for one read option, and the outcome of one run as the
/// fallback receives it, typed in code for its tests. Only the Secure
/// Boot lines were seen in the M1 evidence (5.2.2 printed "SecureBoot=Enabled", a 3.x version
/// "secureboot=enable"); every other line follows syscfg.xml of 5.2.2, whose values are written as
/// New[legacy] pairs. Protocol step M4-H2 (4 October 2026, cctk 5.2.2.292, BIOS A18) printed the
/// lines SecureBoot=Enabled, EmbSataRaid=Ahci, TpmSecurity=Enabled, TpmActivation=Enabled,
/// Virtualization=Enabled, VtForDirectIo=Enabled, EmbNic1=Enabled, WakeOnLan=Disabled and
/// NumLock=Enabled on the unit, each the only line of its run. The banner is synthetic.
/// No output here holds an identity.
/// </summary>
internal static class CctkOutputFixtures
{
    /// <summary>Secure Boot in the 5.2.2 words, as the M1 probe saw it on the unit.</summary>
    public const string SecureBootNew = "SecureBoot=Enabled";

    /// <summary>Secure Boot in the legacy words of the 3.x versions, as the M1 evidence holds it.</summary>
    public const string SecureBootLegacy = "secureboot=enable";

    /// <summary>Num Lock with the line break a console program ends its line with.</summary>
    public const string NumLockWithLineBreak = "NumLock=Enabled\r\n";

    /// <summary>TPM activation as 5.2.2 is expected to print it for an activated TPM (syscfg.xml pairs Enabled with the legacy activate).</summary>
    public const string TpmActivationEnabled = "TpmActivation=Enabled";

    /// <summary>Wake on LAN in the legacy word for LAN Only.</summary>
    public const string WakeOnLanLegacyEnable = "wakeonlan=enable";

    /// <summary>The integrated NIC in the legacy word for Enabled with PXE.</summary>
    public const string NicOn = "EmbNic1=on";

    /// <summary>A banner (synthetic), a line of another name that holds an "=" too and a blank line before the value line.</summary>
    public const string BannerThenNumLock = "Dell Command Configure 5.2.2.292 (synthetic banner)\r\nCopyright Dell Inc.\r\nMode=read\r\n\r\nNumLock=Enabled";

    /// <summary>No output at all.</summary>
    public const string Empty = "";

    /// <summary>Two lines that both name Num Lock, one in each vocabulary.</summary>
    public const string TwoNumLockLines = "NumLock=Enabled\r\nnumlock=disable";

    /// <summary>The time every scripted run took, in milliseconds.</summary>
    public const double ElapsedMilliseconds = 12;

    /// <summary>
    /// The line each of the nine read options is expected to print on the unit, in the 5.2.2
    /// words, for the values of the A18 export of 23 September 2026: Secure Boot Enabled, SATA
    /// AHCI, TPM security Enabled, TPM activation Activated, Virtualization Enabled, VT for Direct
    /// I/O Enabled, the NIC Enabled, Wake on LAN Disabled, Num Lock Enabled.
    /// </summary>
    /// <param name="option">The read option.</param>
    /// <returns>The value line.</returns>
    public static string UnitA18Output(CctkReadOption option) => option switch
    {
        CctkReadOption.SecureBoot => SecureBootNew,
        CctkReadOption.SataOperation => "EmbSataRaid=Ahci",
        CctkReadOption.TpmSecurity => "TpmSecurity=Enabled",
        CctkReadOption.TpmActivation => TpmActivationEnabled,
        CctkReadOption.Virtualization => "Virtualization=Enabled",
        CctkReadOption.VtForDirectIo => "VtForDirectIo=Enabled",
        CctkReadOption.IntegratedNic => "EmbNic1=Enabled",
        CctkReadOption.WakeOnLan => "WakeOnLan=Disabled",
        CctkReadOption.NumLock => "NumLock=Enabled",
        _ => throw new ArgumentOutOfRangeException(nameof(option)),
    };

    /// <summary>A run that ended with exit code 0 and printed <paramref name="output"/>.</summary>
    /// <param name="option">The read option.</param>
    /// <param name="output">The standard output.</param>
    /// <returns>The outcome.</returns>
    public static CctkRunOutcome Answer(CctkReadOption option, string output) => Exit(option, 0, output);

    /// <summary>A run that ended with an exit code.</summary>
    /// <param name="option">The read option.</param>
    /// <param name="exitCode">The exit code.</param>
    /// <param name="output">The standard output.</param>
    /// <param name="error">The standard error.</param>
    /// <returns>The outcome.</returns>
    public static CctkRunOutcome Exit(CctkReadOption option, int exitCode, string output = "", string error = "") =>
        new(option, CctkReader.ArgumentFor(option), true, exitCode, output, error, false, null, ElapsedMilliseconds);

    /// <summary>A run that did not finish within its bound and was killed.</summary>
    /// <param name="option">The read option.</param>
    /// <returns>The outcome.</returns>
    public static CctkRunOutcome TimedOut(CctkReadOption option) =>
        new(option, CctkReader.ArgumentFor(option), true, null, string.Empty, string.Empty, true, null, 15000);

    /// <summary>A run that could not be started.</summary>
    /// <param name="option">The read option.</param>
    /// <returns>The outcome.</returns>
    public static CctkRunOutcome NotStarted(CctkReadOption option) =>
        new(option, CctkReader.ArgumentFor(option), false, null, string.Empty, string.Empty, false, "process did not start", 0);
}
