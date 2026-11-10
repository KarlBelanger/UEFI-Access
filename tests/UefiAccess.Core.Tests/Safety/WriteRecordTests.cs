// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using System.Text.Json;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Variables;
using Xunit;

namespace UefiAccess.Core.Tests.Safety;

/// <summary>
/// The structured record of task 3.8: the field set and its order,
/// camelCase with enums as strings, one line per record for write-log.jsonl whatever the
/// sentence holds, a round trip through <see cref="WriteRecord.TryParse"/>, and no field, at any
/// depth, that could hold key material, the service tag or the Windows machine ID.
/// </summary>
public sealed class WriteRecordTests
{
    private static readonly string[] ExpectedFields =
    {
        "schema", "timestampUtc", "transactionId", "toolVersion", "host", "operation", "isRestore", "restoredFrom",
        "variable", "attributesBefore", "attributesRequested", "attributeNames", "sizeBefore", "sizeRequested",
        "beforeSha256", "proposedSha256", "afterSha256", "before", "after", "checks", "blockedBy", "confirmed",
        "bitLockerChoice", "bitLocker", "writesThisStart", "bootId", "deviceNodeTypes", "secureBoot", "machine", "apiCalled",
        "api", "verification", "snapshotFolder", "outcome", "exitCode", "sentence",
    };

    /// <summary>Parts of a property name that would mean a secret: a password, a key package, an external key, a protector's ID, the service tag, the Windows machine ID.</summary>
    private static readonly string[] SecretNameParts = { "Password", "Secret", "KeyPackage", "ExternalKey", "ProtectorId", "ServiceTag", "SerialNumber", "MachineId", "MachineGuid" };

    [Fact]
    public void Json_HasTheFieldsOfTheDesign_InTheirOrder()
    {
        using JsonDocument json = JsonDocument.Parse(Sample().ToJsonLine());

        Assert.Equal(ExpectedFields, json.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(WriteRecord.SchemaId, json.RootElement.GetProperty("schema").GetString());
        Assert.Equal("Verified", json.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("SuspendForOneRestart", json.RootElement.GetProperty("bitLockerChoice").GetString());
        Assert.Equal("On", json.RootElement.GetProperty("bitLocker").GetProperty("state").GetString());
        Assert.Equal("Success", json.RootElement.GetProperty("verification").GetProperty("readStatus").GetString());
        Assert.Equal(57u, json.RootElement.GetProperty("bootId").GetUInt32());
        Assert.Equal(
            new[] { "succeeded", "win32Error", "win32Name", "win32Text", "ntStatus", "ntStatusText", "ntStatusSource", "elapsedMs" },
            json.RootElement.GetProperty("api").EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            new[] { "model", "biosVersion", "confirmed", "unitConfirmed" },
            json.RootElement.GetProperty("machine").EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public void NoField_AtAnyDepth_CanHoldKeyMaterialOrTheServiceTag()
    {
        List<string> names = new();
        Collect(typeof(WriteRecord), names, new HashSet<Type>());

        Assert.Contains("Api.NtStatusSource", names);
        Assert.Contains("BitLocker.ProtectorTypes", names);
        Assert.Contains("Checks[].Reason", names);
        Assert.Contains("DeviceNodeTypes", names);
        Assert.All(names, name => Assert.DoesNotContain(SecretNameParts, part => name.Contains(part, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void ToJsonLine_IsOneLine_EvenWhenTheSentenceHasLineBreaks()
    {
        WriteRecord record = Sample() with { Sentence = "first line\r\nsecond line\nthird" };

        string line = record.ToJsonLine();

        Assert.DoesNotContain("\n", line, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", line, StringComparison.Ordinal);
        Assert.Contains("\n", record.ToJson(), StringComparison.Ordinal);
        Assert.Equal(record.Sentence, WriteRecord.TryParse(line)!.Sentence);
    }

    [Fact]
    public void TryParse_RoundTripsEveryField()
    {
        WriteRecord record = Sample();

        WriteRecord parsed = Assert.IsType<WriteRecord>(WriteRecord.TryParse(record.ToJsonLine()));

        Assert.Equal(record.ToJsonLine(), parsed.ToJsonLine());
        Assert.Equal(record.ToJsonLine(), WriteRecord.TryParse(record.ToJson())!.ToJsonLine());
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 12, 15, 2, TimeSpan.Zero), parsed.Timestamp);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("{\"schema\": \"another-schema/1\", \"outcome\": \"Verified\"}")]
    [InlineData("{\"outcome\": \"NoSuchOutcome\"}")]
    public void TryParse_ReturnsNullForTextThatIsNotARecord(string? text)
    {
        Assert.Null(WriteRecord.TryParse(text));
    }

    [Fact]
    public void FormatTimestamp_IsUtcToTheSecond()
    {
        DateTimeOffset local = new(2026, 9, 30, 14, 15, 2, 999, TimeSpan.FromHours(2));

        Assert.Equal("2026-09-30T12:15:02Z", WriteRecord.FormatTimestamp(local));
    }

    private static WriteRecord Sample()
    {
        return new WriteRecord
        {
            TimestampUtc = "2026-09-30T12:15:02Z",
            TransactionId = "20260930-141502-reorder",
            ToolVersion = "0.1.0+test",
            Host = ChangeRequest.AppHost,
            Operation = nameof(ChangeOperation.Reorder),
            Variable = new WriteRecordVariable(Whitelist.BootOrder.Key, "BootOrder", KnownGuids.EfiGlobalVariable.ToString("D")),
            AttributesBefore = 7,
            AttributesRequested = 7,
            AttributeNames = "NV BS RT",
            SizeBefore = 4,
            SizeRequested = 4,
            BeforeSha256 = new string('b', 64),
            ProposedSha256 = new string('c', 64),
            AfterSha256 = new string('c', 64),
            Before = new WriteRecordImage(true, "02000300", new[] { "Boot0002", "Boot0003" }),
            After = new WriteRecordImage(true, "03000200", new[] { "Boot0003", "Boot0002" }),
            Checks = new[] { new CheckResult(CheckNames.Preconditions, true, string.Empty), new CheckResult(CheckNames.RemovableAhead, true, "a warning", IsWarning: true) },
            Confirmed = true,
            BitLockerChoice = BitLockerChoice.SuspendForOneRestart,
            BitLocker = new WriteRecordBitLocker(BitLockerState.On, 1, 1, new[] { "TPM", "Numerical password" }, "PCR 0,2,4,11 (legacy)", true, true, "0", 0),
            WritesThisStart = 1,
            BootId = 57,
            DeviceNodeTypes = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["Boot0003"] = new[] { "PciRoot", "Pci", "USB" } },
            SecureBoot = new WriteRecordSecureBoot(true, false, true),
            Machine = new WriteRecordMachine("OptiPlex 7020", "A18", true, true),
            ApiCalled = true,
            Api = new WriteRecordApi(true, 0, string.Empty, string.Empty, null, null, FirmwareVariableWriter.NtStatusNotObserved, 12),
            Verification = new WriteRecordVerification(ReadStatus.Success, true, false, new string('c', 64), 7),
            SnapshotFolder = @"C:\ProgramData\UefiAccess\snapshots\20260930-141502-reorder",
            Outcome = WriteRecordOutcome.Verified,
            ExitCode = WriteExitCodes.Success,
            Sentence = "Boot order changed to Boot0003 (USB), Boot0002 (Windows Boot Manager). Restart to use the new order. Snapshot 20260930-141502-reorder.",
        };
    }

    /// <summary>Every public instance property name under <paramref name="type"/>, as "Parent.Child", following the record types of the Safety namespace.</summary>
    private static void Collect(Type type, List<string> names, HashSet<Type> seen, string prefix = "")
    {
        if (!seen.Add(type))
        {
            return;
        }

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            string name = prefix + property.Name;
            names.Add(name);
            Descend(Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType, names, seen, name);
        }
    }

    /// <summary>
    /// Walks into a property's type: a record of the Safety folder is collected field by field, and
    /// a list or dictionary (Checks, DeviceNodeTypes) is walked through its element types, each
    /// named with "[]", so a field held only inside a collection is checked too.
    /// </summary>
    private static void Descend(Type type, List<string> names, HashSet<Type> seen, string name)
    {
        if (type.Namespace == typeof(WriteRecord).Namespace && type.IsClass)
        {
            Collect(type, names, seen, name + ".");
            return;
        }

        if (type.IsGenericType)
        {
            foreach (Type argument in type.GetGenericArguments())
            {
                Descend(argument, names, seen, name + "[]");
            }
        }
    }
}
