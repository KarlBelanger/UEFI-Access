// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// Enforces the architecture rule that native declarations live only under
/// src/UefiAccess.Core/Native: every C# file under src/ outside that
/// folder must be free of DllImport, LibraryImport and the extern keyword, in code and comments
/// alike. No file under src/, Native included, may bind native code at run time (NativeLibrary,
/// GetProcAddress, LoadLibrary, GetDelegateForFunctionPointer, unmanaged function pointers), which
/// a declaration scan could not see. Because a text scan cannot see an entry-point name built by
/// concatenation, the P/Invoke entry points the compiler emitted into the built assemblies are
/// read by reflection: exactly one of them may be a name <see cref="NoWriteGuardTests"/> forbids,
/// the single firmware write of M3;
/// no other forbidden name, no raw TPM command submission, no ntdll setter and no import by
/// ordinal. The read of the thread's last NTSTATUS the writer makes after a failed call is a
/// read-only import. The matchers are exercised against synthetic lines as well, so the guard's
/// own coverage is tested.
/// </summary>
public sealed class NativeLocationGuardTests
{
    /// <summary>The only folder (relative to the repository root, "/" separators) that may declare native entry points.</summary>
    private const string NativeDirectory = "src/UefiAccess.Core/Native/";

    /// <summary>The one forbidden-named import the product may make, as "library!EntryPoint": the documented kernel32 write called from the single call site.</summary>
    private const string AllowedWriteEntryPoint = "kernel32.dll!SetFirmwareEnvironmentVariableExW";

    /// <summary>An ntdll entry point that sets something; the product never imports one (the NTSTATUS is read after the kernel32 call, never obtained by calling the setter).</summary>
    private static readonly Regex NtdllSetterPattern = new(@"^(?:Nt|Zw)Set", RegexOptions.CultureInvariant);

    /// <summary>A P/Invoke attribute or the extern keyword as a whole word.</summary>
    private static readonly Regex NativeDeclarationPattern = new(@"\b(?:DllImport|LibraryImport|extern)\b", RegexOptions.CultureInvariant);

    /// <summary>Binding a native export at run time: the name is a string a declaration scan never sees.</summary>
    private static readonly Regex DynamicBindingPattern = new(
        @"\b(?:NativeLibrary|GetProcAddress|LoadLibrary\w*|GetDelegateForFunctionPointer)\b|\bdelegate\s*\*\s*unmanaged",
        RegexOptions.CultureInvariant);

    [Fact]
    public void NativeDeclarationsAppearOnlyUnderCoreNative()
    {
        IReadOnlyList<string> files = RepositoryPaths.Enumerate("src", "*.cs");
        Assert.NotEmpty(files);

        List<string> violations = new();
        int declarationsInsideNative = 0;
        foreach (string file in files)
        {
            string relative = RepositoryPaths.Relative(file);
            bool insideNative = relative.StartsWith(NativeDirectory, StringComparison.Ordinal);
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (!IsNativeDeclaration(lines[i]))
                {
                    continue;
                }

                if (insideNative)
                {
                    declarationsInsideNative++;
                }
                else
                {
                    violations.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Assert.True(declarationsInsideNative > 0, "expected at least one DllImport under " + NativeDirectory + "; the guard would otherwise be scanning the wrong tree");
        Assert.True(
            violations.Count == 0,
            "Native declarations are only allowed under " + NativeDirectory + ". Found:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void NoFileBindsNativeCodeAtRunTime()
    {
        IReadOnlyList<string> files = RepositoryPaths.Enumerate("src", "*.cs");
        Assert.NotEmpty(files);

        List<string> violations = new();
        foreach (string file in files)
        {
            string relative = RepositoryPaths.Relative(file);
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (IsDynamicNativeBinding(lines[i]))
                {
                    violations.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Native code must be declared with DllImport under " + NativeDirectory + ", never bound at run time. Found:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void BuiltAssembliesImportExactlyTheAllowedWriteEntryPoint()
    {
        IReadOnlyList<string> entryPoints = NativeEntryPoints();

        // Not vacuous: the two firmware reads the store makes and the last-NTSTATUS read the
        // writer makes are seen, by their final names.
        Assert.Contains("kernel32.dll!GetFirmwareEnvironmentVariableExW", entryPoints);
        Assert.Contains("ntdll.dll!NtEnumerateSystemEnvironmentValuesEx", entryPoints);
        Assert.Contains("ntdll.dll!RtlGetLastNtStatus", entryPoints);

        string[] forbidden = entryPoints.Where(entry => IsForbiddenEntryPoint(EntryPointName(entry))).ToArray();
        Assert.True(
            forbidden.SequenceEqual(new[] { AllowedWriteEntryPoint }, StringComparer.Ordinal),
            "The built assemblies must import exactly one write entry point, " + AllowedWriteEntryPoint + ". Forbidden-named imports found:" + Environment.NewLine + string.Join(Environment.NewLine, forbidden));

        string[] setters = entryPoints.Where(entry => IsNtdllSetter(EntryPointName(entry))).ToArray();
        Assert.True(setters.Length == 0, "The built assemblies import an ntdll setter:" + Environment.NewLine + string.Join(Environment.NewLine, setters));
    }

    [Fact]
    public void ScanCoversTheNativeFolderAndTheOtherProjects()
    {
        string[] relative = RepositoryPaths.Enumerate("src", "*.cs").Select(RepositoryPaths.Relative).ToArray();

        Assert.Contains("src/UefiAccess.Core/Native/NtDll.cs", relative);
        Assert.Contains("src/UefiAccess.Core/Native/Kernel32.cs", relative);
        Assert.Contains("src/UefiAccess.Core/Native/AdvApi32.cs", relative);
        Assert.Contains("src/UefiAccess.Core/Variables/WindowsFirmwareVariableStore.cs", relative);
        Assert.Contains(relative, path => path.StartsWith("src/UefiAccess.Cli/", StringComparison.Ordinal));
        Assert.Contains(relative, path => path.StartsWith("src/UefiAccess.App/", StringComparison.Ordinal));
        Assert.Contains(relative, path => path.StartsWith("src/UefiAccess.Presentation/", StringComparison.Ordinal));
        Assert.Contains("src/UefiAccess.Presentation/Services/LiveFirmwareSource.cs", relative);
        Assert.DoesNotContain(relative, path => path.Contains("/bin/", StringComparison.Ordinal) || path.Contains("/obj/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("[DllImport(\"kernel32.dll\", SetLastError = true)]")]
    [InlineData("    [LibraryImport(\"ntdll.dll\")]")]
    [InlineData("public static extern uint NtEnumerateSystemEnvironmentValuesEx(uint informationClass, IntPtr buffer, ref uint bufferLength);")]
    [InlineData("private static partial extern bool GetFirmwareType(out uint firmwareType);")]
    [InlineData("extern alias Native;")]
    [InlineData("// declared with DllImport elsewhere")]
    public void Matcher_FlagsEveryDeclarationForm(string line)
    {
        Assert.True(IsNativeDeclaration(line));
    }

    [Theory]
    [InlineData("uint status = NtDll.NtEnumerateSystemEnvironmentValuesEx(informationClass, buffer, ref length);")]
    [InlineData("// forwards to the P/Invoke declarations under Native/")]
    [InlineData("// an external tool such as cctk.exe")]
    [InlineData("internal static class Externals")]
    [InlineData("string DllImports = \"none\";")]
    [InlineData("using System.Runtime.InteropServices;")]
    [InlineData("")]
    public void Matcher_IgnoresOrdinaryCode(string line)
    {
        Assert.False(IsNativeDeclaration(line));
    }

    [Theory]
    [InlineData("IntPtr module = NativeLibrary.Load(\"kernel32.dll\");")]
    [InlineData("IntPtr export = NativeLibrary.GetExport(module, \"SetFirmware\" + \"EnvironmentVariableExW\");")]
    [InlineData("IntPtr address = GetProcAddress(module, name);")]
    [InlineData("IntPtr module = LoadLibraryExW(\"ntdll.dll\", IntPtr.Zero, 0);")]
    [InlineData("SetVariable setter = Marshal.GetDelegateForFunctionPointer<SetVariable>(address);")]
    [InlineData("delegate* unmanaged<char*, char*, void*, uint, uint, int> setter = (delegate* unmanaged<char*, char*, void*, uint, uint, int>)address;")]
    public void Matcher_FlagsEveryRunTimeBindingForm(string line)
    {
        Assert.True(IsDynamicNativeBinding(line));
    }

    [Theory]
    [InlineData("buffer = Marshal.AllocHGlobal((nint)allocated);")]
    [InlineData("int error = Marshal.GetLastWin32Error();")]
    [InlineData("string system = Marshal.GetPInvokeErrorMessage(code).Trim();")]
    [InlineData("public delegate void Loaded(object sender);")]
    [InlineData("// the library is loaded by the runtime from the DllImport declaration")]
    [InlineData("")]
    public void Matcher_IgnoresOrdinaryInteropCode(string line)
    {
        Assert.False(IsDynamicNativeBinding(line));
    }

    [Theory]
    [InlineData("SetFirmwareEnvironmentVariableExW")]
    [InlineData("SetFirmwareEnvironmentVariableW")]
    [InlineData("NtSetSystemEnvironmentValueEx")]
    [InlineData("ZwSetSystemEnvironmentValueEx")]
    [InlineData("Tbsip_Submit_Command")]
    [InlineData("#42")]
    public void EntryPointCheck_FlagsFirmwareWritesTpmCommandsAndOrdinals(string entryPoint)
    {
        Assert.True(IsForbiddenEntryPoint(entryPoint));
    }

    [Theory]
    [InlineData("GetFirmwareEnvironmentVariableExW")]
    [InlineData("NtEnumerateSystemEnvironmentValuesEx")]
    [InlineData("GetFirmwareType")]
    [InlineData("OpenProcessToken")]
    [InlineData("LookupPrivilegeValueW")]
    [InlineData("AdjustTokenPrivileges")]
    [InlineData("CloseHandle")]
    [InlineData("GetCurrentProcess")]
    [InlineData("RtlGetLastNtStatus")]
    public void EntryPointCheck_AcceptsTheReadOnlyImports(string entryPoint)
    {
        Assert.False(IsForbiddenEntryPoint(entryPoint));
    }

    [Theory]
    [InlineData("NtSetSystemEnvironmentValueEx", true)]
    [InlineData("ZwSetSystemEnvironmentValueEx", true)]
    [InlineData("NtSetValueKey", true)]
    [InlineData("NtSetInformationThread", true)]
    [InlineData("NtEnumerateSystemEnvironmentValuesEx", false)]
    [InlineData("RtlGetLastNtStatus", false)]
    [InlineData("SetFirmwareEnvironmentVariableExW", false)]
    public void EntryPointCheck_RecognisesNtdllSetters(string entryPoint, bool expected)
    {
        Assert.Equal(expected, IsNtdllSetter(entryPoint));
    }

    /// <summary>True when the line contains a P/Invoke attribute or the extern keyword.</summary>
    /// <param name="line">One line of C# source.</param>
    /// <returns>True for a native declaration.</returns>
    internal static bool IsNativeDeclaration(string line) => NativeDeclarationPattern.IsMatch(line);

    /// <summary>True when the line binds a native export at run time.</summary>
    /// <param name="line">One line of C# source.</param>
    /// <returns>True for NativeLibrary, GetProcAddress, LoadLibrary*, GetDelegateForFunctionPointer or an unmanaged function pointer.</returns>
    internal static bool IsDynamicNativeBinding(string line) => DynamicBindingPattern.IsMatch(line);

    /// <summary>True for an import by ordinal ("#n") or a name <see cref="NoWriteGuardTests.FindViolations"/> flags.</summary>
    /// <param name="entryPoint">The entry-point name as emitted into metadata.</param>
    /// <returns>True when the import is forbidden.</returns>
    internal static bool IsForbiddenEntryPoint(string entryPoint) =>
        entryPoint.StartsWith('#') || NoWriteGuardTests.FindViolations(entryPoint).Count > 0;

    /// <summary>True for an ntdll entry point that sets something (Nt* or Zw* followed by Set).</summary>
    /// <param name="entryPoint">The entry-point name as emitted into metadata.</param>
    /// <returns>True when the import is a setter.</returns>
    internal static bool IsNtdllSetter(string entryPoint) => NtdllSetterPattern.IsMatch(entryPoint);

    /// <summary>The entry-point name of a "library!EntryPoint" pair.</summary>
    /// <param name="entry">One entry of <see cref="NativeEntryPoints"/>.</param>
    /// <returns>The part after "!".</returns>
    internal static string EntryPointName(string entry) => entry[(entry.IndexOf('!', StringComparison.Ordinal) + 1)..];

    /// <summary>
    /// Every P/Invoke the product assemblies import, as "library!EntryPoint", read from the
    /// DllImport metadata (a LibraryImport's generated stub carries one too), so constant
    /// concatenation in the source has already been folded into the final name.
    /// </summary>
    /// <returns>The imports, sorted ordinally.</returns>
    internal static IReadOnlyList<string> NativeEntryPoints()
    {
        const BindingFlags AllMethods = BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return ProductAssemblies.Types()
            .SelectMany(type => type.GetMethods(AllMethods))
            .Where(method => (method.Attributes & MethodAttributes.PinvokeImpl) != 0)
            .Select(method =>
            {
                DllImportAttribute? import = method.GetCustomAttribute<DllImportAttribute>();
                return (import?.Value ?? "?") + "!" + (import?.EntryPoint ?? method.Name);
            })
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
