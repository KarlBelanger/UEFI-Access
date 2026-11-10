// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Platform;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The BitLocker status words the gate prints: ConversionStatus in words, and a protection status
/// that never reads "Off" while the volume is still being encrypted (Windows reports
/// ProtectionStatus 0 until encryption completes, which a tester who has just turned BitLocker on
/// would read as a failure). Pure logic; no WMI.
/// </summary>
public sealed class BitLockerStatusTextTests
{
    [Theory]
    [InlineData(0, "Decrypted")]
    [InlineData(1, "Encrypted")]
    [InlineData(2, "Encrypting")]
    [InlineData(3, "Decrypting")]
    [InlineData(4, "Encryption paused")]
    [InlineData(5, "Decryption paused")]
    [InlineData(-1, "Unknown")]
    [InlineData(9, "Unknown (9)")]
    public void ConversionStatusText_NamesEveryDocumentedValue(int conversionStatus, string expected)
    {
        Assert.Equal(expected, BitLockerInspector.ConversionStatusText(conversionStatus));
    }

    [Theory]
    [InlineData(0, 0, "Off")]
    [InlineData(0, 1, "Off")]
    [InlineData(0, 2, "Pending until encryption completes")]
    [InlineData(0, 3, "Off")]
    [InlineData(0, 4, "Pending, encryption paused")]
    [InlineData(0, 5, "Off")]
    [InlineData(0, -1, "Off")]
    [InlineData(1, 1, "On")]
    [InlineData(1, 2, "On")]
    [InlineData(2, 2, "Unknown")]
    public void ProtectionStatusText_TakesTheConversionIntoAccount(int protectionStatus, int conversionStatus, string expected)
    {
        Assert.Equal(expected, BitLockerInspector.ProtectionStatusText(protectionStatus, conversionStatus));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void ProtectionStatusText_NeverSaysOffWhileEncryptionIsUnderWay(int conversionStatus)
    {
        Assert.DoesNotContain("Off", BitLockerInspector.ProtectionStatusText(0, conversionStatus), StringComparison.Ordinal);
    }
}
