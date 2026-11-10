// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Diagnostics;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Privileges;
using UefiAccess.Core.Variables;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>Smoke tests for the pieces the scaffold implements fully.</summary>
public sealed class ScaffoldSmokeTests
{
    private static readonly Guid Global = KnownGuids.EfiGlobalVariable;
    private static readonly Guid ImageSecurity = KnownGuids.EfiImageSecurityDatabase;

    [Fact]
    public void VariableSet_SortsByKeyOrdinal_AndFindsByNameAndGuid()
    {
        FirmwareVariable bootOrder = new("BootOrder", Global, VariableAttributes.NonVolatile | VariableAttributes.RuntimeAccess, new byte[] { 1, 0 });
        FirmwareVariable dbx = new("dbx", ImageSecurity, VariableAttributes.RuntimeAccess, Array.Empty<byte>());
        FirmwareVariable boot0001 = new("Boot0001", Global, VariableAttributes.RuntimeAccess, new byte[] { 9 });
        FirmwareVariable lowerCase = new("bootorder", Global, VariableAttributes.RuntimeAccess, new byte[] { 2 });

        VariableSet set = new(new[] { dbx, bootOrder, lowerCase, boot0001 });

        string[] keys = set.All.Select(v => v.Key).ToArray();
        string[] expected = keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, keys);
        Assert.Equal(4, set.Count);
        Assert.Equal("8be4df61-93ca-11d2-aa0d-00e098032b8c:Boot0001", keys[0]);
        Assert.Equal("8be4df61-93ca-11d2-aa0d-00e098032b8c:BootOrder", keys[1]);
        Assert.Equal("8be4df61-93ca-11d2-aa0d-00e098032b8c:bootorder", keys[2]);
        Assert.Equal("d719b2cb-3d3a-4596-a3bc-dad00e67656f:dbx", keys[3]);

        Assert.Same(bootOrder, set.Find("BootOrder", Global));
        Assert.Same(dbx, set.Find("dbx", ImageSecurity));
        Assert.Null(set.Find("dbx", Global));
        Assert.Null(set.Find("BootOrder", ImageSecurity));
        Assert.Null(set.Find("Missing", Global));
        Assert.Empty(VariableSet.Empty.All);
    }

    [Fact]
    public void FirmwareVariable_KeyAndSize()
    {
        FirmwareVariable variable = new("Timeout", Global, VariableAttributes.None, new byte[] { 5, 0 });
        Assert.Equal("8be4df61-93ca-11d2-aa0d-00e098032b8c:Timeout", variable.Key);
        Assert.Equal(2, variable.Size);
        Assert.Equal(variable.Key, FirmwareVariable.MakeKey(Global, "Timeout"));
    }

    [Fact]
    public void KnownGuids_ToBracedUpper_UsesBracesAndUppercaseHex()
    {
        Assert.Equal("{8BE4DF61-93CA-11D2-AA0D-00E098032B8C}", KnownGuids.ToBracedUpper(Global));
        Assert.Equal("{D719B2CB-3D3A-4596-A3BC-DAD00E67656F}", KnownGuids.ToBracedUpper(ImageSecurity));
    }

    [Theory]
    [InlineData("db", true)]
    [InlineData("dbx", true)]
    [InlineData("dbt", true)]
    [InlineData("dbr", true)]
    [InlineData("BootOrder", false)]
    [InlineData("PK", false)]
    [InlineData("DB", false)]
    [InlineData("", false)]
    public void KnownGuids_DefaultGuidFor(string name, bool expectImageSecurity)
    {
        Guid expected = expectImageSecurity ? ImageSecurity : Global;
        Assert.Equal(expected, KnownGuids.DefaultGuidFor(name));
    }

    [Theory]
    [InlineData("Boot0001", true, 1)]
    [InlineData("Boot0000", true, 0)]
    [InlineData("BootFFFF", true, 0xFFFF)]
    [InlineData("Boot00A5", true, 0xA5)]
    [InlineData("boot0001", false, -1)]
    [InlineData("Boot001", false, -1)]
    [InlineData("Boot00G1", false, -1)]
    [InlineData("Boot00a5", false, -1)]
    [InlineData("Boot00011", false, -1)]
    [InlineData("BootOrder", false, -1)]
    [InlineData("", false, -1)]
    public void KnownVariableNames_IsBootEntryName(string name, bool expected, int expectedIndex)
    {
        bool result = KnownVariableNames.IsBootEntryName(name, out int index);
        Assert.Equal(expected, result);
        Assert.Equal(expectedIndex, index);
    }

    [Fact]
    public void KnownVariableNames_BootName_AndFixedList()
    {
        Assert.Equal("Boot0000", KnownVariableNames.BootName(0));
        Assert.Equal("Boot0001", KnownVariableNames.BootName(1));
        Assert.Equal("Boot00FF", KnownVariableNames.BootName(0xFF));
        Assert.Equal("BootFFFF", KnownVariableNames.BootName(0xFFFF));
        Assert.True(KnownVariableNames.IsBootEntryName(KnownVariableNames.BootName(0x1A2B), out int index));
        Assert.Equal(0x1A2B, index);

        Assert.Equal(19, KnownVariableNames.Fixed.Count);
        Assert.Contains(KnownVariableNames.Fixed, entry => entry.Name == "BootOrder" && entry.Guid == Global);
        Assert.Contains(KnownVariableNames.Fixed, entry => entry.Name == "dbx" && entry.Guid == ImageSecurity);
        Assert.DoesNotContain(KnownVariableNames.Fixed, entry => entry.Name == "dbr");
    }

    [Theory]
    [InlineData(1, "ERROR_INVALID_FUNCTION", "legacy BIOS")]
    [InlineData(5, "ERROR_ACCESS_DENIED", "access denied")]
    [InlineData(122, "ERROR_INSUFFICIENT_BUFFER", "buffer is too small")]
    [InlineData(203, "ERROR_ENVVAR_NOT_FOUND", "not found")]
    [InlineData(1300, "ERROR_NOT_ALL_ASSIGNED", "privilege is absent")]
    [InlineData(1314, "ERROR_PRIVILEGE_NOT_HELD", "SeSystemEnvironmentPrivilege")]
    public void NativeError_DescribeWin32_KnownCodes(int code, string symbol, string phrase)
    {
        string message = NativeError.DescribeWin32(code);
        Assert.Contains(symbol, message, StringComparison.Ordinal);
        Assert.Contains("(" + code + ")", message, StringComparison.Ordinal);
        Assert.Contains(phrase, message, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeError_DescribeWin32_UnknownCodeIncludesNumber()
    {
        string message = NativeError.DescribeWin32(4242);
        Assert.Contains("4242", message, StringComparison.Ordinal);
        Assert.Contains("0x00001092", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0xC0000023u, "STATUS_BUFFER_TOO_SMALL", "buffer is too small")]
    [InlineData(0xC0000061u, "STATUS_PRIVILEGE_NOT_HELD", "SeSystemEnvironmentPrivilege")]
    [InlineData(0xC0000002u, "STATUS_NOT_IMPLEMENTED", "legacy BIOS")]
    [InlineData(0xC000000Du, "STATUS_INVALID_PARAMETER", "invalid parameter")]
    [InlineData(0u, "STATUS_SUCCESS", "succeeded")]
    public void NativeError_DescribeNtStatus_KnownCodes(uint status, string symbol, string phrase)
    {
        string message = NativeError.DescribeNtStatus(status);
        Assert.Contains(symbol, message, StringComparison.Ordinal);
        Assert.Contains("0x" + status.ToString("X8"), message, StringComparison.Ordinal);
        Assert.Contains(phrase, message, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeError_DescribeNtStatus_UnknownPrintsHex()
    {
        Assert.Equal("NTSTATUS 0xC00000BB", NativeError.DescribeNtStatus(0xC00000BB));
        Assert.True(NativeError.IsNtSuccess(0));
        Assert.False(NativeError.IsNtSuccess(0xC0000023));
    }

    [Fact]
    public void PrivilegeManager_DoesNotThrowOnThisHost()
    {
        PrivilegeManager manager = new();
        PrivilegeState state = PrivilegeState.NotElevated;
        bool elevated = true;
        Exception? exception = Record.Exception(() =>
        {
            elevated = manager.IsElevated;
            state = manager.EnableSystemEnvironmentPrivilege();
        });

        Assert.Null(exception);
        Assert.True(Enum.IsDefined(state));
        if (!OperatingSystem.IsWindows())
        {
            Assert.False(elevated);
            Assert.Equal(PrivilegeState.NotElevated, state);
            Assert.NotNull(manager.LastDetail);
        }
        else if (!elevated)
        {
            Assert.Equal(PrivilegeState.NotElevated, state);
        }
    }

    [Fact]
    public void FirmwareTypeDetector_DoesNotThrowOnThisHost()
    {
        FirmwareTypeDetector detector = new();
        FirmwareType type = FirmwareType.Unknown;
        Exception? exception = Record.Exception(() => type = detector.Detect());

        Assert.Null(exception);
        Assert.True(Enum.IsDefined(type));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(FirmwareType.Unknown, type);
        }
    }

    [Fact]
    public void Logging_CreateNeverThrows_AndDirectoriesAreUnderUefiAccess()
    {
        Assert.EndsWith(Path.Combine("UefiAccess", "logs"), Logging.LogDirectory, StringComparison.Ordinal);
        Assert.EndsWith("UefiAccess", Logging.DataDirectory, StringComparison.Ordinal);

        // The logger writes into a temporary data root, never the machine's real
        // ProgramData\UefiAccess: a test run there would create the tool's data root with the
        // inherited access list and owned by the test user, which the elevated tool then refuses.
        string root = Path.Combine(Path.GetTempPath(), "UefiAccess.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            string data = Path.Combine(root, "ProgramData", "UefiAccess");
            LogLocations locations = new(data, Path.Combine(data, "logs"), Path.Combine(root, "LocalAppData", "UefiAccess", "logs"));
            Exception? exception = Record.Exception(() =>
            {
                Serilog.ILogger logger = Logging.Create("tests", console: false, locations, elevated: false, TextWriter.Null);
                Assert.NotNull(logger);
                logger.Debug("scaffold smoke test");
                (logger as IDisposable)?.Dispose();
            });
            Assert.Null(exception);
            Assert.False(Directory.Exists(data), "a process that is not elevated never creates the data root");
        }
        finally
        {
            TestFolders.DeleteQuietly(root);
        }
    }
}
