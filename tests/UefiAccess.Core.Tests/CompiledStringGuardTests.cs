// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using UefiAccess.Core.Safety;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The source scan of <see cref="NoWriteGuardTests"/> reads each line as written, so a name split
/// across a constant concatenation ("Suspend-Bit" + "Locker -MountPoint C:") gets past it. The
/// compiler folds such a concatenation into one string, so this test reads every string the
/// compiler emitted into the built Core, CLI and Presentation assemblies: every string literal
/// the code loads (the user-string heap, where each used constant's value is inlined too) and the
/// value of every string constant and default parameter value. Each line of each string, wrapped in
/// double quotes as it would stand in source, must pass <see cref="NoWriteGuardTests.FindViolations"/>,
/// with one exception since M3: Core holds the BitLocker
/// suspender's WMI method name exactly once, as that whole string and nothing longer, so no log
/// template, message, resource or folded label ("DisableKeyProtectors on C:") repeats it. The
/// firmware write import's name is in no compiled string at all: its DllImport is a
/// custom-attribute argument, which this reader does not see, and the log says "firmware write"
/// and the variable key. Strings built at run time are beyond it, and so are custom-attribute
/// arguments and the WPF App, which the test project cannot load; the source scans cover those.
/// </summary>
public sealed class CompiledStringGuardTests
{
    /// <summary>
    /// A constant folded from two literals. Nothing in the source spells the joined command; it
    /// exists only in this test assembly's metadata, which the product scan does not read.
    /// </summary>
    private const string FoldedProbe = "Suspend-Bit" + "Locker -MountPoint C: -RebootCount 0";

    /// <summary>The write import's name, which no compiled string of the product may contain.</summary>
    private const string WriteImportName = "SetFirmwareEnvironmentVariable";

    /// <summary>The one forbidden name Core may hold as a compiled string, equal to the suspender's constant.</summary>
    private const string SuspendMethodName = "DisableKeyProtectors";

    [Fact]
    public void EveryStringTheCompilerEmittedPassesTheNoWriteScanExceptTheOneSuspendMethodName()
    {
        Assert.Equal(SuspendMethodName, BitLockerSuspender.DisableKeyProtectorsMethod);
        Assembly core = typeof(BitLockerSuspender).Assembly;
        foreach (Assembly assembly in ProductAssemblies.All)
        {
            string name = assembly.GetName().Name ?? assembly.Location;
            IReadOnlyList<string> strings = CompiledStrings(assembly.Location);
            Assert.True(strings.Count > 0, "no string read from " + name + "; the scan would be vacuous");

            string[] offending = strings.Where(value => Violations(value).Count > 0).Order(StringComparer.Ordinal).ToArray();
            string[] allowed = assembly == core ? new[] { SuspendMethodName } : Array.Empty<string>();
            Assert.True(
                offending.SequenceEqual(allowed, StringComparer.Ordinal),
                $"{name}: the compiled strings that name a write path must be exactly [{string.Join(", ", allowed)}]. Found:" + Environment.NewLine
                + string.Join(Environment.NewLine, offending.Select(value => $"\"{value}\": {string.Join("; ", Violations(value))}")));

            string[] naming = strings.Where(value => value.Contains(WriteImportName, StringComparison.OrdinalIgnoreCase)).ToArray();
            Assert.True(naming.Length == 0, $"{name}: a compiled string names the firmware write import: " + string.Join(", ", naming));
        }
    }

    [Fact]
    public void ReaderSeesInlinedConstantsAndFoldedConcatenations()
    {
        // The product: the PowerShell command, the cctk option and the suspend method are declared
        // as private or internal constants and reach the user-string heap only because the
        // compiler inlines them where they are used.
        string core = ProductAssemblies.All[0].Location;
        Assert.Contains("Confirm-SecureBootUEFI", UserStrings(core));
        Assert.Contains("--secureboot", UserStrings(core));
        Assert.Contains("Confirm-SecureBootUEFI", ConstantStrings(core));
        Assert.Contains(SuspendMethodName, UserStrings(core));
        Assert.Contains(SuspendMethodName, ConstantStrings(core));

        // This assembly: the constant table holds the folded value whole, and the scan flags it.
        Assert.Contains(FoldedProbe, ConstantStrings(typeof(CompiledStringGuardTests).Assembly.Location));
        Assert.NotEmpty(NoWriteGuardTests.FindViolations("\"" + FoldedProbe + "\""));
    }

    /// <summary>What the no-write scan says about each line of a compiled string, wrapped in double quotes as it would stand in source.</summary>
    /// <param name="value">The compiled string.</param>
    /// <returns>Zero or more violation descriptions.</returns>
    private static IReadOnlyList<string> Violations(string value) =>
        value.Split('\n').SelectMany(line => NoWriteGuardTests.FindViolations("\"" + line.TrimEnd('\r') + "\"")).ToArray();

    /// <summary>Every string in an assembly's metadata that the code can use: <see cref="UserStrings"/> and <see cref="ConstantStrings"/>.</summary>
    /// <param name="path">The assembly file.</param>
    /// <returns>The strings, duplicates removed.</returns>
    internal static IReadOnlyList<string> CompiledStrings(string path) => UserStrings(path).Union(ConstantStrings(path), StringComparer.Ordinal).ToArray();

    /// <summary>The user-string heap: the operand of every ldstr, a used constant's inlined value included.</summary>
    /// <param name="path">The assembly file.</param>
    /// <returns>The strings in heap order.</returns>
    internal static IReadOnlyList<string> UserStrings(string path)
    {
        return ReadMetadata(path, reader =>
        {
            List<string> strings = new();
            for (UserStringHandle handle = reader.GetNextHandle(MetadataTokens.UserStringHandle(0)); !handle.IsNil; handle = reader.GetNextHandle(handle))
            {
                strings.Add(reader.GetUserString(handle));
            }

            return strings;
        });
    }

    /// <summary>The string values of every constant field and every default parameter value.</summary>
    /// <param name="path">The assembly file.</param>
    /// <returns>The strings in metadata order.</returns>
    internal static IReadOnlyList<string> ConstantStrings(string path)
    {
        return ReadMetadata(path, reader =>
        {
            IEnumerable<ConstantHandle> constants = reader.FieldDefinitions
                .Select(field => reader.GetFieldDefinition(field).GetDefaultValue())
                .Concat(reader.MethodDefinitions
                    .SelectMany(method => reader.GetMethodDefinition(method).GetParameters())
                    .Select(parameter => reader.GetParameter(parameter).GetDefaultValue()));
            List<string> strings = new();
            foreach (ConstantHandle handle in constants.Where(handle => !handle.IsNil))
            {
                Constant constant = reader.GetConstant(handle);
                if (constant.TypeCode == ConstantTypeCode.String)
                {
                    BlobReader blob = reader.GetBlobReader(constant.Value);
                    strings.Add(blob.ReadUTF16(blob.Length));
                }
            }

            return strings;
        });
    }

    private static IReadOnlyList<string> ReadMetadata(string path, Func<MetadataReader, List<string>> read)
    {
        Assert.False(string.IsNullOrEmpty(path), "the assembly has no file location");
        using FileStream stream = File.OpenRead(path);
        using PEReader pe = new(stream);
        return read(pe.GetMetadataReader());
    }
}
