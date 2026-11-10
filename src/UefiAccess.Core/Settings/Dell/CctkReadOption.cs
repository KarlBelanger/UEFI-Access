// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Core.Settings.Dell;

/// <summary>
/// The nine Dell BIOS settings this build reads through Dell Command | Configure, one read option
/// each. A member is the only thing a caller can hand
/// <see cref="CctkReader.RunCctk"/>; <see cref="CctkReader.ArgumentFor"/> turns it into one of
/// the reader's nine option constants, so no caller can pass free text, a value or a second
/// argument. Boot list option has no member: its only read form shares an option whose other
/// forms change a setting, and this build never passes that option. Adding a member is a guard
/// change, reviewed at gate G-A with the reader and NoWriteGuardTests.
/// </summary>
internal enum CctkReadOption
{
    /// <summary>Secure Boot (catalog row 2).</summary>
    SecureBoot,

    /// <summary>SATA operation (catalog row 3).</summary>
    SataOperation,

    /// <summary>TPM security (catalog row 4).</summary>
    TpmSecurity,

    /// <summary>TPM activation (catalog row 5).</summary>
    TpmActivation,

    /// <summary>Virtualization (catalog row 6).</summary>
    Virtualization,

    /// <summary>VT for Direct I/O (catalog row 7).</summary>
    VtForDirectIo,

    /// <summary>Integrated NIC (catalog row 8).</summary>
    IntegratedNic,

    /// <summary>Wake on LAN (catalog row 9).</summary>
    WakeOnLan,

    /// <summary>Num Lock at boot (catalog row 10).</summary>
    NumLock,
}
