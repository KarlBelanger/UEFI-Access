// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using System.Text.RegularExpressions;
using UefiAccess.Core.Settings;
using UefiAccess.Core.Settings.Dell;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The tripwires (item 7) around the Dell settings, which M4
/// only reads: the settings folder (src/UefiAccess.Core/Settings) names neither Dell Command |
/// Monitor's service class, which carries the methods that change a BIOS setting, nor its string
/// class, nor a WMI method call, a WMI write, the service tag or a process launch, comments
/// included; it reaches WMI only through WmiHelper, never through System.Management itself; and
/// every query it holds is one of the pinned SELECTs. Nothing under src/ or tools/ controls a
/// service or installs software, so the tool never starts the dcstor64 service or installs the
/// Visual C++ runtime or Dell's tools: it says what is missing and what to do. No public type or
/// member of the settings model is named like a change (Set, Write, Apply, Change, Enable,
/// Disable, Reset or Save as its first word), so nothing on the surface invites one. And no test
/// on this PC, which is the OptiPlex 7020 itself, reads Dell Command | Monitor or starts cctk: the
/// real readers, and the command line's production composition root that builds one, are composed
/// only in tests that run off Windows, while the seam constructors of the settings reader and the
/// providers, and the command line's factory overloads, which take the test's fakes and touch
/// nothing real, stay open to every test (section 7.1 needs them). Every check reads the sources
/// as text (with the one scanner, <see cref="SourceCalls"/>) or the built types by reflection: a
/// tripwire, not a proof. A name assembled at run time, reflection or dynamic in the product is
/// beyond it, so review stays the control. Gate G-A (work package step 1.1) brings these checks
/// before the folder holds a query; steps 1.2 and 1.4 add the pins of the three queries.
/// </summary>
public sealed class DellSettingsGuardTests
{
    /// <summary>The settings folder, relative to the repository root.</summary>
    private const string SettingsDirectory = "src/UefiAccess.Core/Settings";

    /// <summary>The namespace of the vendor-neutral settings model.</summary>
    private const string SettingsNamespace = "UefiAccess.Core.Settings";

    /// <summary>The namespace of the Dell settings.</summary>
    private const string DellSettingsNamespace = "UefiAccess.Core.Settings.Dell";

    private const string CctkReaderFile = "src/UefiAccess.Core/Settings/Dell/CctkReader.cs";
    private const string ThisFile = "tests/UefiAccess.Core.Tests/DellSettingsGuardTests.cs";

    /// <summary>
    /// What the settings folder never names, in any casing, comments included: Dell Command | Monitor's service class, whose methods change BIOS
    /// settings, and its string class, which holds no data from the unit; every way to invoke a WMI
    /// method or to write a WMI instance back; the serial number and the class that holds the
    /// service tag; and every way to start a process, which only ProcessRunner does.
    /// </summary>
    private static readonly string[] SettingsFolderBannedNames =
    {
        "DCIM_BIOSService",
        "BIOSService",
        "DCIM_BIOSString",
        "BIOSString",
        "InvokeMethod",
        "GetMethodParameters",
        "ExecMethod",
        "ManagementClass",
        ".Put(",
        ".Properties[",
        "SerialNumber",
        "Win32_SystemEnclosure",
        "ProcessStartInfo",
        "Process.Start",
    };

    /// <summary>
    /// The only WQL queries the settings folder may hold, each a string constant pinned to its text: the type's full name, the constant, and the text.
    /// Gate G-A pinned none, because the folder held no query yet; work package step 1.2 added
    /// DcmSettingsProvider.SettingsQuery (R2), step 1.4 DellMachineReader.ComputerSystemQuery and
    /// DellMachineReader.BiosQuery (R1), the machine's three fields and never the serial number.
    /// </summary>
    private static readonly (string Type, string Constant, string Text)[] PinnedQueries =
    {
        ("UefiAccess.Core.Settings.Dell.DcmSettingsProvider", "SettingsQuery", "SELECT AttributeName, CurrentValue, PossibleValues, PossibleValuesDescription, IsReadOnly FROM DCIM_BIOSEnumeration"),
        ("UefiAccess.Core.Settings.Dell.DellMachineReader", "ComputerSystemQuery", "SELECT Manufacturer, Model FROM Win32_ComputerSystem"),
        ("UefiAccess.Core.Settings.Dell.DellMachineReader", "BiosQuery", "SELECT SMBIOSBIOSVersion FROM Win32_BIOS"),
    };

    /// <summary>System.Management named in code: a using directive, a using alias or a qualified type name.</summary>
    private static readonly Regex SystemManagementPattern = new(@"\bSystem\s*\.\s*Management\b", RegexOptions.CultureInvariant);

    /// <summary>A System.Management type by its simple name, which a global using elsewhere could bring into scope.</summary>
    private static readonly Regex ManagementTypePattern = new(
        @"\b(?:Management(?:Object|ObjectSearcher|ObjectCollection|BaseObject|Scope|Path|Class|Exception|Status|Query|EventWatcher|NamedValueCollection)|ObjectQuery|WqlObjectQuery|SelectQuery|ConnectionOptions|InvokeMethodOptions|ObjectGetOptions|PutOptions)\b",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Service control and installation, in any casing: the
    /// ServiceController class; sc.exe; the PowerShell service commands that start, stop, restart,
    /// change, suspend, resume, create or remove a service, and the aliases sasv and spsv; net
    /// start and net stop (net.exe too); msiexec; winget; Install-Package; Install-Module;
    /// Add-AppxPackage; and the Visual C++ runtime's installer, vc_redist. Get-Service and a CIM
    /// read of Win32_Service only read, and stay allowed.
    /// </summary>
    private static readonly Regex ServiceControlOrInstallPattern = new(
        @"ServiceController"
        + @"|(?<![\w-])sc\.exe(?![\w-])"
        + @"|(?<![\w-])(?:Start|Stop|Restart|Set|Suspend|Resume|New|Remove)-Service(?![\w-])"
        + @"|(?<![\w-])(?:sasv|spsv)(?![\w-])"
        + @"|(?<![\w-])net(?:\.exe)?\s+(?:start|stop)(?![\w-])"
        + @"|msiexec|winget|Install-Package|Install-Module|Add-AppxPackage|vc_redist",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// A name that suggests a change:
    /// one of the change words as the name's first word, that is, not followed by a lower-case
    /// letter, compared ordinally and case-sensitively. Settings, SetupLocation and Enabled pass;
    /// SetValue, Apply and EnableX do not.
    /// </summary>
    private static readonly Regex ChangeWordPattern = new(@"^(?:Set|Write|Apply|Change|Enable|Disable|Reset|Save)(?![a-z])", RegexOptions.CultureInvariant);

    /// <summary>
    /// The real cctk launcher in a test, whose only constructor is the real one: CctkReader built
    /// with new (explicit or qualified), any declaration of that type (a variable, a field, a
    /// parameter, or a member that returns one, however it is then built), and RunCctk named at
    /// all, called or as a method group, since no fake carries that name (the provider's seam is its
    /// run delegate); the window's live source composed through
    /// LiveDellSettingsSource.CreateWindows; or the command line's production composition root,
    /// which builds the real Dell settings reader: CliPlatform.CreateWindows (called or as a method
    /// group), the one-argument CliServices.Create (called or as a method group; the five-argument
    /// one takes the test's platform), Program.Main, Program.BuildRootCommand() without a factory,
    /// and a command's one-argument Create (DellCommand.Create(verbose) and the like, whose
    /// two-argument form takes the test's factory). A fake with a prefixed name does not match,
    /// nor do typeof(CctkReader), its constants and a test method named RunCctk_....
    /// </summary>
    private static readonly Regex RealDellReaderPattern = new(
        @"\bnew\s+(?:global::)?(?:\w+\s*\.\s*)*CctkReader\s*\("
        + @"|\bCctkReader\??\s+@?\w+"
        + @"|(?<!\w)RunCctk(?!\w)"
        + @"|\bLiveDellSettingsSource\s*\.\s*CreateWindows\s*\("
        + @"|\bCliPlatform\s*\.\s*CreateWindows\b"
        + @"|\bCliServices\s*\.\s*Create\b(?!\s*\(\s*[^,()]+\s*,)"
        + @"|\bProgram\s*\.\s*Main\b"
        + @"|\bProgram\s*\.\s*BuildRootCommand\s*\(\s*\)"
        + @"|\b\w+Command\s*\.\s*Create\s*\(\s*\w+\s*\)",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// The Dell settings reader or a Dell settings provider built with new: explicit or qualified;
    /// target-typed into a variable or field of that type; or target-typed as the body of a member
    /// that returns that type (an expression body, or a return statement of a block body, see
    /// <see cref="DellComposerBlockPattern"/>). The arguments tell the logger-only constructor,
    /// which composes the real interfaces, from the one that takes the test's seams.
    /// </summary>
    private static readonly Regex DellProviderConstructionPattern = new(
        @"\bnew\s+(?:global::)?(?:\w+\s*\.\s*)*(?:DellSettingsReader|DcmSettingsProvider|CctkSettingsProvider)\s*(?=\()"
        + @"|\b(?:DellSettingsReader|DcmSettingsProvider|CctkSettingsProvider)\??\s+@?\w+\s*(?:\([^()]*\))?\s*(?:=>|=)\s*new\s*(?=\()",
        RegexOptions.CultureInvariant);

    /// <summary>A member that returns the Dell settings reader or a provider and has a block body (a method or a property), up to its "{".</summary>
    private static readonly Regex DellComposerBlockPattern = new(@"\b(?:DellSettingsReader|DcmSettingsProvider|CctkSettingsProvider)\??\s+@?\w+\s*(?:\([^()]*\))?\s*(?=\{)", RegexOptions.CultureInvariant);

    /// <summary>A target-typed new in a return statement.</summary>
    private static readonly Regex ReturnTargetTypedNewPattern = new(@"\breturn\s+new\s*(?=\()", RegexOptions.CultureInvariant);

    /// <summary>The Windows shell composed in a test.</summary>
    private static readonly Regex ShellCreateWindowsPattern = new(@"\bShellViewModel\s*\.\s*CreateWindows\s*\(", RegexOptions.CultureInvariant);

    /// <summary>The shell's Dell read, which a test that composes the Windows shell must never name.</summary>
    private static readonly Regex ShellDellReadPattern = new(@"\b(?:ReadDellSettingsAsync|ReadDellSettingsCommand)\b", RegexOptions.CultureInvariant);

    /// <summary>
    /// The [NonWindowsFact] attribute, alone or in a list of attributes, unless it sets Skip: the
    /// attribute skips the test on Windows in its constructor, and a named Skip (Skip = null)
    /// would undo that and run the test on this PC.
    /// </summary>
    private static readonly Regex NonWindowsFactPattern = new(@"\[[^\[\]]*\bNonWindowsFact(?:Attribute)?\b(?!\s*\([^)]*\bSkip\b)[^\[\]]*\]", RegexOptions.CultureInvariant);

    /// <summary>A Unicode escape in code, which could spell CctkReader or RunCctk where no pattern here would read it.</summary>
    private static readonly Regex UnicodeEscapePattern = new(@"\\u[0-9A-Fa-f]{4}|\\U[0-9A-Fa-f]{8}", RegexOptions.CultureInvariant);

    /// <summary>Any xunit fact or theory attribute ([Fact], [Theory], [NonWindowsFact] and the like), alone or in a list of attributes.</summary>
    private static readonly Regex TestMethodAttributePattern = new(@"\[[^\[\]]*\b\w*(?:Fact|Theory)(?:Attribute)?\b[^\[\]]*\]", RegexOptions.CultureInvariant);

    [Fact]
    public void SettingsFolderNamesNoServiceClassStringClassOrMethodCall()
    {
        IReadOnlyList<string> files = SettingsFiles();

        List<string> violations = new();
        foreach (string file in files)
        {
            string relative = RepositoryPaths.Relative(file);
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (string name in SettingsFolderBannedNames.Where(name => lines[i].Contains(name, StringComparison.OrdinalIgnoreCase)))
                {
                    violations.Add($"{relative}:{i + 1}: names {name}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "The settings folder names no service class, no string class, no WMI method call or write, no serial number and no process launch, not even in a comment:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void SettingsFolderDoesNotUseSystemManagementDirectly()
    {
        List<string> violations = new();
        foreach (string file in SettingsFiles().Where(path => path.EndsWith(".cs", StringComparison.Ordinal)))
        {
            string relative = RepositoryPaths.Relative(file);
            string code = SourceCalls.WithoutCommentsAndStrings(File.ReadAllText(file));
            foreach (int line in SourceCalls.LinesMatching(code, SystemManagementPattern))
            {
                violations.Add($"{relative}:{line}: names System.Management");
            }

            foreach (int line in SourceCalls.LinesMatching(code, ManagementTypePattern))
            {
                violations.Add($"{relative}:{line}: names a System.Management type");
            }
        }

        Assert.True(
            violations.Count == 0,
            "The settings folder reaches WMI only through WmiHelper:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void SettingsFolderSelectsOnlyThePinnedQueries()
    {
        // Reflection: each pinned constant exists and holds its text.
        Assembly core = typeof(CctkReader).Assembly;
        foreach ((string typeName, string constant, string text) in PinnedQueries)
        {
            Type? type = core.GetType(typeName);
            Assert.True(type is not null, typeName + " is gone; update the pin");
            FieldInfo? field = type!.GetField(constant, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.True(field is { IsLiteral: true } && field.FieldType == typeof(string), typeName + "." + constant + " must be a string constant");
            Assert.Equal(text, (string?)field!.GetRawConstantValue());
        }

        HashSet<string> allowed = new(PinnedQueries.Select(query => query.Text), StringComparer.Ordinal);

        // Reflection: no other string constant of the settings namespaces is a query.
        string[] constants = core.GetTypes()
            .Where(IsInSettingsNamespace)
            .SelectMany(type => type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(field => field.IsLiteral && field.FieldType == typeof(string))
                .Select(field => (Name: type.FullName + "." + field.Name, Value: (string?)field.GetRawConstantValue() ?? string.Empty)))
            .Where(constant => IsQuery(constant.Value) && !allowed.Contains(constant.Value))
            .Select(constant => constant.Name + " = \"" + constant.Value + "\"")
            .ToArray();
        Assert.True(constants.Length == 0, "A string constant of the settings namespaces is a query that is not pinned:" + Environment.NewLine + string.Join(Environment.NewLine, constants));

        // Source: every string literal of the folder that starts with SELECT, in any casing, is
        // exactly one of the pinned texts; a concatenation, an interpolation or an escape is not.
        List<string> violations = new();
        foreach (string file in SettingsFiles().Where(path => path.EndsWith(".cs", StringComparison.Ordinal)))
        {
            string relative = RepositoryPaths.Relative(file);
            foreach ((int line, string body) in StringLiterals(File.ReadAllText(file)))
            {
                if (IsQuery(body) && !allowed.Contains(body))
                {
                    violations.Add($"{relative}:{line}: \"{body}\"");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "The settings folder may select only the pinned queries (" + (allowed.Count == 0 ? "none yet" : string.Join("; ", allowed)) + "):" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [InlineData("internal const string Query = \"SELECT * FROM DCIM_BIOSEnumeration\";", "SELECT * FROM DCIM_BIOSEnumeration")]
    [InlineData("string wql = \"select Name from \" + className;", "select Name from ")]
    [InlineData("string wql = $\"SELECT {fields} FROM Win32_BIOS\";", "SELECT {fields} FROM Win32_BIOS")]
    [InlineData("string wql = @\"SELECT AttributeName FROM DCIM_BIOSEnumeration\";", "SELECT AttributeName FROM DCIM_BIOSEnumeration")]
    [InlineData("Log(\"x\", /* note */ \"  Select Model FROM Win32_ComputerSystem\");", "  Select Model FROM Win32_ComputerSystem")]
    public void StringLiterals_FindEveryQueryLiteral(string line, string expected)
    {
        (int Line, string Body)[] literals = StringLiterals("// SELECT in a comment is not a literal\n" + line + "\n").ToArray();

        Assert.Contains((2, expected), literals);
        Assert.True(IsQuery(expected));
        Assert.DoesNotContain(literals, literal => literal.Line == 1);
    }

    [Fact]
    public void NoServiceControlOrInstallInSourceOrTools()
    {
        IReadOnlyList<string> files = ServiceScanFiles();
        string[] relative = files.Select(RepositoryPaths.Relative).ToArray();
        Assert.Contains(CctkReaderFile, relative);
        Assert.Contains("tools/gate/Run-Gate.ps1", relative);
        Assert.Contains("tools/common/DataFolder.psm1", relative);
        Assert.Contains("tools/write-tests/Run-FailureCases.ps1", relative);
        Assert.Contains("src/UefiAccess.App/MainWindow.xaml", relative);

        List<string> violations = new();
        foreach (string file in files)
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match match in ServiceControlOrInstallPattern.Matches(lines[i]))
                {
                    violations.Add($"{RepositoryPaths.Relative(file)}:{i + 1}: {match.Value}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Nothing under src/ or tools/ starts, stops or changes a service or installs software; the tool says what is missing and what to do:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [InlineData("using ServiceController service = new(\"dcstor64\");")]
    [InlineData("System.ServiceProcess.ServiceController.GetServices();")]
    [InlineData("ProcessRunner.Run(scPath, null, timeout, _logger, \"start\", \"dcstor64\"); // sc.exe")]
    [InlineData("& sc.exe start dcstor64")]
    [InlineData("Start-Service -Name dcstor64")]
    [InlineData("stop-service dcstor64 -Force")]
    [InlineData("Restart-Service -Name dcstor64")]
    [InlineData("Set-Service -Name dcstor64 -StartupType Automatic")]
    [InlineData("sasv dcstor64")]
    [InlineData("& net start \"DSM SA Data Manager\"")]
    [InlineData("NET.EXE STOP dcstor64")]
    [InlineData("Start-Process msiexec.exe -ArgumentList '/i', $package -Wait")]
    [InlineData("winget install Microsoft.VCRedist.2015+.x64")]
    [InlineData("Install-Package -Name DellCommandMonitor")]
    [InlineData("Install-Module -Name SomeModule")]
    [InlineData("Add-AppxPackage -Path .\\app.msix")]
    [InlineData("& .\\VC_redist.x64.exe /install /quiet /norestart")]
    public void ServiceControlMatcher_FlagsEveryForm(string line)
    {
        Assert.Matches(ServiceControlOrInstallPattern, line);
    }

    [Theory]
    [InlineData("Get-Service dcstor64 | Format-List Name, Status, StartType")]
    [InlineData("$service = Get-CimInstance -ClassName Win32_Service -Filter \"Name='dcstor64'\"")]
    [InlineData("dotnet build UefiAccess.sln -c Release")]
    [InlineData("Its data service, DSM SA Data Manager, is probably not running; restart Windows, then try again. This application never starts it.")]
    [InlineData("Open the internet start page.")]
    [InlineData("private static readonly TimeSpan ServiceTimeout = TimeSpan.FromSeconds(60);")]
    public void ServiceControlMatcher_AcceptsReadsAndSentences(string line)
    {
        Assert.DoesNotMatch(ServiceControlOrInstallPattern, line);
    }

    [Fact]
    public void NoPublicSettingsMemberSuggestsAChange()
    {
        // Not vacuous: the namespaces are the ones the settings types are declared in, and since
        // work package step 1.2 the public model is among the types checked, since step 1.4 the
        // reader too.
        Assert.Equal(DellSettingsNamespace, typeof(CctkReader).Namespace);
        Assert.Equal(DellSettingsNamespace, typeof(CctkReadOption).Namespace);
        Type[] visible = typeof(CctkReader).Assembly.GetTypes().Where(type => type.IsVisible && IsInSettingsNamespace(type)).ToArray();
        Assert.Contains(typeof(FirmwareSetting), visible);
        Assert.Contains(typeof(DellAttributeCatalog), visible);
        Assert.Contains(typeof(DellSentences), visible);
        Assert.Contains(typeof(DellSettingsReader), visible);
        Assert.Contains(typeof(IDellSettingsReader), visible);

        List<string> problems = new();
        foreach (Type type in visible)
        {
            foreach (string name in PublicNames(type).Where(SuggestsAChange))
            {
                problems.Add(type.FullName + ": " + name);
            }
        }

        Assert.True(
            problems.Count == 0,
            "No public settings type or member is named like a change (Set, Write, Apply, Change, Enable, Disable, Reset or Save as its first word):" + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    [Theory]
    [InlineData("Settings", false)]
    [InlineData("SetupLocation", false)]
    [InlineData("Enabled", false)]
    [InlineData("Reader", false)]
    [InlineData("Writability", false)]
    [InlineData("SetValue", true)]
    [InlineData("Apply", true)]
    [InlineData("EnableX", true)]
    [InlineData("Set", true)]
    [InlineData("WriteSetting", true)]
    [InlineData("ResetToDefault", true)]
    [InlineData("SaveAs", true)]
    public void NoPublicSettingsMemberSuggestsAChange_JudgesOnlyTheFirstWord(string name, bool suggestsAChange)
    {
        Assert.Equal(suggestsAChange, SuggestsAChange(name));
    }

    [Fact]
    public void TestsNeverRunTheRealDellReader()
    {
        IReadOnlyList<string> testFiles = RepositoryPaths.Enumerate("tests", "*.cs");
        Assert.Contains(testFiles, file => RepositoryPaths.Relative(file) == ThisFile);

        // This file is scanned too: it names the real readers only in strings and comments.
        List<string> violations = new();
        foreach (string file in testFiles)
        {
            string relative = RepositoryPaths.Relative(file);
            violations.AddRange(RealDellReaderViolations(File.ReadAllText(file)).Select(violation => relative + ":" + violation));
        }

        Assert.True(
            violations.Count == 0,
            "No test on this PC reads Dell Command Monitor or starts cctk: the real Dell readers are composed only in [NonWindowsFact] tests, and a test that composes the Windows shell never reads the Dell settings:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [InlineData("[Fact]\npublic void T()\n{\n    DellSettingsResult result = new DellSettingsReader(null).Read(DellReadOptions.Default);\n}\n")]
    [InlineData("[Fact]\npublic void T()\n{\n    CctkReader reader = new(null);\n}\n")]
    [InlineData("[Theory]\n[InlineData(1)]\npublic void T(int n)\n{\n    var reader = new UefiAccess.Core.Settings.Dell.CctkReader(log);\n}\n")]
    [InlineData("[Fact]\npublic void T()\n{\n    DcmSettingsProvider provider = new(log);\n}\n")]
    [InlineData("[Fact]\npublic void T()\n{\n    var provider = new CctkSettingsProvider(log);\n}\n")]
    [InlineData("[Fact]\npublic void T()\n{\n    var provider = new CctkSettingsProvider();\n}\n")]
    [InlineData("[Fact]\npublic async Task T()\n{\n    await LiveDellSettingsSource.CreateWindows(null).ReadAsync(CancellationToken.None);\n}\n")]
    [InlineData("private static readonly CctkReader Shared = new(null);\n[NonWindowsFact]\npublic void T()\n{\n    Assert.False(Shared.Found);\n}\n")]
    [InlineData("[NonWindowsFact]\npublic void T() => Assert.NotNull(Make());\nprivate static CctkReader Make() => new CctkReader(null);\n")]
    [InlineData("[Fact]\npublic async Task T()\n{\n    ShellViewModel shell = ShellViewModel.CreateWindows(announcer, focus, platform, null);\n    await shell.ReadDellSettingsAsync();\n}\n")]
    [InlineData("[Fact]\npublic void T()\n{\n    ShellViewModel shell = ShellViewModel.CreateWindows(announcer, focus, platform, null);\n    shell.ReadDellSettingsCommand.Execute(null);\n}\n")]
    [InlineData("[Fact]\npublic void T()\n{\n    Make().RunCctk(CctkReadOption.SecureBoot, TimeSpan.FromSeconds(1));\n}\nprivate static CctkReader Make() => new(null);\n")]
    [InlineData("[Fact]\npublic void T()\n{\n    Func<CctkReadOption, TimeSpan, CctkRunOutcome> run = Shared.RunCctk;\n}\n")]
    [InlineData("[Fact]\npublic void T()\n{\n    List<CctkReader> readers = new() { new(null) };\n    readers[0].RunCctk(CctkReadOption.NumLock, TimeSpan.FromSeconds(1));\n}\n")]
    [InlineData("[Fact]\npublic void T()\n{\n    CctkRe\\u0061der reader = new(null);\n}\n")]
    [InlineData("[Fact]\npublic void T() => Make().Read(DellReadOptions.Default);\nprivate static DellSettingsReader Make() => new(null);\n")]
    [InlineData("[Fact]\npublic void T() => Make().Read(DellReadOptions.Default);\nprivate static DellSettingsReader Make()\n{\n    return new(null);\n}\n")]
    [InlineData("private static CctkSettingsProvider Provider { get { return new(log); } }\n")]
    [InlineData("private static readonly DcmSettingsProvider Shared = new(log);\n")]
    [InlineData("[NonWindowsFact(Skip = null)]\npublic void T()\n{\n    CctkReader reader = new(null);\n}\n")]
    [InlineData("[Fact]\npublic async Task T() => await Compose().ReadDellSettingsAsync();\nprivate static ShellViewModel Compose() => ShellViewModel.CreateWindows(a, f, p, null);\n")]
    [InlineData("[Fact]\npublic void T() => CliTestHarness.Run(CliPlatform.CreateWindows(null), \"dell\");\n")]
    [InlineData("[Fact]\npublic void T()\n{\n    Func<ILogger, CliPlatform> windows = CliPlatform.CreateWindows;\n}\n")]
    [InlineData("[Fact]\npublic void T() => Program.Run(new[] { \"dell\", \"--source\", \"cctk\" }, CliServices.Create, o, e);\n")]
    [InlineData("[Fact]\npublic void T()\n{\n    CliServices services = CliServices.Create(true);\n}\n")]
    [InlineData("[Fact]\npublic void T()\n{\n    RootCommand root = Program.BuildRootCommand();\n}\n")]
    [InlineData("[Fact]\npublic void T() => Program.Main(new[] { \"baseline\" });\n")]
    [InlineData("[Fact]\npublic void T()\n{\n    Command command = DellCommand.Create(verbose);\n}\n")]
    [InlineData("[Fact]\npublic void T() => Assert.NotNull(BaselineCommand.Create(verbose));\n")]
    public void RealDellReaderCheck_FlagsEveryOtherSite(string source)
    {
        Assert.NotEmpty(RealDellReaderViolations(source));
    }

    [Theory]
    [InlineData("[NonWindowsFact]\npublic void T()\n{\n    DellSettingsResult result = new DellSettingsReader(null).Read(DellReadOptions.Default);\n    Assert.NotEmpty(result.Problems);\n}\n")]
    [InlineData("[NonWindowsFact]\npublic void T()\n{\n    CctkReader reader = new(null);\n    Assert.False(reader.Found);\n}\n")]
    [InlineData("[NonWindowsFact]\npublic void T() => Assert.False(new CctkReader(null).Found);\n")]
    [InlineData("[Fact]\npublic void T()\n{\n    DcmSettingsProvider provider = new(log, query, namespaceExists);\n    var other = new CctkSettingsProvider(log, () => true, run);\n}\n")]
    [InlineData("[Fact]\npublic void T()\n{\n    LiveDellSettingsSource source = new(new FakeDellSettingsReader());\n    IDellSettingsReader reader = new GatedDellSettingsReader();\n}\n")]
    [InlineData("[Theory]\n[InlineData(\"new DellSettingsReader(null)\")]\npublic void T(string line)\n{\n    // new CctkReader(null) is never called here\n    Assert.NotEmpty(line);\n}\n")]
    [InlineData("[Fact]\npublic void T()\n{\n    ShellViewModel shell = ShellViewModel.CreateWindows(announcer, focus, platform, null);\n    Assert.Equal(ShellState.Idle, shell.State);\n}\n[Fact]\npublic async Task U()\n{\n    ShellViewModel shell = Rig.Shell();\n    await shell.ReadDellSettingsAsync();\n}\n")]
    [InlineData("[Fact]\npublic void T()\n{\n    DellSettingsReader reader = new(log, dcm, cctk, machine, native, clock, elapsed, () => true);\n    var other = new DellSettingsReader(log, dcm, cctk, machine, native, clock, elapsed, isWindows);\n}\nprivate static DellSettingsReader Make() => new(log, dcm, cctk, machine, native, clock, elapsed, isWindows);\n")]
    [InlineData("[Fact]\npublic void T()\n{\n    Assert.Equal(\"--numlock\", CctkReader.ArgumentFor(CctkReadOption.NumLock));\n    Assert.Equal(\"UefiAccess.Core.Settings.Dell\", typeof(CctkReader).Namespace);\n}\n[NonWindowsFact]\npublic void RunCctk_WithoutCctk_StartsNothing() => Assert.False(new CctkReader(null).RunCctk(CctkReadOption.NumLock, TimeSpan.FromSeconds(1)).Started);\n")]
    [InlineData("[NonWindowsFact(Timeout = 1000)]\npublic void T()\n{\n    CctkReader reader = new(null);\n    Assert.False(reader.Found);\n}\n")]
    [InlineData("[Fact]\npublic void T()\n{\n    CliServices services = CliServices.Create(verbose, _ => platform, output, error, Serilog.Core.Logger.None);\n}\n")]
    [InlineData("[Fact]\npublic void T() => Program.Run(args, verbose => CliServices.Create(verbose, _ => platform, output, error, Logger.None), output, error);\n")]
    [InlineData("[Fact]\npublic void T()\n{\n    Command command = DellCommand.Create(verbose, factory);\n    RootCommand root = Program.BuildRootCommand(factory);\n    CliPlatform platform = CliTestHarness.CreatePlatform();\n}\n")]
    [InlineData("[NonWindowsFact]\npublic void T() => CliTestHarness.Run(CliPlatform.CreateWindows(null), \"dell\");\n")]
    public void RealDellReaderCheck_AcceptsNonWindowsFactsLiteralsAndFakes(string source)
    {
        Assert.Empty(RealDellReaderViolations(source));
    }

    /// <summary>
    /// What a test source does that could read the Dell interfaces of this PC,
    /// read with comments and string literals blanked: the real cctk launcher (CctkReader built or
    /// declared, RunCctk named), the window's live source (LiveDellSettingsSource.CreateWindows) or
    /// the command line's production composition root (CliPlatform.CreateWindows, the one-argument
    /// CliServices.Create, Program.Main, Program.BuildRootCommand() and a command's one-argument
    /// Create), anywhere but inside a [NonWindowsFact] method that does not set Skip, which is
    /// skipped on Windows; the Dell settings reader or a Dell settings provider built with its
    /// logger-only constructor (one argument or none) anywhere but there, the seam constructors
    /// taking the test's fakes; ShellViewModel.CreateWindows outside the body of a test method,
    /// where a helper, field or property could hand the composed shell to a test that reads the
    /// Dell settings; a test method that composes the Windows shell and names ReadDellSettingsAsync
    /// or ReadDellSettingsCommand; and a Unicode escape in code, which could spell any of these names.
    /// Composing the shell reads nothing, so the existing tests that do it stay allowed.
    /// </summary>
    /// <param name="source">The C# source of a test file.</param>
    /// <returns>"line: description" for each site; empty when the file cannot run a real Dell reader on Windows.</returns>
    internal static IReadOnlyList<string> RealDellReaderViolations(string source)
    {
        string code = SourceCalls.WithoutCommentsAndStrings(source);
        IReadOnlyList<(int Start, int End)> nonWindowsFacts = AttributedMethodBodies(code, NonWindowsFactPattern);
        bool InNonWindowsFact(int index) => nonWindowsFacts.Any(body => index >= body.Start && index < body.End);

        List<string> violations = new();
        foreach (Match match in RealDellReaderPattern.Matches(code).Where(match => !InNonWindowsFact(match.Index)))
        {
            violations.Add($"{SourceCalls.LineOf(code, match.Index)}: composes a real Dell reader ({Regex.Replace(match.Value, @"\s+", " ").Trim()}) outside a [NonWindowsFact] test");
        }

        List<int> newSites = DellProviderConstructionPattern.Matches(code).Select(match => match.Index + match.Length).ToList();
        foreach (Match member in DellComposerBlockPattern.Matches(code))
        {
            int open = member.Index + member.Length;
            int close = MatchingClose(code, open, '{', '}');
            if (close > open)
            {
                newSites.AddRange(ReturnTargetTypedNewPattern.Matches(code[open..close]).Select(match => open + match.Index + match.Length));
            }
        }

        foreach (int open in newSites.Where(open => !InNonWindowsFact(open)).Distinct())
        {
            int close = MatchingClose(code, open, '(', ')');
            if (close > open && SourceCalls.SplitTopLevelArguments(code[(open + 1)..close]).Count <= 1)
            {
                violations.Add($"{SourceCalls.LineOf(code, open)}: builds the Dell settings reader or a Dell settings provider with its logger-only constructor, which composes the real interfaces, outside a [NonWindowsFact] test");
            }
        }

        IReadOnlyList<(int Start, int End)> testMethods = AttributedMethodBodies(code, TestMethodAttributePattern);
        foreach (Match match in ShellCreateWindowsPattern.Matches(code).Where(match => !testMethods.Any(body => match.Index >= body.Start && match.Index < body.End)))
        {
            violations.Add($"{SourceCalls.LineOf(code, match.Index)}: composes the Windows shell outside the body of a test method, where no check can follow it to a Dell read");
        }

        foreach ((int start, int end) in testMethods)
        {
            string body = code[start..end];
            Match read = ShellDellReadPattern.Match(body);
            if (ShellCreateWindowsPattern.IsMatch(body) && read.Success)
            {
                violations.Add($"{SourceCalls.LineOf(code, start + read.Index)}: a test that composes the Windows shell names {read.Value}");
            }
        }

        foreach (int line in SourceCalls.LinesMatching(code, UnicodeEscapePattern))
        {
            violations.Add($"{line}: spells a name with a Unicode escape, which hides it from this check");
        }

        return violations;
    }

    /// <summary>
    /// The bodies of the methods marked with an attribute the pattern matches, in blanked code, as
    /// spans: from the "{" to the matching "}", or from "=&gt;" to the ";" that ends an
    /// expression-bodied method. Further attributes between the matched one and the method are
    /// skipped.
    /// </summary>
    private static IReadOnlyList<(int Start, int End)> AttributedMethodBodies(string code, Regex attribute)
    {
        List<(int Start, int End)> bodies = new();
        foreach (Match match in attribute.Matches(code))
        {
            int position = match.Index + match.Length;
            while (true)
            {
                while (position < code.Length && char.IsWhiteSpace(code[position]))
                {
                    position++;
                }

                if (position >= code.Length || code[position] != '[')
                {
                    break;
                }

                int closeBracket = MatchingClose(code, position, '[', ']');
                if (closeBracket < 0)
                {
                    break;
                }

                position = closeBracket + 1;
            }

            int open = code.IndexOf('(', position);
            int close = open < 0 ? -1 : MatchingClose(code, open, '(', ')');
            if (close < 0)
            {
                continue;
            }

            int next = close + 1;
            while (next < code.Length && char.IsWhiteSpace(code[next]))
            {
                next++;
            }

            if (next < code.Length && code[next] == '{')
            {
                int end = MatchingClose(code, next, '{', '}');
                if (end > next)
                {
                    bodies.Add((next, end + 1));
                }
            }
            else if (code.AsSpan(next).StartsWith("=>", StringComparison.Ordinal))
            {
                int end = code.IndexOf(';', next);
                bodies.Add((next, end < 0 ? code.Length : end + 1));
            }
        }

        return bodies;
    }

    /// <summary>The index of the <paramref name="close"/> that balances the <paramref name="open"/> at <paramref name="index"/> in blanked code; -1 when unbalanced.</summary>
    private static int MatchingClose(string code, int index, char open, char close)
    {
        int depth = 0;
        for (int i = index; i < code.Length; i++)
        {
            if (code[i] == open)
            {
                depth++;
            }
            else if (code[i] == close)
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// Every string literal of a C# source outside comments, with its 1-based line and its text
    /// between the quotes as written (escapes not resolved; the "@" or "$" before it not
    /// included). A literal is found where the source with comments blanked and the source with
    /// comments and literals blanked differ; it runs to its closing quote, the last such
    /// difference before the next code character.
    /// </summary>
    internal static IEnumerable<(int Line, string Body)> StringLiterals(string source)
    {
        string text = SourceCalls.WithoutComments(source);
        string code = SourceCalls.WithoutCommentsAndStrings(source);
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] != '"' || code[i] == '"')
            {
                i++;
                continue;
            }

            int start = i;
            int last = i;
            int j = i + 1;
            while (j < text.Length && char.IsWhiteSpace(code[j]))
            {
                if (text[j] != code[j])
                {
                    last = j;
                }

                j++;
            }

            yield return (SourceCalls.LineOf(text, start), text[start..(last + 1)].Trim('"'));
            i = last + 1;
        }
    }

    /// <summary>True when a literal or constant is a WQL query: it starts, after any spaces, with the word SELECT in any casing.</summary>
    private static bool IsQuery(string text) => Regex.IsMatch(text, @"^\s*SELECT\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>True when a name has one of the change words as its first word (<see cref="ChangeWordPattern"/>).</summary>
    private static bool SuggestsAChange(string name) => ChangeWordPattern.IsMatch(name);

    /// <summary>True for a type of the settings model's namespace or of the Dell settings' namespace.</summary>
    private static bool IsInSettingsNamespace(Type type) => type.Namespace is SettingsNamespace or DellSettingsNamespace;

    /// <summary>
    /// The names a public type shows: its own; every public property, field and event, instance or
    /// static, inherited ones included; and every public method that is not a special name, so the
    /// accessors of properties (set_ of an init-only one among them) and the operators are left to
    /// their properties and types.
    /// </summary>
    private static IEnumerable<string> PublicNames(Type type)
    {
        const BindingFlags Public = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
        return new[] { type.Name }
            .Concat(type.GetProperties(Public).Select(property => property.Name))
            .Concat(type.GetFields(Public).Select(field => field.Name))
            .Concat(type.GetEvents(Public).Select(@event => @event.Name))
            .Concat(type.GetMethods(Public).Where(method => !method.IsSpecialName).Select(method => method.Name));
    }

    /// <summary>Every file of the settings folder, comments and all; the folder must exist and hold the reader.</summary>
    private static IReadOnlyList<string> SettingsFiles()
    {
        IReadOnlyList<string> files = RepositoryPaths.Enumerate(SettingsDirectory, "*");
        Assert.Contains(files, file => RepositoryPaths.Relative(file) == CctkReaderFile);
        return files;
    }

    /// <summary>
    /// Every file under src/ and tools/ that the service and install scan reads: all of them, build
    /// output excepted, and the sample baseline's variable payloads (vars/*.bin), which are firmware
    /// data, not text.
    /// </summary>
    private static IReadOnlyList<string> ServiceScanFiles()
    {
        return RepositoryPaths.Enumerate("src", "*")
            .Concat(RepositoryPaths.Enumerate("tools", "*"))
            .Where(path => !path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}
