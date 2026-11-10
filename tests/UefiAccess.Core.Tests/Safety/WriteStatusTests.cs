// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Diagnostics;
using UefiAccess.Core.Safety;
using Xunit;

namespace UefiAccess.Core.Tests.Safety;

/// <summary>
/// The Win32-to-class mapping of task 3.6 and its sentences: every code
/// in the table maps to its class and its sentence verbatim, an unknown code is a firmware error
/// with the code and its text, a successful outcome has no refusal sentence, and the record's
/// name and text fields come from the SDK headers, never from the localised system message.
/// Pure logic; nothing here touches native code.
/// </summary>
public sealed class WriteStatusTests
{
    [Theory]
    [InlineData(0, WriteStatus.Success)]
    [InlineData(19, WriteStatus.WriteProtected)]
    [InlineData(5, WriteStatus.AccessDenied)]
    [InlineData(50, WriteStatus.NotSupported)]
    [InlineData(87, WriteStatus.InvalidParameter)]
    [InlineData(203, WriteStatus.NotFound)]
    [InlineData(1470, WriteStatus.NoNvramRoom)]
    [InlineData(8, WriteStatus.SystemResources)]
    [InlineData(14, WriteStatus.SystemResources)]
    [InlineData(1450, WriteStatus.SystemResources)]
    [InlineData(1314, WriteStatus.PrivilegeNotHeld)]
    [InlineData(1, WriteStatus.NotImplemented)]
    [InlineData(998, WriteStatus.FirmwareError)]
    [InlineData(31, WriteStatus.FirmwareError)]
    [InlineData(1117, WriteStatus.FirmwareError)]
    [InlineData(4242, WriteStatus.FirmwareError)]
    public void FromWin32_ClassifiesEveryCodeOfTheTable(int code, WriteStatus expected)
    {
        Assert.Equal(expected, WriteStatusMapping.FromWin32(code));
    }

    [Theory]
    [InlineData(19, "The firmware refused the change: the variable is write-protected (error 19). Nothing was changed.")]
    [InlineData(5, "The firmware refused the change: access denied (error 5). Nothing was changed.")]
    [InlineData(50, "The firmware does not support this change at run time (error 50). Nothing was changed.")]
    [InlineData(87, "The firmware rejected the request as invalid (error 87): the attributes or size did not match. Nothing was changed.")]
    [InlineData(203, "The variable was not found by the firmware (error 203). Nothing was changed.")]
    [InlineData(1470, "The firmware has no room left for the change (error 1470). Nothing was changed. Restart the machine and read the variables again before trying once more.")]
    [InlineData(8, "Windows could not get the resources for the call (error 8: not enough memory resources are available to process this command). Nothing was changed.")]
    [InlineData(14, "Windows could not get the resources for the call (error 14: not enough storage is available to complete this operation). Nothing was changed.")]
    [InlineData(1450, "Windows could not get the resources for the call (error 1450: insufficient system resources exist to complete the requested service). Nothing was changed.")]
    [InlineData(1314, "Windows refused the write: the privilege was not held at the moment of the call (error 1314). Nothing was changed. The record shows the privilege state.")]
    [InlineData(1, "The firmware does not implement this variable operation at run time (error 1). Nothing was changed.")]
    [InlineData(998, "The firmware does not know the variable's namespace (error 998). Nothing was changed.")]
    [InlineData(31, "The firmware reported an error it did not name (error 31: a device attached to the system is not functioning). Nothing was changed.")]
    [InlineData(1117, "The firmware reported an error it did not name (error 1117: the request could not be performed because of an I/O device error). Nothing was changed.")]
    public void Sentence_IsTheDesignsSentenceForEveryCode(int code, string expected)
    {
        WriteOutcome outcome = WriteStatusMapping.Failed(code, 0xC0000001, FirmwareVariableWriter.NtStatusSourceName, TimeSpan.Zero);

        Assert.Equal(expected, WriteStatusMapping.Sentence(outcome));
        Assert.Equal(expected, WriteStatusMapping.Sentence(outcome.Status, outcome.Win32Error, outcome.Win32Text));
    }

    [Fact]
    public void Sentence_AnUnknownCodeWithoutText_SaysSo()
    {
        Assert.Equal(
            "The firmware reported an error it did not name (error 4242: no description). Nothing was changed.",
            WriteStatusMapping.Sentence(WriteStatus.FirmwareError, 4242, string.Empty));
    }

    [Fact]
    public void Sentence_EveryRefusalStatesThatNothingWasChanged()
    {
        foreach (int code in new[] { 19, 5, 50, 87, 203, 1470, 8, 14, 1450, 1314, 1, 998, 31, 1117, 4242 })
        {
            string sentence = WriteStatusMapping.Sentence(WriteStatusMapping.FromWin32(code), code, NativeError.Win32Text(code));
            Assert.Contains("Nothing was changed.", sentence, StringComparison.Ordinal);
            Assert.Contains("(error " + code, sentence, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Sentence_ASuccessfulOutcomeHasNoRefusalSentence()
    {
        WriteOutcome success = WriteStatusMapping.Succeeded(TimeSpan.FromMilliseconds(3));

        Assert.Throws<ArgumentException>(() => WriteStatusMapping.Sentence(success));
        Assert.Throws<ArgumentException>(() => WriteStatusMapping.Sentence(WriteStatus.Success, 0, string.Empty));
    }

    [Fact]
    public void ChangedDespiteErrorSentence_NamesTheCodeAndTheSnapshot()
    {
        Assert.Equal(
            "The firmware reported error 19 but the change is present. Snapshot 20260930-141502-reorder.",
            WriteStatusMapping.ChangedDespiteErrorSentence(19, "20260930-141502-reorder"));
    }

    [Fact]
    public void Succeeded_HasNoErrorAndNoObservedNtStatus()
    {
        WriteOutcome outcome = WriteStatusMapping.Succeeded(TimeSpan.FromMilliseconds(12));

        Assert.True(outcome.Succeeded);
        Assert.Equal(0, outcome.Win32Error);
        Assert.Equal(string.Empty, outcome.Win32Name);
        Assert.Equal(string.Empty, outcome.Win32Text);
        Assert.Null(outcome.NtStatus);
        Assert.Null(outcome.NtStatusText);
        Assert.Equal(FirmwareVariableWriter.NtStatusNotObserved, outcome.NtStatusSource);
        Assert.Equal("not observed", outcome.NtStatusSource);
        Assert.Equal(WriteStatus.Success, outcome.Status);
        Assert.Equal(TimeSpan.FromMilliseconds(12), outcome.Elapsed);
    }

    [Fact]
    public void Failed_CarriesTheNameTheTextAndTheNtStatusAsObserved()
    {
        WriteOutcome outcome = WriteStatusMapping.Failed(19, 0xC00000A2, FirmwareVariableWriter.NtStatusSourceName, TimeSpan.FromMilliseconds(7));

        Assert.False(outcome.Succeeded);
        Assert.Equal(19, outcome.Win32Error);
        Assert.Equal("ERROR_WRITE_PROTECT", outcome.Win32Name);
        Assert.Equal("the media is write protected", outcome.Win32Text);
        Assert.Equal(0xC00000A2u, outcome.NtStatus);
        Assert.Equal("STATUS_MEDIA_WRITE_PROTECTED (0xC00000A2): the disk cannot be written to because it is write-protected", outcome.NtStatusText);
        Assert.Equal("RtlGetLastNtStatus", outcome.NtStatusSource);
        Assert.Equal(WriteStatus.WriteProtected, outcome.Status);
        Assert.Equal(TimeSpan.FromMilliseconds(7), outcome.Elapsed);
    }

    [Fact]
    public void Failed_NeverDerivesTheNtStatusFromTheWin32Code_AndSaysWhenItMayBeStale()
    {
        // Error 19 with an NTSTATUS that does not correspond to it: both numbers are kept as read,
        // and the source says the status may be left over from an earlier failure.
        WriteOutcome outcome = WriteStatusMapping.Failed(19, 0xC0000022, FirmwareVariableWriter.NtStatusSourceName, TimeSpan.Zero);

        Assert.Equal(0xC0000022u, outcome.NtStatus);
        Assert.StartsWith("STATUS_ACCESS_DENIED (0xC0000022)", outcome.NtStatusText, StringComparison.Ordinal);
        Assert.Equal("RtlGetLastNtStatus; may be stale: not known to be the status behind error 19", outcome.NtStatusSource);
        Assert.Equal(WriteStatus.WriteProtected, outcome.Status);

        // A status Windows does not turn into any code of the table is not known to belong either.
        Assert.EndsWith("may be stale: not known to be the status behind error 31", WriteStatusMapping.Failed(31, 0xC00000FF, FirmwareVariableWriter.NtStatusSourceName, TimeSpan.Zero).NtStatusSource, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0xC0000001u, 31)]
    [InlineData(0xC0000002u, 1)]
    [InlineData(0xC000000Du, 87)]
    [InlineData(0xC0000017u, 8)]
    [InlineData(0xC0000022u, 5)]
    [InlineData(0xC0000061u, 1314)]
    [InlineData(0xC000009Au, 1450)]
    [InlineData(0xC00000A2u, 19)]
    [InlineData(0xC00000BBu, 50)]
    [InlineData(0xC0000100u, 203)]
    [InlineData(0xC0000185u, 1117)]
    [InlineData(0xC0000454u, 1470)]
    public void NtStatusCorrespondsTo_TheWin32CodeWindowsTurnsItInto_AndNoOther(uint status, int code)
    {
        Assert.True(NativeError.NtStatusCorrespondsTo(status, code));
        Assert.False(NativeError.NtStatusCorrespondsTo(status, code + 1));

        WriteOutcome outcome = WriteStatusMapping.Failed(code, status, FirmwareVariableWriter.NtStatusSourceName, TimeSpan.Zero);
        Assert.Equal("RtlGetLastNtStatus", outcome.NtStatusSource);
    }

    [Fact]
    public void Failed_WithoutAnErrorCode_IsAFirmwareError_NeverASuccess()
    {
        WriteOutcome outcome = WriteStatusMapping.Failed(0, 0xC0000001, FirmwareVariableWriter.NtStatusSourceName, TimeSpan.Zero);

        Assert.False(outcome.Succeeded);
        Assert.Equal(WriteStatus.FirmwareError, outcome.Status);
        Assert.Equal("the call reported a failure without an error code", outcome.Win32Text);
        Assert.Equal(
            "The firmware reported an error it did not name (error 0: the call reported a failure without an error code). Nothing was changed.",
            WriteStatusMapping.Sentence(outcome));
    }

    [Fact]
    public void TheWriteTexts_GiveNoReadPathAdvice_WhileTheReadPathKeepsItsMilestoneOneMessages()
    {
        // At S15 the machine is UEFI and the privilege was enabled at S1 (design section 3.6), so
        // "legacy BIOS mode" and "run elevated" would be wrong in the write record.
        foreach (string text in new[]
        {
            NativeError.Win32WriteText(1),
            NativeError.Win32WriteText(1314),
            NativeError.NtStatusWriteText(0xC0000002),
            NativeError.NtStatusWriteText(0xC0000061),
            NativeError.DescribeWriteNtStatus(0xC0000002),
            NativeError.DescribeWriteNtStatus(0xC0000061),
            WriteStatusMapping.Failed(1, 0xC0000002, FirmwareVariableWriter.NtStatusSourceName, TimeSpan.Zero).Win32Text,
            WriteStatusMapping.Failed(1314, 0xC0000061, FirmwareVariableWriter.NtStatusSourceName, TimeSpan.Zero).NtStatusText ?? string.Empty,
        })
        {
            Assert.DoesNotContain("legacy BIOS", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("run elevated", text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal("invalid function; the firmware does not implement this variable operation at run time", NativeError.Win32WriteText(1));
        Assert.Equal("SeSystemEnvironmentPrivilege was not held at the moment of the call", NativeError.Win32WriteText(1314));
        Assert.Equal(NativeError.Win32Text(19), NativeError.Win32WriteText(19));
        Assert.Equal("ERROR_INVALID_FUNCTION (1): invalid function; the machine booted in legacy BIOS mode or the firmware exposes no runtime variable services", NativeError.DescribeWin32(1));
        Assert.Equal("STATUS_NOT_IMPLEMENTED (0xC0000002): not implemented; the machine booted in legacy BIOS mode", NativeError.DescribeNtStatus(0xC0000002));
        Assert.Equal("STATUS_PRIVILEGE_NOT_HELD (0xC0000061): SeSystemEnvironmentPrivilege is not held; run elevated", NativeError.DescribeNtStatus(0xC0000061));
    }

    [Theory]
    [InlineData(8, "ERROR_NOT_ENOUGH_MEMORY", "not enough memory resources are available to process this command")]
    [InlineData(14, "ERROR_OUTOFMEMORY", "not enough storage is available to complete this operation")]
    [InlineData(19, "ERROR_WRITE_PROTECT", "the media is write protected")]
    [InlineData(31, "ERROR_GEN_FAILURE", "a device attached to the system is not functioning")]
    [InlineData(50, "ERROR_NOT_SUPPORTED", "the request is not supported")]
    [InlineData(87, "ERROR_INVALID_PARAMETER", "the parameter is incorrect")]
    [InlineData(998, "ERROR_NOACCESS", "invalid access to memory location")]
    [InlineData(1117, "ERROR_IO_DEVICE", "the request could not be performed because of an I/O device error")]
    [InlineData(1450, "ERROR_NO_SYSTEM_RESOURCES", "insufficient system resources exist to complete the requested service")]
    [InlineData(1470, "ERROR_NO_NVRAM_RESOURCES", "insufficient NVRAM resources exist to complete the requested service; a reboot might be required")]
    public void NativeError_NamesAndDescribesEveryWin32CodeOfTheTable(int code, string name, string text)
    {
        Assert.Equal(name, NativeError.Win32Name(code));
        Assert.Equal(text, NativeError.Win32Text(code));

        // The read path's M1 message for a code M1 did not name is unchanged: its number and the system's message.
        Assert.StartsWith("Win32 error " + code + " (0x" + code.ToString("X8") + ")", NativeError.DescribeWin32(code), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0xC0000001u, "STATUS_UNSUCCESSFUL")]
    [InlineData(0xC0000002u, "STATUS_NOT_IMPLEMENTED")]
    [InlineData(0xC0000017u, "STATUS_NO_MEMORY")]
    [InlineData(0xC0000022u, "STATUS_ACCESS_DENIED")]
    [InlineData(0xC000009Au, "STATUS_INSUFFICIENT_RESOURCES")]
    [InlineData(0xC00000A2u, "STATUS_MEDIA_WRITE_PROTECTED")]
    [InlineData(0xC00000BBu, "STATUS_NOT_SUPPORTED")]
    [InlineData(0xC0000100u, "STATUS_VARIABLE_NOT_FOUND")]
    [InlineData(0xC0000185u, "STATUS_IO_DEVICE_ERROR")]
    [InlineData(0xC0000454u, "STATUS_INSUFFICIENT_NVRAM_RESOURCES")]
    public void NativeError_NamesEveryNtStatusOfTheTable(uint status, string name)
    {
        Assert.Equal(name, NativeError.NtStatusName(status));
        Assert.NotEqual(string.Empty, NativeError.NtStatusText(status));
        Assert.Equal(name + " (0x" + status.ToString("X8") + "): " + NativeError.NtStatusWriteText(status), NativeError.DescribeWriteNtStatus(status));
    }

    [Fact]
    public void NativeError_KeepsTheMilestoneOneTextsAndTheGenericForms()
    {
        Assert.Equal("ERROR_SUCCESS (0): the operation succeeded", NativeError.DescribeWin32(0));
        Assert.Equal("ERROR_ACCESS_DENIED (5): access denied", NativeError.DescribeWin32(5));
        Assert.Equal("ERROR_ENVVAR_NOT_FOUND (203): the firmware variable was not found", NativeError.DescribeWin32(203));
        Assert.Equal("ERROR_PRIVILEGE_NOT_HELD (1314): SeSystemEnvironmentPrivilege is not held; run elevated", NativeError.DescribeWin32(1314));
        Assert.Equal("STATUS_BUFFER_TOO_SMALL (0xC0000023): the buffer is too small; the required length was returned", NativeError.DescribeNtStatus(0xC0000023));
        Assert.StartsWith("Win32 error 4242 (0x00001092)", NativeError.DescribeWin32(4242), StringComparison.Ordinal);
        Assert.Equal("NTSTATUS 0xC00000FF", NativeError.DescribeNtStatus(0xC00000FF));
        Assert.Equal("NTSTATUS 0xC00000FF", NativeError.DescribeWriteNtStatus(0xC00000FF));

        // The M3 codes are named for the write record only; the read path's messages keep the M1 forms.
        Assert.Equal("NTSTATUS 0xC00000BB", NativeError.DescribeNtStatus(0xC00000BB));
        Assert.StartsWith("STATUS_NOT_SUPPORTED (0xC00000BB): ", NativeError.DescribeWriteNtStatus(0xC00000BB), StringComparison.Ordinal);
        Assert.StartsWith("Win32 error 31 (0x0000001F)", NativeError.DescribeWin32(31), StringComparison.Ordinal);
        Assert.Equal(string.Empty, NativeError.Win32Name(4242));
        Assert.Equal(string.Empty, NativeError.NtStatusName(0xC00000FF));
        Assert.Equal(string.Empty, NativeError.NtStatusText(0xC00000FF));
    }

    [Fact]
    public void TheEnum_HasExactlyTheElevenClassesOfTheDesign()
    {
        Assert.Equal(
            new[] { "Success", "PrivilegeNotHeld", "NotImplemented", "WriteProtected", "AccessDenied", "NotSupported", "InvalidParameter", "NotFound", "NoNvramRoom", "SystemResources", "FirmwareError" },
            Enum.GetNames<WriteStatus>());
    }
}
