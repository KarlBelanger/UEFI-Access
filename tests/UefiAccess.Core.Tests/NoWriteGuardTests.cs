// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UefiAccess.Core.Platform;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Settings.Dell;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// Enforces the write guarantee of M3, which grew out of the read-only guarantee of M1 and Alpha 1
/// (M2): no C# or XAML file under src/ and no PowerShell script under tools/ may name a
/// firmware-variable, boot-configuration, Dell BIOS, BitLocker, TPM or registry write path, in
/// code, comments, or string literals, except at the three write sites, each of which may name one literal on an exact number of lines
/// (<see cref="WriteSiteAllowances"/>): the declaration of the single firmware write import, the
/// writer that calls it, and the BitLocker suspender that invokes DisableKeyProtectors once with
/// an explicit one-restart count. A line holding the allowed literal is judged as if it held the
/// read-only counterpart instead, so everything else on it is still checked; every other
/// forbidden name stays forbidden in those files, and those names stay forbidden everywhere else.
/// What the allowed lines do is constrained by the file checks
/// (<see cref="BitLockerSuspender_SuspendsForOneRestartThroughTheOneConstant"/>, the Safety
/// guard tests) and pinned by reflection, and reviewed at every change. The registry is
/// one because Windows' own Secure Boot servicing, which writes the db, dbx and KEK variables, is
/// driven through registry values and a scheduled task. The scan is textual and runs on every
/// line; <see cref="FindViolations"/> lists the shapes it matches, and nothing else is checked.
/// Names are compared without regard to case, as PowerShell does, except quoted WMI method names
/// and the two allowed cctk options, which must match exactly. A registry key can be written only
/// once it is open for writing, so the scan catches the writable open (OpenSubKey with anything
/// but false as its second argument, CreateSubKey) rather than every SetValue, a name WPF and UI
/// Automation use too. The Registry class's base keys come open for writing: SetValue on one is caught as
/// Registry.SetValue or Registry.LocalMachine.SetValue, but not on a base key first stored in a
/// variable, which reaches only the hive's root and not the Secure Boot keys. The patterns are
/// deliberately strict: "manage-bde", wmic and the Dell Command | PowerShell Provider
/// (DellBIOSProvider, its DellSmbios: drive) fail wherever they appear, even in a PowerShell
/// comment, because the scripts must never run them either. A text scan cannot see what a constant
/// holds, so reflection pins the constants the scan trusts (the WMI method names on the allow-list,
/// the cctk arguments and the PowerShell command), and source tests check what the three process
/// launches pass. Since M4 the Dell settings reader,
/// Settings/Dell/CctkReader.cs, runs cctk with nine read options; the line scan's allow-list is not
/// widened for them, so every other file, PowerShell under tools/ included, still passes only the
/// probe's two. The reader needs no allowance, because no line of it puts an option after the word
/// cctk: its nine constants are pinned by reflection, ArgumentFor by its enumeration and its shape,
/// its one launch (the fixed program path, the 15-second clamp, ArgumentFor(option) alone) by
/// <see cref="CctkReaderLaunchViolations"/>, and its code as a whole by a digest, so any change to
/// it is reviewed again at gate G-A. A constant folded by the compiler, "Suspend-Bit" +
/// "Locker" for example, is checked in the built assemblies by
/// <see cref="CompiledStringGuardTests"/>, native entry points by
/// <see cref="NativeLocationGuardTests"/>, and <see cref="ProcessLaunchGuardTests"/> limits
/// which files may start a process at all. PowerShell that runs a string (Invoke-Expression, a
/// script block built from text, -EncodedCommand) is forbidden, because no scan could read it.
/// None of these checks can see a command whose name is assembled at run time and run through a
/// variable ("&amp; $name"), nor the WPF App's compiled strings, which the test project cannot
/// load. The matchers are exercised against synthetic lines as well, so the guard's own coverage
/// is tested.
/// </summary>
public sealed class NoWriteGuardTests
{
    /// <summary>
    /// The read-only Win32_EncryptableVolume methods the BitLocker inspector calls. They are the
    /// only WMI methods the product may invoke; every other method fails the guard, including the
    /// Get* readers that return a recovery password or key material.
    /// </summary>
    private static readonly string[] AllowedWmiMethods = { "GetKeyProtectors", "GetKeyProtectorType", "GetKeyProtectorPlatformValidationProfile" };

    /// <summary>
    /// The constants through which BitLockerInspector passes <see cref="AllowedWmiMethods"/>, with
    /// the value each must hold. A WMI invocation may name one of these instead of a literal;
    /// <see cref="AllowListedWmiMethodConstantsHoldTheReadOnlyNames"/> pins their values.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> AllowedWmiMethodConstants = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["GetKeyProtectorsMethod"] = "GetKeyProtectors",
        ["GetKeyProtectorTypeMethod"] = "GetKeyProtectorType",
        ["GetPlatformValidationProfileMethod"] = "GetKeyProtectorPlatformValidationProfile",
    };

    /// <summary>
    /// The three places M3 lets one forbidden name stand: the declaration of the single write import under Native/, the writer that
    /// calls it through its native seam, and the BitLocker suspender that invokes
    /// DisableKeyProtectors. A line of the file that holds the literal, in any casing, is judged
    /// as if it held the read-only counterpart (the read import; the GetKeyProtectors reader and
    /// its constant), so the allowed name and, for the suspender, its WMI invocation are accepted
    /// while everything else on the line is checked as on any other line. The number of lines
    /// holding the literal, comments included, must equal the count here exactly, so a second
    /// mention cannot creep in, and the file must exist. Every other forbidden literal stays
    /// forbidden in these files, NtSet and ZwSet names included, and these literals stay
    /// forbidden in every other file. What the allowed lines may do is constrained by
    /// <see cref="BitLockerSuspender_SuspendsForOneRestartThroughTheOneConstant"/> and by the
    /// Safety guard tests (WriteCallSiteGuardTests), which pin the constants by reflection and
    /// check the calls themselves.
    /// </summary>
    internal static readonly WriteSiteAllowance[] WriteSiteAllowances =
    {
        // Each count is the number of lines of the file that hold the literal, in any casing, comments and strings included.
        new("src/UefiAccess.Core/Native/Kernel32.cs", "SetFirmwareEnvironmentVariable", "GetFirmwareEnvironmentVariable", 1), // exact count; update with the file
        new("src/UefiAccess.Core/Safety/FirmwareVariableWriter.cs", "SetFirmwareEnvironmentVariable", "GetFirmwareEnvironmentVariable", 1), // exact count; update with the file
        new("src/UefiAccess.Core/Safety/BitLockerSuspender.cs", "DisableKeyProtectors", "GetKeyProtectors", 4), // exact count; update with the file
    };

    /// <summary>
    /// The files of <see cref="WriteSiteAllowances"/>, each once: the list the README's guarantee
    /// must name.
    /// </summary>
    internal static IReadOnlyList<string> WriteSiteFiles { get; } = WriteSiteAllowances.Select(allowance => allowance.File).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>The suspender's constant that names DisableKeyProtectors, the one method the suspend check lets an invocation pass.</summary>
    private const string SuspendMethodConstant = "DisableKeyProtectorsMethod";

    /// <summary>
    /// An indexer assignment, key and value, such as ["DisableCount"] = OneRestart; on a WMI
    /// parameter object. A comparison (==) and a lambda (=&gt;) are not assignments.
    /// </summary>
    private static readonly Regex IndexerAssignmentPattern = new(@"\[(?<key>[^\[\]\n]*)\]\s*=(?![=>])\s*(?<value>[^;]+);", CaseSensitive);

    /// <summary>The ways to invoke a WMI method or set a WMI value other than InvokeMethod and an indexer: ExecMethod, SetPropertyValue, the Properties collection.</summary>
    private static readonly Regex OtherWmiWritePattern = new(@"\bExecMethod\b|\bSetPropertyValue\b|\.\s*Properties\s*\[", CaseSensitive);

    /// <summary>OneRestart assigned, compounded or passed by reference; its constant declaration is told apart by its line.</summary>
    private static readonly Regex OneRestartAssignedPattern = new(@"(?<![\w.])OneRestart\s*(?:[-+*/%&|^]=|<<=|>>=|\?\?=|\+\+|--|=(?![=>]))|(?:\+\+|--)\s*OneRestart\b|\b(?:out|ref)\s+OneRestart\b", CaseSensitive);

    /// <summary>
    /// The only cctk options the line scan lets follow the word cctk, exactly as written: the M1
    /// probe's two. Unchanged since M4: the Dell settings reader's nine read options are pinned by
    /// reflection and by its launch check instead, so no other
    /// file, script or comment can pass them.
    /// </summary>
    private static readonly string[] AllowedCctkOptions = { "--version", "--secureboot" };

    /// <summary>The Dell settings reader, the one file that runs cctk with the nine read options.</summary>
    private const string CctkReaderFile = "src/UefiAccess.Core/Settings/Dell/CctkReader.cs";

    /// <summary>The probe, which runs cctk with its own two options through its own RunCctk.</summary>
    private const string DellInterfaceProbeFile = "src/UefiAccess.Core/Platform/DellInterfaceProbe.cs";

    /// <summary>The only file that may call the reader's RunCctk, inside the lambda that composes it.</summary>
    private const string CctkSettingsProviderFile = "src/UefiAccess.Core/Settings/Dell/CctkSettingsProvider.cs";

    /// <summary>
    /// The nine read options of <see cref="CctkReader"/>, written out here in the order of
    /// <see cref="CctkReadOption"/> so the test does not read them from the class it checks
    ///. Each is an option name alone, never "=value", and none names a file to apply or to
    /// write.
    /// </summary>
    private static readonly string[] CctkReadOptionArguments =
    {
        "--secureboot",
        "--embsataraid",
        "--tpm",
        "--tpmactivation",
        "--virtualization",
        "--vtfordirectio",
        "--embnic1",
        "--wakeonlan",
        "--numlock",
    };

    /// <summary>RunCctk's option assigned, compounded, stepped or passed by reference (in included); a comparison (==) and a lambda (=&gt;) are not assignments.</summary>
    private static readonly Regex OptionAssignedPattern = new(@"(?<![\w.])option\s*(?:[-+*/%&|^]=|<<=|>>=|\?\?=|\+\+|--|=(?![=>]))|(?:\+\+|--)\s*option\b|\b(?:out|ref|in)\s+option\b", CaseSensitive);

    /// <summary>The reader's program path assigned in any way, with the value assigned (up to the ";"), or passed by reference (in included, which Unsafe.AsRef turns into a writable one).</summary>
    private static readonly Regex CctkPathAssignedPattern = new(@"(?<![\w.])(?:this\s*\.\s*)?_cctkPath\s*(?:[-+*/%&|^]=|<<=|>>=|\?\?=|=(?![=>]))(?<value>[^;]*);|\b(?:out|ref|in)\s+(?:this\s*\.\s*)?_cctkPath\b", CaseSensitive);

    /// <summary>The program path named in code, in any role: the constructor may name it once, in its one assignment.</summary>
    private static readonly Regex CctkPathMentionPattern = new(@"(?<!\w)_cctkPath(?!\w)", CaseSensitive);

    /// <summary>An init accessor with a body, which may assign a read-only field as a constructor can.</summary>
    private static readonly Regex InitAccessorPattern = new(@"\binit\s*(?:=>|\{)", CaseSensitive);

    /// <summary>ProcessRunner.Run named in code, called or not, however laid out: across lines, or with the verbatim "@Run".</summary>
    private static readonly Regex ProcessRunnerRunMentionPattern = new(@"(?<!\w)ProcessRunner\s*\.\s*@?Run(?!\w)", CaseSensitive);

    /// <summary>A ProcessRunner.Run call, however laid out.</summary>
    private static readonly Regex ProcessRunnerRunCallPattern = new(@"(?<!\w)ProcessRunner\s*\.\s*@?Run\s*\(", CaseSensitive);

    /// <summary>
    /// The one shape ArgumentFor's body may have, read with comments and literals blanked: a switch
    /// on option whose arms each map a member of CctkReadOption to that member's own constant
    /// (SecureBoot to SecureBootArgument), then a discard arm that throws. So no arm can serve a
    /// value outside the enumeration, such as (CctkReadOption)9, or return anything but a constant
    /// the reflection pin reads.
    /// </summary>
    private static readonly Regex ArgumentForShapePattern = new(
        @"^\s*option\s+switch\s*\{(?:\s*CctkReadOption\s*\.\s*(?<member>\w+)\s*=>\s*\k<member>Argument\s*,)+\s*_\s*=>\s*throw\s+new\s+ArgumentOutOfRangeException\s*\(\s*nameof\s*\(\s*option\s*\)\s*\)\s*,?\s*\}\s*$",
        CaseSensitive);

    /// <summary>
    /// The three type names the launch check trusts as text in the reader, in code, where they are
    /// not followed by "." as a use of the real type is: a local, a member, a parameter, a nested
    /// type or a using alias of that name, any of which would replace the type.
    /// </summary>
    private static readonly Regex TrustedNameRebindingPattern = new(@"(?<![\w.])(?:DellInterfaceProbe|CctkSettingsProvider|ProcessRunner)\b(?!\s*\.)", CaseSensitive);

    /// <summary>A "using static" directive or a using alias, either of which hides what a name in the file binds to (a bare Run( could be ProcessRunner's).</summary>
    private static readonly Regex HidingUsingPattern = new(@"\busing\s+(?:static\b|@?\w+\s*=)", CaseSensitive);

    /// <summary>
    /// The names the launch check trusts in the reader, each with the one type it must bind to. No
    /// other type of Core may carry one of these names (a type in an enclosing namespace would win
    /// over the using directive that imports the real one), and no using alias under src/ may take
    /// one (a global alias would win too).
    /// </summary>
    private static readonly (string Name, Type Real)[] TrustedReaderTypes =
    {
        ("DellInterfaceProbe", typeof(DellInterfaceProbe)),
        ("CctkSettingsProvider", typeof(CctkSettingsProvider)),
        ("ProcessRunner", typeof(ProcessRunner)),
        ("CctkReadOption", typeof(CctkReadOption)),
    };

    /// <summary>
    /// SHA-256 of the code of CctkReader.cs as reviewed at gate G-A, comments and whitespace
    /// removed (<see cref="ReaderCodeDigest"/>). Changing the reader is a guard change
    ///: update this only with the review of the new code.
    /// </summary>
    private const string ReviewedCctkReaderCodeSha256 = "62F576567C5FCE0FEFC006C7A70DD0EE3DBA5F03EBEE01D622B1CD22C33A7514";

    /// <summary>The one value the reader's program path may be assigned: the probe's search of the two Program Files known folders.</summary>
    private static readonly Regex FindCctkValuePattern = new(@"^DellInterfaceProbe\s*\.\s*FindCctk\s*\(\s*_?logger\s*\)$", CaseSensitive);

    /// <summary>The reader's program path declared as a read-only field without an initializer.</summary>
    private static readonly Regex CctkPathFieldPattern = new(@"\bprivate\s+readonly\s+string\s*\?\s+_cctkPath\s*;", CaseSensitive);

    /// <summary>A call of RunCctk, as code; in a file other than the reader and the probe, a call of the reader's.</summary>
    private static readonly Regex RunCctkCallPattern = new(@"(?<!\w)RunCctk\s*\(", CaseSensitive);

    /// <summary>A character that cannot occur in C# code, put where a string or character literal stood (see <see cref="WithLiteralsMarked"/>).</summary>
    private const char LiteralMark = '\u0001';

    /// <summary>The switches the Secure Boot policy inspector may pass powershell.exe before "-Command", as C# literals.</summary>
    private static readonly string[] AllowedPowerShellSwitches = { "\"-NoProfile\"", "\"-NonInteractive\"", "\"-NoLogo\"" };

    /// <summary>
    /// The statements the -Command script may hold: the read-only cmdlet, and switching the child's
    /// own console output to UTF-8, which changes nothing outside that PowerShell process.
    /// </summary>
    private static readonly string[] AllowedPowerShellStatements = { "Confirm-SecureBootUEFI", "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8" };

    /// <summary>
    /// Names that must not appear anywhere, in any casing: the firmware-variable write APIs, the
    /// Dell BIOS write methods, the Dell Command | PowerShell Provider (its module and its
    /// DellSmbios: drive, where Set-Item changes a BIOS setting), manage-bde, the tool (bcdboot) and
    /// the boot manager object that write firmware boot entries ({fwbootmgr} and its GUID), the
    /// BitLocker key-protector writers, raw TPM command submission, the read-only methods and
    /// properties that return a BitLocker recovery password, key package or external key, or the
    /// TPM owner authorization, the registry writers (RegistryKey's CreateSubKey, DeleteSubKey,
    /// DeleteSubKeyTree and DeleteValue, and the advapi32 Reg* and ntdll Nt*/Zw* functions that
    /// set, create, delete, rename, copy, restore, replace, load or unload keys and values), and
    /// the registry values and scheduled task through which Windows' Secure Boot servicing applies
    /// db, dbx and KEK updates to the firmware (AvailableUpdates, MicrosoftUpdateManagedOptIn,
    /// Secure-Boot-Update).
    /// </summary>
    private static readonly string[] ForbiddenLiterals =
    {
        "SetFirmwareEnvironmentVariable",
        "NtSetSystemEnvironmentValue",
        "ZwSetSystemEnvironmentValue",
        "SetAttribute",
        "SetBIOSAttributes",
        "DellSmbios",
        "DellBIOSProvider",
        "manage-bde",
        "bcdboot",
        "fwbootmgr",
        "a5a30fa2-3d06-4e9f-b5f4-a01df9d1fcba",
        "ProtectKeyWith",
        "DisableKeyProtectors",
        "EnableKeyProtectors",
        "DeleteKeyProtector",
        "UnlockWith",
        "SaveExternalKeyToFile",
        "BackupRecoveryInformationTo",
        "Tbsip_Submit_Command",
        "Tbsi_Physical_Presence_Command",
        "GetKeyProtectorNumericalPassword",
        "GetKeyPackage",
        "GetKeyProtectorExternalKey",
        "GetExternalKeyFromFile",
        "GetOwnerAuth",
        "RecoveryPassword",
        "CreateSubKey",
        "DeleteSubKey",
        "DeleteValue",
        "RegSetValue",
        "RegSetKeyValue",
        "RegSetKeySecurity",
        "RegCreateKey",
        "RegDeleteKey",
        "RegDeleteTree",
        "RegCopyTree",
        "RegRenameKey",
        "RegRestoreKey",
        "RegReplaceKey",
        "RegLoadKey",
        "RegUnLoadKey",
        "NtSetValueKey",
        "ZwSetValueKey",
        "NtCreateKey",
        "ZwCreateKey",
        "NtDeleteKey",
        "ZwDeleteKey",
        "NtRenameKey",
        "ZwRenameKey",
        "NtRestoreKey",
        "ZwRestoreKey",
        "NtReplaceKey",
        "ZwReplaceKey",
        "NtLoadKey",
        "ZwLoadKey",
        "NtUnloadKey",
        "ZwUnloadKey",
        "AvailableUpdates",
        "MicrosoftUpdateManagedOptIn",
        "Secure-Boot-Update",
    };

    /// <summary>
    /// The Win32_EncryptableVolume, Win32_Tpm and Dell methods that change state and have a name
    /// distinctive enough to match on its own. Each is caught in double or single quotes,
    /// case-sensitively, so InvokeMethod("Name", ...) and a constant holding the name are both
    /// caught, and as a call in any casing (".Name(", ".Name.Invoke", ForEach-Object Name), so a
    /// PowerShell call on a WMI object is caught too. Win32_Tpm's Clear, Enable and Disable are too
    /// common to match by name: the WMI allow-list catches them in an invocation, and the
    /// PowerShell sources of objects that expose them as direct calls (Get-WmiObject, the WMI
    /// accelerators, System.Management types) are forbidden.
    /// </summary>
    private static readonly string[] ForbiddenMethodNames =
    {
        "Encrypt",
        "EncryptAfterHardwareTest",
        "Decrypt",
        "Lock",
        "Unlock",
        "PauseConversion",
        "ResumeConversion",
        "ChangePIN",
        "ChangePassphrase",
        "ChangeExternalKey",
        "DeleteKeyProtector",
        "DeleteKeyProtectors",
        "DisableKeyProtectors",
        "EnableKeyProtectors",
        "SetIdentificationField",
        "ClearAllAutoUnlockKeys",
        "EnableAutoUnlock",
        "DisableAutoUnlock",
        "PrepareVolume",
        "UpgradeVolume",
        "BackupRecoveryInformationToActiveDirectory",
        "TakeOwnership",
        "ChangeOwnerAuth",
        "ImportOwnerAuth",
        "ResetAuthLockOut",
        "ResetSrkAuth",
        "SetPhysicalPresenceRequest",
        "Provision",
        "CreateEndorsementKeyPair",
        "AddBlockedCommand",
        "RemoveBlockedCommand",
        "EnableAutoProvisioning",
        "DisableAutoProvisioning",
        "SetAttribute",
        "SetAttributes",
        "SetBIOSAttributes",
    };

    private const RegexOptions CaseSensitive = RegexOptions.CultureInvariant;
    private const RegexOptions IgnoreCase = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    /// <summary><see cref="ForbiddenMethodNames"/> as a regex alternation, longest first.</summary>
    private static readonly string ForbiddenMethodAlternation = string.Join("|", ForbiddenMethodNames.OrderByDescending(name => name.Length));

    /// <summary>
    /// A call of one of <see cref="ForbiddenMethodNames"/>, in any casing: ".Name(" with optional
    /// spaces, or ".Name.Invoke", which calls a PowerShell method object without parentheses.
    /// </summary>
    private static readonly Regex ForbiddenMethodCallPattern = new(
        @"\.\s*(?<method>" + ForbiddenMethodAlternation + @")\s*(?:\(|\.\s*Invoke)",
        IgnoreCase);

    /// <summary>
    /// A PowerShell call of one of <see cref="ForbiddenMethodNames"/> by member name, in any casing:
    /// ForEach-Object (or its aliases foreach and %) with the name, with or without -MemberName,
    /// and the .ForEach('Name') collection method.
    /// </summary>
    private static readonly Regex ForbiddenMemberNameCallPattern = new(
        @"(?:(?<![\w-])(?:ForEach-Object|foreach)|(?<![\w%])%)\s+(?:-MemberName(?:\s+|:\s*))?[""']?(?<method>" + ForbiddenMethodAlternation + @")(?![\w-])"
        + @"|\.\s*ForEach\s*\(\s*[""'](?<method>" + ForbiddenMethodAlternation + @")[""']",
        IgnoreCase);

    /// <summary>
    /// wmic as a word, in any casing: the command-line tool calls any WMI method by an unquoted name
    /// ("call PauseConversion"), so it is forbidden outright, as manage-bde is.
    /// </summary>
    private static readonly Regex WmicPattern = new(@"(?<![\w-])wmic(?![\w-])", IgnoreCase);

    /// <summary>
    /// PowerShell that runs a command held in a string, which no text scan can read:
    /// Invoke-Expression (iex), [scriptblock]::Create, InvokeScript and NewScriptBlock, and an
    /// encoded command line (-EncodedCommand, -enc, -ec), in any casing.
    /// </summary>
    private static readonly Regex DynamicPowerShellPattern = new(
        @"(?<![\w-])(?:Invoke-Expression|iex)(?![\w-])|\[scriptblock\]\s*::\s*Create\b|\.\s*(?:InvokeScript|NewScriptBlock)\s*\(|(?<![\w-])-(?:EncodedCommand|enc|ec)(?![\w-])",
        IgnoreCase);

    /// <summary>
    /// PowerShell commands that change Secure Boot, BitLocker, TPM or WMI state, with their
    /// aliases, and every command of the BitLocker module (Get-BitLockerVolume returns the recovery
    /// passwords). Get-WmiObject (gwmi) is forbidden too: its objects expose every WMI method as a
    /// direct call ($tpm.Clear(), $tpm.Disable()) that a text scan cannot tell from an ordinary
    /// one. Confirm-SecureBootUEFI, Get-SecureBootUEFI, Get-Tpm and Get-CimInstance only read, and
    /// a CIM instance exposes no method except through Invoke-CimMethod, so they stay allowed.
    /// </summary>
    private static readonly Regex ForbiddenCmdletPattern = new(
        @"(?<![\w-])(?:Set-SecureBootUEFI"
        + @"|(?:Add|Backup|BackupToAAD|Clear|Disable|Enable|Get|Lock|Remove|Resume|Suspend|Unlock)-BitLocker\w*"
        + @"|(?:Clear|Disable|Enable|Import|Initialize|Set|Unblock)-Tpm\w*"
        + @"|(?:New|Remove|Set)-(?:CimInstance|WmiInstance|WmiObject)|Get-WmiObject"
        + @"|gwmi|ncim|rcim|rwmi|scim|swmi)(?![\w-])",
        IgnoreCase);

    /// <summary>
    /// A System.Management invocation (InvokeMethod, GetMethodParameters, ExecMethod), in C# or on
    /// a PowerShell object, whose first argument is not one of <see cref="AllowedWmiMethods"/> as a
    /// literal or one of <see cref="AllowedWmiMethodConstants"/>. A variable, a concatenation or any
    /// other Get* name fails.
    /// </summary>
    private static readonly Regex WmiInvocationPattern = new(
        @"\b(?:InvokeMethod|GetMethodParameters|ExecMethod)\s*\(\s*(?!(?:"
        + string.Join("|", AllowedWmiMethodConstants.Keys.Concat(AllowedWmiMethods.SelectMany(name => new[] { "\"" + name + "\"", "'" + name + "'" })))
        + @")\s*[,)])",
        IgnoreCase);

    /// <summary>The PowerShell commands (and aliases) that invoke a WMI or CIM method.</summary>
    private static readonly Regex PowerShellWmiInvokerPattern = new(@"(?<![\w-])(?:Invoke-CimMethod|Invoke-WmiMethod|icim|iwmi)(?![\w-])", IgnoreCase);

    /// <summary>The method a PowerShell invoker names: -MethodName or -Name, then a word, a quoted word or a variable.</summary>
    private static readonly Regex PowerShellMethodArgumentPattern = new(@"(?<![\w-])-(?:MethodName|Name)(?:\s+|:)(?<name>[^\s;|)]+)", IgnoreCase);

    /// <summary>
    /// The [wmi], [wmiclass] and [wmisearcher] type accelerators, and System.Management types
    /// named in PowerShell ([System.Management.ManagementObject], New-Object Management.*), whose
    /// objects expose every WMI method as a direct call.
    /// </summary>
    private static readonly Regex WmiAcceleratorPattern = new(
        @"\[wmi(?:class|searcher)?\]|\[(?:System\.)?Management\.Management\w*\]|(?<![\w-])New-Object\s+(?:-TypeName\s+)?(?:System\.)?Management\.",
        IgnoreCase);

    /// <summary>ManagementObject.Put, which writes changed WMI properties back to the provider.</summary>
    private static readonly Regex WmiPutPattern = new(@"\.\s*Put\s*\(", IgnoreCase);

    /// <summary>bcdedit (or an identifier naming it) with a switch, / or -, that changes the boot configuration store.</summary>
    private static readonly Regex BcdEditWritePattern = new(
        @"bcdedit[^\n]*?(?<![\w-])[/-](?:set|deletevalue|delete|createstore|create|copy|import|sysstore|mirror|displayorder"
        + @"|bootsequence|default|timeout|toolsdisplayorder|bootems|emssettings|ems|bootdebug|dbgsettings|debug"
        + @"|hypervisorsettings|eventsettings|event)(?![\w-])",
        IgnoreCase);

    /// <summary>A bcdedit write switch as a quoted argument on its own, as it would be passed to ProcessRunner.Run, in any casing (bcdedit reads "/SET" as "/set").</summary>
    private static readonly Regex BcdEditSwitchLiteralPattern = new(
        @"[""'][/-](?:set|deletevalue|delete|create|copy|import|bootsequence|displayorder|default|timeout|toolsdisplayorder)[""']",
        IgnoreCase);

    /// <summary>
    /// Registry.SetValue, and SetValue on one of the Registry class's base keys
    /// (Registry.LocalMachine.SetValue), in C# or as [Microsoft.Win32.Registry]::... in
    /// PowerShell, in any casing.
    /// </summary>
    private static readonly Regex RegistrySetValuePattern = new(@"\bRegistry\s*\]?\s*(?:\.|::)\s*(?:\w+\s*\.\s*)?SetValue\b", IgnoreCase);

    /// <summary>The start of an OpenSubKey call, in any casing; <see cref="RegistryOpensForWriting"/> reads its arguments.</summary>
    private static readonly Regex OpenSubKeyCallPattern = new(@"\bOpenSubKey\s*\(", IgnoreCase);

    /// <summary>The one second argument that keeps an OpenSubKey call read-only: false, $false in PowerShell.</summary>
    private static readonly Regex ReadOnlyOpenArgumentPattern = new(@"^\$?false$", IgnoreCase);

    /// <summary>
    /// The PowerShell commands that change item properties, registry values among them, in any
    /// casing: Set-, New-, Remove-, Clear-, Copy-, Move- and Rename-ItemProperty, and the aliases
    /// sp, rp, clp, cpp, mp and rnp.
    /// </summary>
    private static readonly Regex ItemPropertyWriteCmdletPattern = new(
        @"(?<![\w-])(?:(?:Set|New|Remove|Clear|Copy|Move|Rename)-ItemProperty|sp|rp|clp|cpp|mp|rnp)(?![\w-])",
        IgnoreCase);

    /// <summary>A registry path, in any casing: a PowerShell registry drive (HKLM:, HKCU:, HKCR:, HKU:, HKCC:), the Registry:: provider prefix, or a HKEY_ root name.</summary>
    private static readonly Regex RegistryPathPattern = new(@"(?<![\w-])(?:HK(?:LM|CU|CR|U|CC)\s*:|Registry::|HKEY_\w+)", IgnoreCase);

    /// <summary>
    /// The PowerShell commands that create, change, move or remove an item, in any casing: New-,
    /// Set-, Remove-, Clear-, Copy-, Move- and Rename-Item, and the aliases ni, si, ri, rm, rmdir,
    /// rd, del, erase, cli, cpi, copy, cp, mi, move, mv, rni and ren. On a line that names a
    /// <see cref="RegistryPathPattern"/> they write the registry.
    /// </summary>
    private static readonly Regex ItemWriteCmdletPattern = new(
        @"(?<![\w-])(?:(?:New|Set|Remove|Clear|Copy|Move|Rename)-Item|ni|si|ri|rm|rmdir|rd|del|erase|cli|cpi|copy|cp|mi|move|mv|rni|ren)(?![\w-])",
        IgnoreCase);

    /// <summary>
    /// The registry tools run to change the registry, in any casing: reg (or reg.exe) with add,
    /// delete, import, copy, restore, load, unload or flags; regedit with /s or -s or a file name
    /// ending in .reg; and regini.
    /// </summary>
    private static readonly Regex RegistryToolWritePattern = new(
        @"(?<![\w-])(?:reg(?:\.exe)?\s+(?:add|delete|import|copy|restore|load|unload|flags)|regedit(?:\.exe)?\s+(?:[/-]s|[^\s/-]\S*\.reg)|regini)(?![\w-])",
        IgnoreCase);

    /// <summary>A command-line option: one or two dashes, a letter, then word characters or dashes, optionally followed by "=".</summary>
    private static readonly Regex CommandLineOptionPattern = new(@"(?<![\w-])(?<option>--?[A-Za-z][\w-]*)(?<value>=)?", CaseSensitive);

    /// <summary>
    /// A single-dash token on a cctk line that belongs to PowerShell, not to cctk: a PascalCase
    /// parameter such as -FilePath or -ArgumentList, or an operator such as -and or -eq.
    /// </summary>
    private static readonly Regex PowerShellParameterOrOperatorPattern = new(
        @"^-(?:[A-Z][a-z][A-Za-z]*|[ci]?(?:eq|ne|gt|ge|lt|le|like|notlike|match|notmatch|contains|notcontains|in|notin|replace|split)"
        + @"|join|and|or|xor|not|is|isnot|as|f|band|bor|bxor|bnot|shl|shr)$",
        CaseSensitive);

    /// <summary>
    /// Any string literal that starts with a set-style argument ("--name=value", "-name=value" or
    /// the PowerShell '--name=value'), wherever it sits. The real cctk invocation passes its
    /// argument through a constant declared on another line, so the argument text itself is what
    /// must be caught. No read-only option in src/ or tools/ has this shape.
    /// </summary>
    private static readonly Regex SetStyleArgumentLiteralPattern = new(@"[""']--?[A-Za-z0-9_-]+=", CaseSensitive);

    /// <summary>cctk's apply-a-configuration-file argument (-i or --infile) as a quoted literal, which a constant would hold.</summary>
    private static readonly Regex CctkApplyFileLiteralPattern = new(@"[""'](?:-i|--infile)[""']", IgnoreCase);

    [Fact]
    public void SourceTreeContainsNoWritePathReferences()
    {
        IReadOnlyList<string> files = ScannedFiles();
        Assert.NotEmpty(files);
        Assert.Contains(files, path => RepositoryPaths.Relative(path).Contains("/UefiAccess.Core/Native/", StringComparison.Ordinal));

        List<string> violations = new();
        Dictionary<WriteSiteAllowance, int> linesHoldingTheLiteral = WriteSiteAllowances.ToDictionary(allowance => allowance, _ => 0);
        HashSet<string> scanned = new(StringComparer.Ordinal);
        foreach (string file in files)
        {
            string relative = RepositoryPaths.Relative(file);
            scanned.Add(relative);
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                WriteSiteAllowance? allowance = AllowanceFor(relative, lines[i]);
                IReadOnlyList<string> found;
                if (allowance is null)
                {
                    found = FindViolations(lines[i]);
                }
                else
                {
                    linesHoldingTheLiteral[allowance]++;
                    found = ViolationsOutsideAllowance(lines[i], allowance);
                }

                foreach (string violation in found)
                {
                    violations.Add($"{relative}:{i + 1}: {violation}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "The product writes only through the allow-listed sites. Forbidden write-path references found:" + Environment.NewLine + string.Join(Environment.NewLine, violations));

        string[] countMismatches = WriteSiteAllowances
            .Where(allowance => linesHoldingTheLiteral[allowance] != allowance.ExpectedLines)
            .Select(allowance => $"{allowance.File}: {linesHoldingTheLiteral[allowance]} line(s) hold \"{allowance.Literal}\", the allowance says exactly {allowance.ExpectedLines}" + (scanned.Contains(allowance.File) ? string.Empty : " (the file is not in the source tree)"))
            .ToArray();
        Assert.True(
            countMismatches.Length == 0,
            "WriteSiteAllowances holds the exact number of lines of each allow-listed file that name its literal; a change to the file changes the count, and the new line is reviewed before the table follows it:" + Environment.NewLine + string.Join(Environment.NewLine, countMismatches));
    }

    [Theory]
    [InlineData("src/UefiAccess.Core/Native/Kernel32.cs", "    public static extern bool SetFirmwareEnvironmentVariableExW(string name, string guid, IntPtr buffer, uint size, uint attributes);")]
    [InlineData("src/UefiAccess.Core/Native/Kernel32.cs", "    /// Writes a firmware environment variable; see setfirmwareenvironmentvariableexw on learn.microsoft.com.")]
    [InlineData("src/UefiAccess.Core/Safety/FirmwareVariableWriter.cs", "        return Kernel32.SetFirmwareEnvironmentVariableExW(name, guid, buffer, size, attributes);")]
    [InlineData("src/UefiAccess.Core/Safety/BitLockerSuspender.cs", "    internal const string DisableKeyProtectorsMethod = \"DisableKeyProtectors\";")]
    [InlineData("src/UefiAccess.Core/Safety/BitLockerSuspender.cs", "                    using ManagementBaseObject inParameters = _volume.GetMethodParameters(DisableKeyProtectorsMethod);")]
    [InlineData("src/UefiAccess.Core/Safety/BitLockerSuspender.cs", "                    using ManagementBaseObject outParameters = _volume.InvokeMethod(DisableKeyProtectorsMethod, inParameters, new InvokeMethodOptions());")]
    [InlineData("src/UefiAccess.Core/Safety/BitLockerSuspender.cs", "        return string.Format(CultureInfo.InvariantCulture, CallLabelFormat, DisableKeyProtectorsMethod, driveLetter);")]
    [InlineData("src/UefiAccess.Core/Safety/BitLockerSuspender.cs", "/// One DisableKeyProtectors call with DisableCount 1; the resume happens at the next restart.")]
    public void WriteSiteAllowance_AcceptsTheAllowedLineOfItsFile(string file, string line)
    {
        WriteSiteAllowance? allowance = AllowanceFor(file, line);

        Assert.NotNull(allowance);
        Assert.NotEmpty(FindViolations(line));
        Assert.Empty(ViolationsOutsideAllowance(line, allowance!));
    }

    [Theory]
    [InlineData("src/UefiAccess.Core/Native/Kernel32.cs", "public static extern uint NtSetSystemEnvironmentValueEx(ref UNICODE_STRING name, ref Guid guid, IntPtr buffer, uint length, uint attributes);")]
    [InlineData("src/UefiAccess.Core/Native/Kernel32.cs", "public static extern bool SetFirmwareEnvironmentVariableExW(string name, string guid, IntPtr buffer, uint size, uint attributes); // or NtSetSystemEnvironmentValueEx")]
    [InlineData("src/UefiAccess.Core/Native/Kernel32.cs", "volume.InvokeMethod(DisableKeyProtectorsMethod, inParameters, null);")]
    [InlineData("src/UefiAccess.Core/Safety/FirmwareVariableWriter.cs", "Kernel32.SetFirmwareEnvironmentVariableExW(plan.Name, guid, buffer, plan.Size, plan.Attributes); NtDll.ZwSetSystemEnvironmentValueEx(ref name, ref guid, buffer, size, attributes);")]
    [InlineData("src/UefiAccess.Core/Safety/FirmwareVariableWriter.cs", "volume.InvokeMethod(DisableKeyProtectorsMethod, inParameters, null); // after SetFirmwareEnvironmentVariableExW")]
    [InlineData("src/UefiAccess.Core/Safety/FirmwareVariableWriter.cs", "Process.Start(\"manage-bde\", \"-protectors -disable C:\"); // then SetFirmwareEnvironmentVariableExW")]
    [InlineData("src/UefiAccess.Core/Safety/BitLockerSuspender.cs", "volume.InvokeMethod(\"DisableKeyProtectors\", inParameters, null); volume.InvokeMethod(\"EnableKeyProtectors\", null, null);")]
    [InlineData("src/UefiAccess.Core/Safety/BitLockerSuspender.cs", "volume.InvokeMethod(DisableKeyProtectorsMethod, inParameters, null); volume.InvokeMethod(\"Decrypt\", null, null);")]
    [InlineData("src/UefiAccess.Core/Safety/BitLockerSuspender.cs", "Kernel32.SetFirmwareEnvironmentVariableExW(name, guid, buffer, size, attributes);")]
    [InlineData("src/UefiAccess.Core/Safety/BitLockerSuspender.cs", "& manage-bde -protectors -disable C: -rc 1 # instead of DisableKeyProtectors")]
    [InlineData("src/UefiAccess.Core/Safety/BitLockerSuspender.cs", "Suspend-BitLocker -MountPoint C: # DisableKeyProtectors by cmdlet")]
    [InlineData("src/UefiAccess.Core/Safety/BitLockerSuspender.cs", "$id = $volume.GetKeyProtectorNumericalPassword($protector) # DisableKeyProtectors")]
    [InlineData("src/UefiAccess.Core/Platform/BitLockerInspector.cs", "volume.InvokeMethod(DisableKeyProtectorsMethod, inParameters, null);")]
    [InlineData("src/UefiAccess.Core/Safety/WriteTransaction.cs", "Kernel32.SetFirmwareEnvironmentVariableExW(name, guid, buffer, size, attributes);")]
    [InlineData("src/UefiAccess.Presentation/ViewModels/ChangeViewModel.cs", "volume.InvokeMethod(DisableKeyProtectorsMethod, inParameters, null);")]
    public void WriteSiteAllowance_CoversOnlyItsOwnLiteralInItsOwnFile(string file, string line)
    {
        WriteSiteAllowance? allowance = AllowanceFor(file, line);

        Assert.NotEmpty(allowance is null ? FindViolations(line) : ViolationsOutsideAllowance(line, allowance));
    }

    [Fact]
    public void BitLockerSuspender_SuspendsForOneRestartThroughTheOneConstant()
    {
        // The text scan accepts the suspender's DisableKeyProtectors lines (WriteSiteAllowances)
        // only because of these pins: the constant holds that
        // name and nothing else; the count constant is 1 (one restart; the omitted count's
        // behaviour is not documented); the class names no other WMI method through a constant
        // (the inspector's own rule, AllowListedWmiMethodConstantsHoldTheReadOnlyNames, stays as
        // it is); and the file invokes the method once, with that count and no literal.
        FieldInfo method = LiteralField(typeof(BitLockerSuspender), SuspendMethodConstant);
        Assert.Equal("DisableKeyProtectors", (string?)method.GetRawConstantValue());

        FieldInfo count = LiteralField(typeof(BitLockerSuspender), "OneRestart");
        Assert.Equal(typeof(uint), count.FieldType);
        Assert.Equal(1u, (uint)count.GetRawConstantValue()!);

        string[] methodConstants = typeof(BitLockerSuspender)
            .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string) && field.Name.EndsWith("Method", StringComparison.Ordinal))
            .Select(field => field.Name)
            .ToArray();
        Assert.Equal(new[] { SuspendMethodConstant }, methodConstants);

        string source = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "src", "UefiAccess.Core", "Safety", "BitLockerSuspender.cs"));
        IReadOnlyList<string> violations = BitLockerSuspendViolations(source);
        Assert.True(violations.Count == 0, "BitLockerSuspender must make exactly one DisableKeyProtectors call, with DisableCount set from OneRestart:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [InlineData("volume.InvokeMethod(\"DisableKeyProtectors\", inParameters, null);")]
    [InlineData("volume.InvokeMethod(EnableKeyProtectorsMethod, null, null);")]
    [InlineData("volume.InvokeMethod(DisableKeyProtectorsMethod + suffix, inParameters, null);")]
    [InlineData("using ManagementBaseObject again = volume.InvokeMethod(DisableKeyProtectorsMethod, inParameters, new InvokeMethodOptions());")]
    [InlineData("using ManagementBaseObject again = volume.GetMethodParameters(\"Decrypt\");")]
    [InlineData("using ManagementBaseObject again = volume.GetMethodParameters(GetKeyProtectorsMethod);")]
    [InlineData("volume.ExecMethod(DisableKeyProtectorsMethod, inParameters);")]
    [InlineData("inParameters.SetPropertyValue(\"DisableCount\", 0u);")]
    [InlineData("inParameters.Properties[\"DisableCount\"].Value = 0u;")]
    [InlineData("inParameters[\"DisableCount\"] = 0u;")]
    [InlineData("inParameters[\"KeyProtectorType\"] = 0u;")]
    [InlineData("inParameters[CountName] = OneRestart;")]
    [InlineData("OneRestart = 0u;")]
    [InlineData("Func<string, ManagementBaseObject, InvokeMethodOptions, ManagementBaseObject> invoke = volume.InvokeMethod;")]
    public void SuspendCheck_FlagsEveryOtherShape(string extraLine)
    {
        Assert.NotEmpty(BitLockerSuspendViolations(SuspendSource(extraLine)));
    }

    [Theory]
    [InlineData("int unrelated = 0;")]
    [InlineData("// volume.InvokeMethod(EnableKeyProtectorsMethod, null, null) is never called")]
    [InlineData("/* inParameters[\"DisableCount\"] = 0u; */")]
    [InlineData("logger.Warning(\"{Call} with DisableCount {Count} failed\", suspendLabel, OneRestart);")]
    [InlineData("return string.Format(CultureInfo.InvariantCulture, CallLabelFormat, DisableKeyProtectorsMethod, driveLetter);")]
    [InlineData("internal const uint OneRestart = 1u;")]
    [InlineData("returnValue = WmiHelper.RunBounded(() => volume.SuspendKeyProtectors(OneRestart), callTimeout, suspendLabel);")]
    [InlineData("if (disableCount != OneRestart) { throw new ArgumentOutOfRangeException(nameof(disableCount), disableCount, OneRestartOnlyMessage); }")]
    [InlineData("if (text[1] == ':') { return true; }")]
    public void SuspendCheck_AcceptsTheOneRestartSuspend(string extraLine)
    {
        Assert.Empty(BitLockerSuspendViolations(SuspendSource(extraLine)));
    }

    [Fact]
    public void SuspendCheck_AcceptsTheCallLaidOutOverLinesAndChecksWhereTheCountIsSet()
    {
        const string laidOut =
            "using ManagementBaseObject inParameters = volume.GetMethodParameters(\n    DisableKeyProtectorsMethod);\n"
            + "inParameters[\"DisableCount\"] = OneRestart;\n"
            + "using ManagementBaseObject outParameters = volume.InvokeMethod(\n    DisableKeyProtectorsMethod,\n    inParameters,\n    new InvokeMethodOptions());\n";
        Assert.Empty(BitLockerSuspendViolations(laidOut));

        const string withoutCount =
            "using ManagementBaseObject inParameters = volume.GetMethodParameters(DisableKeyProtectorsMethod);\n"
            + "using ManagementBaseObject outParameters = volume.InvokeMethod(DisableKeyProtectorsMethod, inParameters, new InvokeMethodOptions());\n";
        Assert.NotEmpty(BitLockerSuspendViolations(withoutCount));

        const string withLiteralCount =
            "using ManagementBaseObject inParameters = volume.GetMethodParameters(DisableKeyProtectorsMethod);\n"
            + "inParameters[\"DisableCount\"] = 1u;\n"
            + "using ManagementBaseObject outParameters = volume.InvokeMethod(DisableKeyProtectorsMethod, inParameters, new InvokeMethodOptions());\n";
        Assert.NotEmpty(BitLockerSuspendViolations(withLiteralCount));

        const string withForwardedCount =
            "using ManagementBaseObject inParameters = volume.GetMethodParameters(DisableKeyProtectorsMethod);\n"
            + "inParameters[\"DisableCount\"] = disableCount;\n"
            + "using ManagementBaseObject outParameters = volume.InvokeMethod(DisableKeyProtectorsMethod, inParameters, new InvokeMethodOptions());\n";
        Assert.NotEmpty(BitLockerSuspendViolations(withForwardedCount));

        const string countBeforeTheFetch =
            "otherParameters[\"DisableCount\"] = OneRestart;\n"
            + "using ManagementBaseObject inParameters = volume.GetMethodParameters(DisableKeyProtectorsMethod);\n"
            + "using ManagementBaseObject outParameters = volume.InvokeMethod(DisableKeyProtectorsMethod, inParameters, new InvokeMethodOptions());\n";
        Assert.NotEmpty(BitLockerSuspendViolations(countBeforeTheFetch));

        const string invocationBeforeTheFetch =
            "using ManagementBaseObject outParameters = volume.InvokeMethod(DisableKeyProtectorsMethod, inParameters, new InvokeMethodOptions());\n"
            + "inParameters[\"DisableCount\"] = OneRestart;\n"
            + "using ManagementBaseObject inParameters = volume.GetMethodParameters(DisableKeyProtectorsMethod);\n";
        Assert.NotEmpty(BitLockerSuspendViolations(invocationBeforeTheFetch));

        Assert.NotEmpty(BitLockerSuspendViolations("int nothing = 0;"));
    }

    [Fact]
    public void ScanCoversCSharpXamlAndPowerShell()
    {
        string[] relative = ScannedFiles().Select(RepositoryPaths.Relative).ToArray();

        Assert.Contains("src/UefiAccess.Core/Native/Kernel32.cs", relative);
        Assert.Contains("src/UefiAccess.Core/Platform/BitLockerInspector.cs", relative);
        Assert.Contains("src/UefiAccess.Core/Platform/DellInterfaceProbe.cs", relative);
        Assert.Contains(CctkReaderFile, relative);
        Assert.Contains("src/UefiAccess.Core/Platform/SecureBootPolicyInspector.cs", relative);
        Assert.Contains("src/UefiAccess.App/MainWindow.xaml", relative);
        Assert.Contains("src/UefiAccess.App/App.xaml", relative);
        Assert.Contains("src/UefiAccess.Presentation/ViewModels/ShellViewModel.cs", relative);
        Assert.Contains("src/UefiAccess.Presentation/Services/LiveFirmwareSource.cs", relative);
        Assert.Contains("src/UefiAccess.Core/Text/HexDump.cs", relative);
        Assert.Contains("tools/gate/Run-Gate.ps1", relative);
        Assert.Contains("tools/write-tests/Run-FailureCases.ps1", relative);
        Assert.Contains("tools/common/DataFolder.psm1", relative);
        Assert.All(relative, path => Assert.True(path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".xaml", StringComparison.Ordinal) || path.EndsWith(".ps1", StringComparison.Ordinal) || path.EndsWith(".psm1", StringComparison.Ordinal), path));
        Assert.DoesNotContain(relative, path => path.Contains("/bin/", StringComparison.Ordinal) || path.Contains("/obj/", StringComparison.Ordinal));
    }

    [Fact]
    public void DellInterfaceProbe_OnlyPassesTheTwoReadOnlyCctkArguments()
    {
        Assert.Equal("--version", DellInterfaceProbe.VersionArgument);
        Assert.Equal("--secureboot", DellInterfaceProbe.SecureBootArgument);

        // Every string constant in the probe that looks like a command-line option (a leading dash,
        // single or double, or a slash) must be one of those two, so a third argument such as "-i"
        // cannot be introduced without this test noticing.
        string[] optionConstants = typeof(DellInterfaceProbe)
            .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string?)field.GetRawConstantValue() ?? string.Empty)
            .Where(value => value.StartsWith('-') || value.StartsWith('/'))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { "--secureboot", "--version" }, optionConstants);
    }

    [Fact]
    public void DellInterfaceProbe_RunsCctkOnlyWithTheTwoArgumentConstants()
    {
        string source = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "src", "UefiAccess.Core", "Platform", "DellInterfaceProbe.cs"));

        IReadOnlyList<string> violations = CctkLaunchViolations(source);
        Assert.True(violations.Count == 0, "DellInterfaceProbe runs cctk with more than the two read-only arguments:" + Environment.NewLine + string.Join(Environment.NewLine, violations));

        // Not vacuous: both constants are passed.
        string?[] passed = SourceCalls.Find(source, "RunCctk").Where(site => !site.IsDeclaration).Select(site => SourceCalls.ArgumentFor(site.Arguments, "argument", 1)).ToArray();
        Assert.Contains("VersionArgument", passed);
        Assert.Contains("SecureBootArgument", passed);
    }

    [Fact]
    public void CctkReader_HoldsOnlyTheNineReadOptionConstants()
    {
        // Every string constant of the reader that looks like a
        // command-line option (a leading dash, single or double, or a slash) is one of the nine read
        // options, so a tenth argument, a set-style one or a file option cannot be introduced
        // without this test noticing.
        FieldInfo[] stringFields = typeof(CctkReader)
            .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => field.FieldType == typeof(string))
            .ToArray();
        string[] optionConstants = stringFields
            .Where(field => field.IsLiteral)
            .Select(field => (string?)field.GetRawConstantValue() ?? string.Empty)
            .Where(value => value.StartsWith('-') || value.StartsWith('/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(CctkReadOptionArguments.Order(StringComparer.Ordinal), optionConstants);

        // No static string field escapes the pin by not being a constant, and no string property,
        // static or not, holds text the pin cannot read either.
        Assert.All(stringFields, field => Assert.True(field.IsLiteral, "CctkReader." + field.Name + " is a static string field that is not a constant, which the reflection pin cannot read"));
        Assert.Empty(typeof(CctkReader)
            .GetProperties(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => property.Name));
    }

    [Fact]
    public void CctkReader_ArgumentForMapsEveryOptionToADistinctReadOptionArgument()
    {
        // Nine members, each mapped to its own read option, in the
        // order the test's list gives; any other value throws before anything could run.
        Assert.Equal(9, Enum.GetNames<CctkReadOption>().Length);
        string[] arguments = Enum.GetValues<CctkReadOption>().Select(CctkReader.ArgumentFor).ToArray();

        Assert.Equal(CctkReadOptionArguments, arguments);
        Assert.Equal(arguments.Length, arguments.Distinct(StringComparer.Ordinal).Count());
        Assert.Throws<ArgumentOutOfRangeException>(() => CctkReader.ArgumentFor((CctkReadOption)99));
    }

    [Fact]
    public void CctkReader_RunsCctkOnlyThroughArgumentFor()
    {
        // The reader's one launch passes the fixed program path,
        // the bound clamped to the run timeout, and ArgumentFor(option) alone.
        string source = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "src", "UefiAccess.Core", "Settings", "Dell", "CctkReader.cs"));
        IReadOnlyList<string> violations = CctkReaderLaunchViolations(source);
        Assert.True(violations.Count == 0, "CctkReader runs cctk other than once, by its fixed path, within the clamped bound, with ArgumentFor(option) alone:" + Environment.NewLine + string.Join(Environment.NewLine, violations));

        // The clamp is worth what its bound holds: 15 seconds, in a field nothing can reassign.
        FieldInfo? runTimeout = typeof(CctkSettingsProvider).GetField(nameof(CctkSettingsProvider.RunTimeout), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.True(runTimeout is { IsInitOnly: true } && runTimeout.FieldType == typeof(TimeSpan), "CctkSettingsProvider.RunTimeout must be a static readonly TimeSpan");
        Assert.Equal(TimeSpan.FromSeconds(15), CctkSettingsProvider.RunTimeout);

        // The source check reads one file, so the type must have no part elsewhere that it cannot
        // see: one constructor, CctkReader(ILogger? logger), the only place _cctkPath is set; one
        // RunCctk; and no nested type that could stand in for a trusted name (a compiler-generated
        // one, whose name no source can spell, aside).
        ConstructorInfo[] constructors = typeof(CctkReader).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.True(
            constructors.Length == 1 && constructors[0].GetParameters().Select(parameter => parameter.ParameterType.FullName).SequenceEqual(new[] { "Serilog.ILogger" }),
            "CctkReader has exactly one constructor, CctkReader(ILogger? logger)");
        Assert.Single(typeof(CctkReader).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly), method => method.Name == "RunCctk");
        Assert.Empty(typeof(CctkReader).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).Where(type => !type.Name.Contains('<', StringComparison.Ordinal)).Select(type => type.Name));

        // Every other RunCctk( under src/ (the probe has its own, checked above) is a call of the
        // reader's, and only CctkSettingsProvider.cs may make it, at most once, inside a lambda,
        // never handing it on as a method group.
        List<string> calls = new();
        List<string> problems = new();
        foreach (string file in RepositoryPaths.Enumerate("src", "*.cs"))
        {
            string relative = RepositoryPaths.Relative(file);
            if (relative is CctkReaderFile or DellInterfaceProbeFile)
            {
                continue;
            }

            string code = SourceCalls.WithoutCommentsAndStrings(File.ReadAllText(file));
            foreach (int line in SourceCalls.FindReferencesWithoutCall(code, "RunCctk"))
            {
                problems.Add($"{relative}:{line}: RunCctk is referenced without being called");
            }

            foreach (Match call in RunCctkCallPattern.Matches(code))
            {
                int line = SourceCalls.LineOf(code, call.Index);
                calls.Add($"{relative}:{line}");
                if (relative != CctkSettingsProviderFile)
                {
                    problems.Add($"{relative}:{line}: calls RunCctk; only {CctkSettingsProviderFile} may");
                }
                else if (!IsInsideLambda(code, call.Index))
                {
                    problems.Add($"{relative}:{line}: calls RunCctk outside the lambda that composes the reader");
                }
            }
        }

        Assert.True(problems.Count == 0, "The reader's RunCctk has one caller, CctkSettingsProvider, through a lambda:" + Environment.NewLine + string.Join(Environment.NewLine, problems));
        Assert.True(calls.Count <= 1, "The reader's RunCctk may be called from one place only. Found: " + string.Join(", ", calls));
    }

    [Fact]
    public void CctkReader_TrustedNamesAreTheRealTypes()
    {
        // The launch check trusts DellInterfaceProbe, CctkSettingsProvider and ProcessRunner (and
        // the enumeration) by name. Inside the reader a local, member or nested type of that name
        // is its business; from outside, a type of the same name in an enclosing namespace, or a
        // using alias (a global one, or an MSBuild Using item) would bind the name first.
        Type[] coreTypes = typeof(CctkReader).Assembly.GetTypes();
        foreach ((string name, Type real) in TrustedReaderTypes)
        {
            Assert.Equal(new[] { real.FullName }, coreTypes.Where(type => type.Name == name).Select(type => type.FullName).ToArray());
        }

        string names = string.Join("|", TrustedReaderTypes.Select(trusted => trusted.Name));
        Regex aliasInCode = new(@"\busing\s+@?(?:" + names + @")\s*=", CaseSensitive);
        Regex aliasInMsBuild = new(@"\bAlias\s*=\s*""\s*(?:" + names + @")\s*""", IgnoreCase);
        List<string> aliases = new();
        foreach (string file in RepositoryPaths.Enumerate("src", "*.cs"))
        {
            string code = SourceCalls.WithoutCommentsAndStrings(File.ReadAllText(file));
            aliases.AddRange(SourceCalls.LinesMatching(code, aliasInCode).Select(line => $"{RepositoryPaths.Relative(file)}:{line}"));
        }

        string[] msBuildFiles = RepositoryPaths.Enumerate("src", "*.csproj", "*.props", "*.targets")
            .Append(Path.Combine(RepositoryPaths.Root, "Directory.Build.props"))
            .Where(File.Exists)
            .ToArray();
        Assert.NotEmpty(msBuildFiles);
        foreach (string file in msBuildFiles)
        {
            string text = File.ReadAllText(file);
            aliases.AddRange(SourceCalls.LinesMatching(text, aliasInMsBuild).Select(line => $"{RepositoryPaths.Relative(file)}:{line}"));
        }

        Assert.True(aliases.Count == 0, "A using alias takes a name the reader's launch check trusts:" + Environment.NewLine + string.Join(Environment.NewLine, aliases));
    }

    [Fact]
    public void CctkReader_CodeIsTheVersionReviewedAtGateGA()
    {
        // Changing the reader is a guard change. The launch check reads the
        // source as text and cannot list every way C# can reach a field or rebind a name
        // (Unsafe.AsRef, reflection, a construct no pattern foresaw), so the reader's code is pinned
        // whole: any change other than to a comment or to whitespace fails here until the new code
        // has been reviewed at gate G-A and the digest updated with that review.
        string source = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "src", "UefiAccess.Core", "Settings", "Dell", "CctkReader.cs"));
        string digest = ReaderCodeDigest(source);

        Assert.True(digest == ReviewedCctkReaderCodeSha256, $"CctkReader.cs changed: review it again at gate G-A, then update {nameof(ReviewedCctkReaderCodeSha256)} to {digest}");
    }

    [Fact]
    public void ReaderCodeDigest_IgnoresCommentsAndWhitespaceOnly()
    {
        string digest = ReaderCodeDigest(CctkReaderSource);

        Assert.Equal(digest, ReaderCodeDigest(CctkReaderSource.Replace("\n", "\r\n", StringComparison.Ordinal)));
        Assert.Equal(digest, ReaderCodeDigest(CctkReaderSource.Replace("// (extra)", "/* a note */ // another", StringComparison.Ordinal)));
        Assert.Equal(digest, ReaderCodeDigest(CctkReaderSource.Replace("    ", "\t", StringComparison.Ordinal)));
        Assert.NotEqual(digest, ReaderCodeDigest(CctkReaderSource.Replace("// (extra)", "(limit, _) = (TimeSpan.FromHours(1), 0);", StringComparison.Ordinal)));
        Assert.NotEqual(digest, ReaderCodeDigest(CctkReaderSource.Replace("\"--numlock\"", "\"--numlock=\"", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("_logger, ArgumentFor(option));", "_logger, \"--numlock\");")]
    [InlineData("_logger, ArgumentFor(option));", "_logger, NumLockArgument);")]
    [InlineData("_logger, ArgumentFor(option));", "_logger, ArgumentFor(option), file);")]
    [InlineData("_logger, ArgumentFor(option));", "_logger, ArgumentFor(option) + suffix);")]
    [InlineData("_logger, ArgumentFor(option));", "_logger, ArgumentFor(option) + \"=enable\");")]
    [InlineData("_logger, ArgumentFor(option));", "null, ArgumentFor(option));")]
    [InlineData("// (extra)", "option = CctkReadOption.NumLock;")]
    [InlineData("// (extra)", "option++;")]
    [InlineData("// (extra)", "Swap(ref option);")]
    [InlineData("// (extra)", "ProcessRunner.Run(_cctkPath, workingDirectory, limit, _logger, ArgumentFor(option));")]
    [InlineData("// (extra)", "Func<string, string?, TimeSpan, ILogger, string[], ProcessRunResult> start = ProcessRunner.Run;")]
    [InlineData("// (extra)", "Func<CctkReadOption, TimeSpan, CctkRunOutcome> run = RunCctk;")]
    [InlineData("// (extra)", "CctkRunOutcome again = RunCctk(CctkReadOption.NumLock, bound);")]
    [InlineData("RunCctk(CctkReadOption option, TimeSpan bound)", "RunCctk(CctkReadOption option, TimeSpan bound, string argument)")]
    [InlineData("RunCctk(CctkReadOption option, TimeSpan bound)", "RunCctk(string cctkPath, CctkReadOption option, TimeSpan bound)")]
    [InlineData("ProcessRunner.Run(_cctkPath,", "ProcessRunner.Run(cctkPath,")]
    [InlineData("ProcessRunner.Run(_cctkPath,", "ProcessRunner.Run(_cctkPath + suffix,")]
    [InlineData("DellInterfaceProbe.FindCctk(_logger)", "Environment.GetEnvironmentVariable(\"CCTK_PATH\")")]
    [InlineData("// (extra)", "_cctkPath = path;")]
    [InlineData("private readonly string? _cctkPath;", "private string? _cctkPath;")]
    [InlineData("limit, _logger,", "bound, _logger,")]
    [InlineData("limit, _logger,", "CctkSettingsProvider.RunTimeout + bound, _logger,")]
    [InlineData("bound < CctkSettingsProvider.RunTimeout ? bound : CctkSettingsProvider.RunTimeout;", "bound;")]
    [InlineData("// (extra)", "limit = TimeSpan.FromMinutes(5);")]
    [InlineData("CctkReadOption.NumLock => NumLockArgument,", "CctkReadOption.NumLock => \"--numlock\",")]
    [InlineData("// (extra)", "ProcessRunResult second = ProcessRunner\n            .Run(_cctkPath, workingDirectory, bound, _logger, NumLockArgument);")]
    [InlineData("// (extra)", "ProcessRunResult second = ProcessRunner.@Run(_cctkPath, workingDirectory, bound, _logger, NumLockArgument);")]
    [InlineData("internal sealed class CctkReader", "using static UefiAccess.Core.Platform.ProcessRunner;\ninternal sealed class CctkReader")]
    [InlineData("CctkReadOption.NumLock => NumLockArgument,", "CctkReadOption.NumLock => NumLockArgument,\n        (CctkReadOption)9 => SecureBootArgument,")]
    [InlineData("CctkReadOption.NumLock => NumLockArgument,", "CctkReadOption.NumLock => SecureBootArgument,")]
    [InlineData("// (extra)", "Func<CctkReadOption, string> ArgumentFor = o => SecureBootArgument;")]
    [InlineData("_cctkPath = DellInterfaceProbe.FindCctk(_logger);", "var DellInterfaceProbe = (FindCctk: (Func<ILogger, string?>)(l => Environment.GetEnvironmentVariable(\"CCTK_PATH\")), 0);\n        _cctkPath = DellInterfaceProbe.FindCctk(_logger);")]
    [InlineData("internal bool Found", "private static class CctkSettingsProvider { internal static readonly TimeSpan RunTimeout = TimeSpan.FromHours(1); }\n\n    internal bool Found")]
    [InlineData("// (extra)", "(limit, _) = (TimeSpan.FromHours(1), 0);")]
    [InlineData("// (extra)", "Unsafe.AsRef(in limit) = TimeSpan.FromHours(1);")]
    [InlineData("_cctkPath = DellInterfaceProbe.FindCctk(_logger);", "_cctkPath = DellInterfaceProbe.FindCctk(_logger);\n        (_cctkPath, _) = (path, 0);")]
    [InlineData("// (extra)", "Unsafe.AsRef(in _cctkPath) = path;")]
    [InlineData("internal bool Found", "internal string? Program { init => (_cctkPath, _) = (value, 0); }\n\n    internal bool Found")]
    [InlineData("internal sealed class CctkReader", "internal sealed partial class CctkReader")]
    public void CctkReaderLaunchCheck_FlagsEveryOtherArgument(string original, string replacement)
    {
        string source = CctkReaderSource.Replace(original, replacement, StringComparison.Ordinal);

        Assert.NotEqual(CctkReaderSource, source);
        Assert.NotEmpty(CctkReaderLaunchViolations(source));
    }

    [Theory]
    [InlineData("// (extra)", "// (extra)")]
    [InlineData("// (extra)", "// ProcessRunner.Run(cctkPath, null, bound, _logger, \"--tpm\", file) is never called")]
    [InlineData("// (extra)", "if (option == CctkReadOption.SecureBoot) { _logger.Debug(\"Secure Boot\"); }")]
    [InlineData("// (extra)", "string label = nameof(option);")]
    [InlineData("// (extra)", "Func<CctkReadOption, bool> isSecureBoot = candidate => candidate == option;")]
    [InlineData("ProcessRunner.Run(_cctkPath, workingDirectory, limit, _logger, ArgumentFor(option));", "ProcessRunner.Run(\n            fileName: _cctkPath,\n            workingDirectory: workingDirectory,\n            timeout: limit,\n            logger: _logger,\n            ArgumentFor(option));")]
    public void CctkReaderLaunchCheck_AcceptsTheReader(string original, string replacement)
    {
        string source = CctkReaderSource.Replace(original, replacement, StringComparison.Ordinal);

        Assert.Empty(CctkReaderLaunchViolations(source));
    }

    [Fact]
    public void SecureBootPolicyInspector_RunsPowerShellOnlyWithConfirmSecureBootUefi()
    {
        Dictionary<string, string> constants = typeof(SecureBootPolicyInspector)
            .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .ToDictionary(field => field.Name, field => (string?)field.GetRawConstantValue() ?? string.Empty, StringComparer.Ordinal);

        // The constant the text scan trusts holds exactly the read-only cmdlet, whatever
        // concatenation the source uses.
        Assert.True(constants.TryGetValue("PowerShellCommand", out string? command), "SecureBootPolicyInspector.PowerShellCommand is gone; update this pin");
        Assert.Equal("Confirm-SecureBootUEFI", command);

        string source = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "src", "UefiAccess.Core", "Platform", "SecureBootPolicyInspector.cs"));
        IReadOnlyList<string> violations = PowerShellLaunchViolations(source, constants);
        Assert.True(violations.Count == 0, "SecureBootPolicyInspector runs PowerShell with more than Confirm-SecureBootUEFI:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [InlineData("x = RunCctk(cctkPath: p, argument: applyFile, errors);")]
    [InlineData("x = RunCctk(p, argument: \"-i\", errors);")]
    [InlineData("x = RunCctk(argument: ApplyArgument, errors: e, cctkPath: p);")]
    [InlineData("x = RunCctk(\n    cctkPath,\n    ApplyArgument,\n    errors);")]
    [InlineData("x = this.RunCctk(cctkPath, VersionArgument + \"=enable\", errors);")]
    [InlineData("Func<string, string, List<string>, string?> run = RunCctk;")]
    [InlineData("argument = \"-i\";")]
    [InlineData("argument += \"=enable\";")]
    [InlineData("Swap(ref argument);")]
    [InlineData("ProcessRunner.Run(cctkPath, null, CctkTimeout, _logger, argument, file);")]
    [InlineData("ProcessRunner.Run(cctkPath, null, CctkTimeout, _logger, \"-i\");")]
    [InlineData("ProcessRunner.Run(cctkPath + \" -i\", null, CctkTimeout, _logger, argument);")]
    [InlineData("private string? RunCctk(string cctkPath, List<string> errors) => null;")]
    public void CctkLaunchCheck_FlagsEveryOtherArgument(string extraLine)
    {
        Assert.NotEmpty(CctkLaunchViolations(CctkLaunchSource(extraLine)));
    }

    [Theory]
    [InlineData("x = RunCctk(cctkPath, VersionArgument, errors);")]
    [InlineData("x = RunCctk(cctkPath: p, argument: SecureBootArgument, errors: e);")]
    [InlineData("x = RunCctk(argument: VersionArgument, errors: e, cctkPath: p);")]
    [InlineData("x = RunCctk(\n    cctkPath,\n    SecureBootArgument,\n    errors);")]
    [InlineData("// RunCctk(p, ApplyArgument, errors) is never called")]
    [InlineData("string label = \"cctk \" + argument;")]
    [InlineData("if (argument == VersionArgument)")]
    public void CctkLaunchCheck_AcceptsTheTwoConstants(string extraLine)
    {
        Assert.Empty(CctkLaunchViolations(CctkLaunchSource(extraLine)));
    }

    [Theory]
    [InlineData("ProcessRunner.Run(executable, null, PowerShellTimeout, _logger, \"-NoProfile\", \"-Command\", Script);", "Suspend-BitLocker -MountPoint C:; Confirm-SecureBootUEFI")]
    [InlineData("ProcessRunner.Run(executable, null, PowerShellTimeout, _logger, \"-NoProfile\", \"-Command\", Script);", "Set-SecureBootUEFI -Name db -ContentFilePath db.bin")]
    [InlineData("ProcessRunner.Run(executable, null, PowerShellTimeout, _logger, \"-NoProfile\", \"-Command\", Script);", "Confirm-SecureBootUEFI; Clear-Tpm")]
    [InlineData("ProcessRunner.Run(executable, null, PowerShellTimeout, _logger, \"-ExecutionPolicy\", \"Bypass\", \"-Command\", Script);", "Confirm-SecureBootUEFI")]
    [InlineData("ProcessRunner.Run(executable, null, PowerShellTimeout, _logger, \"-Command\", \"Confirm-SecureBootUEFI\");", "Confirm-SecureBootUEFI")]
    [InlineData("ProcessRunner.Run(executable, null, PowerShellTimeout, _logger, \"-Command\", Other);", "Confirm-SecureBootUEFI")]
    [InlineData("ProcessRunner.Run(executable, null, PowerShellTimeout, _logger, \"-Command\", Script, \"-x\");", "Confirm-SecureBootUEFI")]
    [InlineData("ProcessRunner.Run(executable, null, PowerShellTimeout, _logger, \"-EncodedCommand\", Script);", "Confirm-SecureBootUEFI")]
    [InlineData("ProcessRunner.Run(executable, null, PowerShellTimeout, _logger, \"-File\", Script);", "Confirm-SecureBootUEFI")]
    [InlineData("ProcessRunner.Run(\"bcdedit.exe\", null, PowerShellTimeout, _logger, \"-Command\", Script);", "Confirm-SecureBootUEFI")]
    [InlineData("ProcessRunner.Run(executable, null, PowerShellTimeout, null, \"-Command\", Script);", "Confirm-SecureBootUEFI")]
    [InlineData("int unrelated = 0;", "Confirm-SecureBootUEFI")]
    public void PowerShellLaunchCheck_FlagsAnythingButTheReadOnlyCommand(string call, string script)
    {
        Assert.NotEmpty(PowerShellLaunchViolations(call, new Dictionary<string, string>(StringComparer.Ordinal) { ["Script"] = script }));
    }

    [Theory]
    [InlineData("ProcessRunner.Run(executable, null, PowerShellTimeout, _logger, \"-NoProfile\", \"-NonInteractive\", \"-Command\", Script);", "Confirm-SecureBootUEFI")]
    [InlineData("ProcessRunner.Run(executable, null, PowerShellTimeout, PowerShellOutputEncoding, _logger, \"-NoProfile\", \"-NonInteractive\", \"-Command\", Script);", "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; Confirm-SecureBootUEFI")]
    [InlineData("ProcessRunner.Run(\n    executable,\n    null,\n    PowerShellTimeout,\n    logger: _logger,\n    \"-NoLogo\",\n    \"-Command\",\n    Script);", "Confirm-SecureBootUEFI;")]
    public void PowerShellLaunchCheck_AcceptsConfirmSecureBootUefi(string call, string script)
    {
        Assert.Empty(PowerShellLaunchViolations(call, new Dictionary<string, string>(StringComparer.Ordinal) { ["Script"] = script }));
    }

    [Fact]
    public void AllowListedWmiMethodConstantsHoldTheReadOnlyNames()
    {
        // Wherever a constant with an allow-listed name is declared, it must hold the allowed method
        // name: the text scan trusts InvokeMethod(GetKeyProtectorsMethod, ...) only because of this.
        List<string> declared = new();
        List<string> wrong = new();
        foreach (Type type in ProductAssemblies.Types())
        {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!field.IsLiteral || field.FieldType != typeof(string) || !AllowedWmiMethodConstants.TryGetValue(field.Name, out string? expected))
                {
                    continue;
                }

                declared.Add(field.Name);
                string? value = (string?)field.GetRawConstantValue();
                if (!string.Equals(value, expected, StringComparison.Ordinal))
                {
                    wrong.Add($"{type.FullName}.{field.Name} = \"{value}\" (must be \"{expected}\")");
                }
            }
        }

        Assert.True(wrong.Count == 0, "Allow-listed WMI method constants hold another method:" + Environment.NewLine + string.Join(Environment.NewLine, wrong));
        Assert.Equal(AllowedWmiMethodConstants.Keys.Order(StringComparer.Ordinal), declared.Distinct().Order(StringComparer.Ordinal));

        // The BitLocker inspector names no other WMI method through a constant.
        string[] methodConstants = typeof(BitLockerInspector)
            .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string) && field.Name.EndsWith("Method", StringComparison.Ordinal))
            .Select(field => field.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(AllowedWmiMethodConstants.Keys.Order(StringComparer.Ordinal), methodConstants);
    }

    [Theory]
    [InlineData("volume.InvokeMethod(\"Encrypt\", null, null);")]
    [InlineData("volume.InvokeMethod(\"Lock\", inParameters, new InvokeMethodOptions());")]
    [InlineData("using ManagementBaseObject inParameters = volume.GetMethodParameters(\"Unlock\");")]
    [InlineData("volume.InvokeMethod(\"PauseConversion\", null, null);")]
    [InlineData("volume.InvokeMethod(\"ChangePIN\", null, null);")]
    [InlineData("volume.InvokeMethod(\"PrepareVolume\", null, null);")]
    [InlineData("volume.InvokeMethod(\"DisableAutoUnlock\", null, null);")]
    [InlineData("volume.InvokeMethod(\"ClearAllAutoUnlockKeys\", null, null);")]
    [InlineData("volume.InvokeMethod(\"EncryptAfterHardwareTest\", null, null);")]
    [InlineData("volume.InvokeMethod(\"ProtectKeyWithTPM\", null, null);")]
    [InlineData("volume.InvokeMethod(\"DeleteKeyProtectors\", null, null);")]
    [InlineData("private const string DecryptMethod = \"Decrypt\";")]
    [InlineData("volume.InvokeMethod(DecryptMethod, inParameters, null);")]
    [InlineData("volume.InvokeMethod(IsAutoUnlockEnabledMethod, inParameters, null);")]
    [InlineData("service.InvokeMethod(\"SetAttribute\", null, null);")]
    [InlineData("service.InvokeMethod(\"SetBIOSAttributes\", null, null);")]
    [InlineData("service.SetAttribute(\"SecureBoot\", \"Enabled\");")]
    [InlineData("private const string SecureBootArgument = \"--secureboot=enable\";")]
    [InlineData("ProcessRunner.Run(\"cctk\", null, timeout, logger, \"--secureboot=enable\");")]
    [InlineData("ProcessRunner.Run(\"cctk\", null, timeout, logger, \"--boot-order=hdd\");")]
    [InlineData("// run cctk --secureboot=disable")]
    [InlineData("Kernel32.SetFirmwareEnvironmentVariableExW(name, guid, buffer, size, attributes);")]
    [InlineData("NtDll.NtSetSystemEnvironmentValueEx(ref name, ref guid, buffer, length, attributes);")]
    [InlineData("Process.Start(\"manage-bde\", \"-on C:\");")]
    [InlineData("volume.Encrypt(0, 0);")]
    [InlineData("& manage-bde -on C: -RecoveryPassword")]
    [InlineData("# never run manage-bde from here")]
    [InlineData("Start-Process -FilePath $cctk -ArgumentList '--secureboot=enable' -Wait")]
    [InlineData("& $cctk --bootorder=hdd,usb")]
    [InlineData("Invoke-WmiMethod -Namespace root\\dcim\\sysman -Class DCIM_BIOSService -Name \"SetBIOSAttributes\"")]
    [InlineData("$volume.InvokeMethod('Encrypt', $null)")]
    [InlineData("<Button Content=\"Apply\" Click=\"SetAttribute(Boot)\" />")]
    public void Matcher_FlagsEveryWritePathForm(string line)
    {
        Assert.NotEmpty(FindViolations(line));
    }

    [Theory]
    [InlineData("using ManagementBaseObject inParameters = volume.GetMethodParameters(GetKeyProtectorsMethod);")]
    [InlineData("using ManagementBaseObject outParameters = volume.InvokeMethod(GetKeyProtectorsMethod, inParameters, new InvokeMethodOptions());")]
    [InlineData("using ManagementBaseObject outParameters = volume.InvokeMethod(GetPlatformValidationProfileMethod, inParameters, new InvokeMethodOptions());")]
    [InlineData("volume.InvokeMethod(\"GetKeyProtectorPlatformValidationProfile\", inParameters, null);")]
    [InlineData("private const string GetKeyProtectorsMethod = \"GetKeyProtectors\";")]
    [InlineData("private const string SecureBootArgument = \"--secureboot\";")]
    [InlineData("private const string VersionArgument = \"--version\";")]
    [InlineData("ProcessRunResult run = ProcessRunner.Run(cctkPath, workingDirectory, CctkTimeout, _logger, argument);")]
    [InlineData("Option<string> outOption = new(\"--out\", \"Output file\");")]
    [InlineData("/// the form \"secureboot=enable\" or \"secureboot=disable\" (case-insensitive)")]
    [InlineData("uint stored = Kernel32.GetFirmwareEnvironmentVariableExW(name, guid, buffer, size, out attributes);")]
    [InlineData("& $ExePath gate --ifr-varstores $IfrVarstores --out $gateJson")]
    [InlineData("Write-Host 'Nothing in this run changed a firmware variable, a BIOS setting, or BitLocker state.'")]
    [InlineData("<TextBlock AutomationProperties.Name=\"Privilege state\" Text=\"{Binding PrivilegeState}\" />")]
    [InlineData("")]
    public void Matcher_AcceptsTheReadOnlyFormsMilestoneOneUses(string line)
    {
        Assert.Empty(FindViolations(line));
    }

    [Theory]
    [InlineData("Set-SecureBootUEFI -Name db -Time 2026-01-01 -ContentFilePath x.bin")]
    [InlineData("private const string PowerShellCommand = \"Set-SecureBootUEFI -Name db -ContentFilePath db.bin\";")]
    [InlineData("Process.Start(\"powershell\", \"-NoProfile -Command Set-SecureBootUEFI -Name PK -Content $null\");")]
    [InlineData("set-securebootuefi -name dbx -contentfilepath dbx.bin")]
    [InlineData("Suspend-BitLocker -MountPoint \"C:\" -RebootCount 0")]
    [InlineData("Resume-BitLocker -MountPoint C:")]
    [InlineData("Disable-BitLocker -MountPoint \"C:\"")]
    [InlineData("Enable-BitLocker -MountPoint C: -TpmProtector")]
    [InlineData("Lock-BitLocker -MountPoint D:")]
    [InlineData("Unlock-BitLocker -MountPoint D: -Password $secure")]
    [InlineData("Add-BitLockerKeyProtector -MountPoint C: -TpmProtector")]
    [InlineData("Remove-BitLockerKeyProtector -MountPoint C: -KeyProtectorId $id")]
    [InlineData("Backup-BitLockerKeyProtector -MountPoint C: -KeyProtectorId $id")]
    [InlineData("BackupToAAD-BitLockerKeyProtector -MountPoint C: -KeyProtectorId $id")]
    [InlineData("Enable-BitLockerAutoUnlock -MountPoint D:")]
    [InlineData("$volume = Get-BitLockerVolume -MountPoint C:")]
    [InlineData("SUSPEND-BITLOCKER -MountPoint C:")]
    [InlineData("Clear-Tpm")]
    [InlineData("Initialize-Tpm -AllowClear $true")]
    [InlineData("Set-TpmOwnerAuth -NewOwnerAuthorization $auth")]
    [InlineData("Unblock-Tpm")]
    [InlineData("Enable-TpmAutoProvisioning")]
    [InlineData("Disable-TpmAutoProvisioning")]
    [InlineData("Import-TpmOwnerAuth -OwnerAuthorization $auth")]
    [InlineData("Set-CimInstance -InputObject $attribute -Property @{ CurrentValue = 'Enabled' }")]
    [InlineData("Set-WmiInstance -Class DCIM_BIOSEnumeration -Arguments @{ CurrentValue = 'Enabled' }")]
    [InlineData("Remove-CimInstance -InputObject $entry")]
    [InlineData("Remove-WmiObject -InputObject $entry")]
    [InlineData("New-CimInstance -ClassName DCIM_BIOSService -Property @{}")]
    [InlineData("$attribute | scim -Property @{ CurrentValue = 'Enabled' }")]
    [InlineData("Set-Item -Path DellSmbios:\\SecureBoot\\SecureBoot -Value Enabled")]
    [InlineData("si dellsmbios:\\BootSequence\\BootList -Value Uefi -Password $pw")]
    [InlineData("Import-Module DellBIOSProvider")]
    [InlineData("Get-ChildItem -Path DellSmbios:\\SecureBoot")]
    [InlineData("$tpm = Get-WmiObject -Namespace root\\cimv2\\security\\microsofttpm -Class Win32_Tpm")]
    [InlineData("$tpm = gwmi Win32_Tpm -Namespace root\\cimv2\\security\\microsofttpm")]
    public void Matcher_FlagsThePowerShellWriteCommands(string line)
    {
        Assert.NotEmpty(FindViolations(line));
    }

    [Theory]
    [InlineData("private const string PowerShellCommand = \"Confirm-SecureBootUEFI\";")]
    [InlineData("sources.AddRow(\"PowerShell Confirm-SecureBootUEFI\", Format.Bool(policy.PowerShellConfirm, \"not available\"));")]
    [InlineData("$setupMode = Get-SecureBootUEFI -Name SetupMode")]
    [InlineData("Get-SecureBootPolicy")]
    [InlineData("$tpm = Get-Tpm")]
    [InlineData("Get-CimInstance -Namespace root\\cimv2\\security\\microsoftvolumeencryption -ClassName Win32_EncryptableVolume")]
    [InlineData("Set-StrictMode -Version 2.0")]
    [InlineData("Set-Content -LiteralPath (Join-Path $outputFolder 'summary.txt') -Value $summaryLines -Encoding UTF8")]
    [InlineData("Write-Host 'A non-BitLocker volume and the TPM were only read.'")]
    public void Matcher_AcceptsTheReadOnlyPowerShellCommands(string line)
    {
        Assert.Empty(FindViolations(line));
    }

    [Theory]
    [InlineData("volume.InvokeMethod(methodName, inParameters, null);")]
    [InlineData("volume.InvokeMethod(\"GetKeyProtectorNumericalPassword\", inParameters, null);")]
    [InlineData("volume.InvokeMethod(GetRecoveryPasswordMethod, inParameters, null);")]
    [InlineData("volume.InvokeMethod(\"GetKey\" + suffix, inParameters, null);")]
    [InlineData("volume.InvokeMethod(\"GetConversionStatus\", inParameters, null);")]
    [InlineData("volume.InvokeMethod(observer, GetKeyProtectorsMethod, inParameters, null);")]
    [InlineData("session.InvokeMethod(Namespace, volume, \"GetKeyProtectors\", parameters);")]
    [InlineData("tpm.InvokeMethod(\"Clear\", null);")]
    [InlineData("$tpm.InvokeMethod('Clear', $null)")]
    [InlineData("$volume.PauseConversion()")]
    [InlineData("$volume.ChangePIN($id, $pin)")]
    [InlineData("$tpm.TakeOwnership($ownerAuth)")]
    [InlineData("$method = 'PauseConversion'")]
    [InlineData("Invoke-CimMethod -InputObject $volume -MethodName PauseConversion")]
    [InlineData("Invoke-WmiMethod -Namespace root\\cimv2\\security\\microsoftvolumeencryption -Class Win32_EncryptableVolume -Name PauseConversion")]
    [InlineData("Invoke-CimMethod -InputObject $volume -MethodName $method")]
    [InlineData("Invoke-CimMethod -Namespace root\\cimv2\\security\\microsofttpm -ClassName Win32_Tpm -MethodName Clear")]
    [InlineData("Invoke-CimMethod -InputObject $volume")]
    [InlineData("Invoke-CimMethod -InputObject $volume -MethodName GetKeyProtectors; Invoke-CimMethod -InputObject $volume -MethodName Decrypt")]
    [InlineData("$volume | icim -MethodName Decrypt")]
    [InlineData("iwmi -Path $path -Name Encrypt")]
    [InlineData("Invoke-WmiMethod -Path $path -Name GetKeyProtectorNumericalPassword -ArgumentList $id")]
    [InlineData("([wmiclass]'root\\cimv2:Win32_Process').Create('cmd.exe')")]
    [InlineData("$volume = [WMI]'root\\cimv2\\security\\microsoftvolumeencryption:Win32_EncryptableVolume.DeviceID=\"x\"'")]
    [InlineData("attribute.Put();")]
    [InlineData("$setting.Put()")]
    [InlineData("$protector.RecoveryPassword")]
    [InlineData("volume.InvokeMethod(\"GetKeyPackage\", inParameters, null);")]
    [InlineData("volume.InvokeMethod(\"SaveExternalKeyToFile\", inParameters, null);")]
    [InlineData("volume.InvokeMethod(\"UnlockWithNumericalPassword\", inParameters, null);")]
    [InlineData("tpm.InvokeMethod(\"GetOwnerAuthForEscrow\", null);")]
    [InlineData("$volume.Decrypt.Invoke()")]
    [InlineData("$volume.PauseConversion . Invoke($null)")]
    [InlineData("$volumes | ForEach-Object -MemberName Decrypt")]
    [InlineData("$volumes | ForEach-Object -MemberName:'PauseConversion'")]
    [InlineData("$volumes | % Decrypt")]
    [InlineData("$volumes | foreach pauseconversion")]
    [InlineData("$volumes.ForEach('decrypt')")]
    [InlineData("wmic /namespace:\\\\root\\cimv2\\security\\microsoftvolumeencryption path Win32_EncryptableVolume where DriveLetter='C:' call PauseConversion")]
    [InlineData("& WMIC.exe path Win32_Tpm call Clear")]
    [InlineData("$searcher = [wmisearcher]'SELECT DeviceID FROM Win32_EncryptableVolume'")]
    [InlineData("$searcher = [System.Management.ManagementObjectSearcher]'SELECT * FROM Win32_Tpm'")]
    [InlineData("$tpm = New-Object -TypeName System.Management.ManagementObject -ArgumentList $path")]
    public void Matcher_FlagsWmiMethodsOffTheAllowList(string line)
    {
        Assert.NotEmpty(FindViolations(line));
    }

    [Theory]
    [InlineData("volume.InvokeMethod(GetKeyProtectorTypeMethod, inParameters, new InvokeMethodOptions(null, QueryTimeout));")]
    [InlineData("volume.InvokeMethod(\"GetKeyProtectorType\", inParameters, null);")]
    [InlineData("volume.GetMethodParameters(\"GetKeyProtectors\");")]
    [InlineData("$volume.InvokeMethod('GetKeyProtectors', $arguments)")]
    [InlineData("$ids = $volume.GetKeyProtectors(0).VolumeKeyProtectorID")]
    [InlineData("Invoke-CimMethod -InputObject $volume -MethodName GetKeyProtectors -Arguments @{ KeyProtectorType = 0 }")]
    [InlineData("Invoke-WmiMethod -InputObject $volume -Name 'GetKeyProtectorType' -ArgumentList $id")]
    [InlineData("Invoke-CimMethod -InputObject $volume -MethodName:GetKeyProtectorPlatformValidationProfile -Arguments @{ VolumeKeyProtectorID = $id }")]
    [InlineData("$volume = Get-CimInstance -Namespace root\\cimv2\\security\\microsoftvolumeencryption -ClassName Win32_EncryptableVolume")]
    [InlineData("SelectionChanged?.Invoke(this, value);")]
    [InlineData("_dispatcher.Invoke(Flush);")]
    [InlineData("$rows | ForEach-Object { $_.Name }")]
    [InlineData("$rows | % Name")]
    [InlineData("IReadOnlyList<string> wmiClassNames = WmiHelper.ListClasses(ns);")]
    [InlineData("using ManagementObjectSearcher searcher = new(scope, new ObjectQuery(wql), CreateEnumerationOptions());")]
    [InlineData("foreach (ManagementBaseObject item in collection)")]
    [InlineData("if (signatureSize != 0 && payload % signatureSize != 0)")]
    [InlineData("3 => \"Numerical password\",")]
    [InlineData("list.Clear();")]
    [InlineData("lock (_gate)")]
    [InlineData("Console.Out.Write(ComputeOutput(value));")]
    public void Matcher_AcceptsTheAllowListedWmiReads(string line)
    {
        Assert.Empty(FindViolations(line));
    }

    [Theory]
    [InlineData("& bcdedit /set \"{fwbootmgr}\" bootsequence \"{bootmgr}\"")]
    [InlineData("bcdedit /set {bootmgr} path \\EFI\\other.efi")]
    [InlineData("bcdedit.exe /delete {current}")]
    [InlineData("bcdedit /create /d \"Other\" /application osloader")]
    [InlineData("bcdedit /deletevalue {default} safeboot")]
    [InlineData("BCDEDIT -SET {current} testsigning on")]
    [InlineData("bcdedit /displayorder {current} /addlast")]
    [InlineData("bcdedit /enum {fwbootmgr}")]
    [InlineData("bcdedit /enum {a5a30fa2-3d06-4e9f-b5f4-a01df9d1fcba}")]
    [InlineData("ProcessRunner.Run(bcdeditPath, null, timeout, logger, \"/set\", \"{bootmgr}\", \"bootmenupolicy\", \"legacy\");")]
    [InlineData("ProcessRunner.Run(tool, null, timeout, logger, \"/deletevalue\", \"{current}\", \"testsigning\");")]
    [InlineData("ProcessRunner.Run(tool, null, timeout, logger, \"/SET\", \"{bootmgr}\", \"bootmenupolicy\", \"legacy\");")]
    [InlineData("private const string OrderSwitch = \"-DisplayOrder\";")]
    [InlineData("$arguments = @('/Timeout', '0')")]
    [InlineData("bcdboot C:\\Windows /s S: /f UEFI")]
    public void Matcher_FlagsBootConfigurationWrites(string line)
    {
        Assert.NotEmpty(FindViolations(line));
    }

    [Theory]
    [InlineData("using RegistryKey? key = Registry.LocalMachine.OpenSubKey(SecureBootKeyPath, true);")]
    [InlineData("using RegistryKey? key = Registry.LocalMachine.OpenSubKey(SecureBootKeyPath, writable: true);")]
    [InlineData("using RegistryKey? key = Registry.LocalMachine.OpenSubKey(SecureBootKeyPath, writable);")]
    [InlineData("using RegistryKey? key = hive.OpenSubKey(Path.Combine(root, name), RegistryKeyPermissionCheck.ReadWriteSubTree);")]
    [InlineData("using RegistryKey? key = hive.OpenSubKey(path, RegistryRights.SetValue);")]
    [InlineData("using RegistryKey? key = Registry.LocalMachine.OpenSubKey(")]
    [InlineData("using RegistryKey key = Registry.LocalMachine.CreateSubKey(SecureBootKeyPath);")]
    [InlineData("key.DeleteValue(name, throwOnMissingValue: false);")]
    [InlineData("hive.DeleteSubKeyTree(path);")]
    [InlineData("Registry.SetValue(@\"HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\Secureboot\", name, 0x40);")]
    [InlineData("Registry.CurrentUser.SetValue(name, value);")]
    [InlineData("int status = AdvApi32.RegSetValueExW(key, name, 0, type, data, size);")]
    [InlineData("uint status = NtDll.NtSetValueKey(handle, ref name, 0, type, data, size);")]
    [InlineData("private const string ServicingValue = \"AvailableUpdates\";")]
    [InlineData("Start-ScheduledTask -TaskName '\\Microsoft\\Windows\\PI\\Secure-Boot-Update'")]
    [InlineData("Set-ItemProperty -Path HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Secureboot -Name $name -Value 0x40")]
    [InlineData("New-ItemProperty -Path $key -Name MicrosoftUpdateManagedOptIn -Value 0x5944 -PropertyType DWord")]
    [InlineData("Remove-ItemProperty -Path $key -Name $name")]
    [InlineData("sp $key $name 0x40")]
    [InlineData("New-Item -Path 'HKLM:\\SOFTWARE\\Policies\\Example' -Force")]
    [InlineData("Remove-Item -LiteralPath Registry::HKEY_LOCAL_MACHINE\\SOFTWARE\\Example -Recurse")]
    [InlineData("ni HKCU:\\Software\\Example")]
    [InlineData("$key = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey($path, $true)")]
    [InlineData("[Microsoft.Win32.Registry]::SetValue($keyName, $name, 0x40)")]
    [InlineData("& reg add HKLM\\SYSTEM\\CurrentControlSet\\Control\\Secureboot /v $name /t REG_DWORD /d 0x40 /f")]
    [InlineData("& REG.EXE import .\\settings.reg")]
    [InlineData("& reg delete $key /f")]
    [InlineData("& regedit /s .\\settings.reg")]
    [InlineData("& regini .\\acl.ini")]
    [InlineData("Invoke-CimMethod -ClassName StdRegProv -MethodName SetDWORDValue -Arguments @{ hDefKey = [uint32]2147483650 }")]
    [InlineData("registry.InvokeMethod(\"SetDWORDValue\", inParameters, null);")]
    public void Matcher_FlagsRegistryWrites(string line)
    {
        Assert.NotEmpty(FindViolations(line));
    }

    [Theory]
    [InlineData("using RegistryKey? key = Registry.LocalMachine.OpenSubKey(RegistryKeyPath, writable: false);")]
    [InlineData("using RegistryKey? key = Registry.LocalMachine.OpenSubKey(RegistryKeyPath);")]
    [InlineData("using RegistryKey? key = Registry.LocalMachine.OpenSubKey(Path.Combine(root, \"State\"), false);")]
    [InlineData("object? raw = key.GetValue(RegistryValueName);")]
    [InlineData("object? raw = Registry.GetValue(keyName, RegistryValueName, null);")]
    [InlineData("$key = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey($path, $false)")]
    [InlineData("$state = Get-ItemProperty -Path HKLM:\\SYSTEM\\CurrentControlSet\\Control\\SecureBoot\\State -Name UEFISecureBootEnabled")]
    [InlineData("$value = Get-ItemPropertyValue -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\SecureBoot\\State' -Name UEFISecureBootEnabled")]
    [InlineData("& reg query HKLM\\SYSTEM\\CurrentControlSet\\Control\\SecureBoot\\State /v UEFISecureBootEnabled")]
    [InlineData("Remove-Item -LiteralPath $stdoutFile -ErrorAction SilentlyContinue")]
    [InlineData("table.AddRow(\"(d) Registry Secure Boot enabled\", Format.Bool(report.SecureBootPolicy.RegistryEnabled, \"unreadable\"));")]
    [InlineData("void IRangeValueProvider.SetValue(double value)")]
    [InlineData("DecoderRegistry registry = DecoderRegistry.CreateDefault();")]
    public void Matcher_AcceptsRegistryReads(string line)
    {
        Assert.Empty(FindViolations(line));
    }

    [Theory]
    [InlineData("bcdedit /enum firmware")]
    [InlineData("bcdedit /enum all /v")]
    [InlineData("ProcessRunner.Run(tool, null, timeout, logger, \"/enum\", \"all\");")]
    [InlineData("Write-Host 'Convert the disk (mbr2gpt /validate, then mbr2gpt /convert) and run this script again.'")]
    public void Matcher_AcceptsBootConfigurationReads(string line)
    {
        Assert.Empty(FindViolations(line));
    }

    [Theory]
    [InlineData("& $cctk -i .\\bios.cctk")]
    [InlineData("& $CctkPath --secureboot=enable")]
    [InlineData("& $cctk --infile C:\\work\\bios.ini")]
    [InlineData("Start-Process -FilePath $cctk -ArgumentList '-i', 'C:\\work\\bios.ini' -Wait")]
    [InlineData("& $cctk -o C:\\work\\current.ini")]
    [InlineData("& $cctk --bootorder")]
    [InlineData("& $cctk --setuppwd=secret")]
    [InlineData("cctk.exe --SecureBoot")]
    [InlineData("private const string ApplyArgument = \"-i\";")]
    [InlineData("ProcessRunner.Run(cctkPath, null, CctkTimeout, _logger, \"--infile\", file);")]
    [InlineData("private const string Toggle = \"-secureboot=enable\";")]
    [InlineData("cctk --numlock")]
    [InlineData("& $CctkPath --tpmactivation")]
    [InlineData("cctk --numlock=enable")]
    [InlineData("cctk --NumLock")]
    [InlineData("cctk --setuppwd=x")]
    [InlineData("cctk -o x")]
    [InlineData("cctk --outfile x")]
    public void Matcher_FlagsCctkInvocationsBeyondTheTwoReadOnlyArguments(string line)
    {
        Assert.NotEmpty(FindViolations(line));
    }

    [Theory]
    [InlineData("/// cctk.exe under the two Program Files paths, run only with \"--version\" and \"--secureboot\".")]
    [InlineData("/// <param name=\"CctkVersion\">Output of \"cctk --version\" when run.</param>")]
    [InlineData("& $cctk --version")]
    [InlineData("$secureBoot = & $CctkPath --secureboot")]
    [InlineData("Start-Process -FilePath $cctk -ArgumentList '--secureboot' -NoNewWindow -Wait")]
    [InlineData("if ($cctkPath -and (Test-Path -LiteralPath $cctkPath)) {")]
    [InlineData("string label = \"cctk \" + argument;")]
    [InlineData("ProcessRunner.Run(executable, null, PowerShellTimeout, _logger, \"-NoProfile\", \"-NonInteractive\", \"-Command\", PowerShellCommand);")]
    public void Matcher_AcceptsTheReadOnlyCctkForms(string line)
    {
        Assert.Empty(FindViolations(line));
    }

    [Theory]
    [InlineData("DirectorySecurity security = new();")]
    [InlineData("security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);")]
    [InlineData("security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.Modify, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));")]
    [InlineData("security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));")]
    [InlineData("new DirectoryInfo(directory).SetAccessControl(security);")]
    [InlineData("FileSystemAclExtensions.CreateDirectory(security, Logging.LogDirectory);")]
    [InlineData("Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);")]
    [InlineData("Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });")]
    [InlineData("StandardOutputEncoding = Encoding.UTF8,")]
    [InlineData("string candidate = Path.Combine(Environment.SystemDirectory, \"WindowsPowerShell\", \"v1.0\", \"powershell.exe\");")]
    [InlineData("ProcessRunner.Run(executable, null, PowerShellTimeout, _logger, \"-NoProfile\", \"-NonInteractive\", \"-ExecutionPolicy\", \"Bypass\", \"-Command\", PowerShellCommand);")]
    [InlineData("private const string PowerShellCommand = \"[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; Confirm-SecureBootUEFI\";")]
    [InlineData("WmiQueryResult result = WmiHelper.Query(SysmanNamespace, \"SELECT * FROM \" + className, className, _logger, maxInstances: 1);")]
    [InlineData("ManagementScope scope = new(namespacePath, new ConnectionOptions { Timeout = ConnectTimeout });")]
    [InlineData("using ManagementObjectCollection instances = dcimClass.GetInstances(new EnumerationOptions { Timeout = QueryTimeout });")]
    [InlineData("$text = Get-Content -LiteralPath $stdoutFile -Encoding Oem")]
    public void Matcher_AcceptsTheReadOnlyHousekeepingOtherCodeDoes(string line)
    {
        // The tool's own data folder ACL, UTF-8 console output, the full-path PowerShell launch and
        // the Dell instance queries with timeouts change nothing in firmware, the BIOS or BitLocker.
        Assert.Empty(FindViolations(line));
    }

    [Theory]
    [InlineData("NtDll.ZwSetSystemEnvironmentValueEx(ref name, ref guid, buffer, length, attributes);")]
    [InlineData("& MANAGE-BDE -status")]
    [InlineData("uint result = Tbs.Tbsip_Submit_Command(context, 0, 0, command, length, response, ref size);")]
    [InlineData("Tbsi_Physical_Presence_Command(context, input, inputLength, output, ref outputLength);")]
    [InlineData("volume.InvokeMethod(\"BackupRecoveryInformationToCloudDomain\", inParameters, null);")]
    [InlineData("service.InvokeMethod(\"SetAttributes\", null, null);")]
    [InlineData("tpm.InvokeMethod(\"SetPhysicalPresenceRequest\", inParameters, null);")]
    public void Matcher_FlagsTheRemainingWriteAndSecretNames(string line)
    {
        Assert.NotEmpty(FindViolations(line));
    }

    [Theory]
    [InlineData("uint status = NtDll.NtEnumerateSystemEnvironmentValuesEx(informationClass, buffer, ref length);")]
    [InlineData("case \"enable\":")]
    [InlineData("case \"disable\":")]
    [InlineData("private const string Query = \"SELECT SpecVersion, IsEnabled_InitialValue, IsActivated_InitialValue FROM Win32_Tpm\";")]
    public void Matcher_AcceptsTheNearMissesOfTheWriteNames(string line)
    {
        Assert.Empty(FindViolations(line));
    }

    [Theory]
    [InlineData("Invoke-Expression \"Suspend-Bit$('Locker') -MountPoint C:\"")]
    [InlineData("iex $command")]
    [InlineData("& ([ScriptBlock]::Create($text))")]
    [InlineData("$ExecutionContext.InvokeCommand.InvokeScript($text)")]
    [InlineData("& $powershell -NoProfile -EncodedCommand $encoded")]
    [InlineData("Start-Process powershell -ArgumentList '-enc', $encoded")]
    public void Matcher_FlagsPowerShellThatRunsAString(string line)
    {
        Assert.NotEmpty(FindViolations(line));
    }

    [Theory]
    [InlineData("$text = Get-Content -LiteralPath $stdoutFile -Encoding Oem")]
    [InlineData("Set-Content -LiteralPath $path -Value $lines -Encoding UTF8")]
    [InlineData("& $ExePath info --json")]
    [InlineData("$exitCode = $LASTEXITCODE")]
    public void Matcher_AcceptsPowerShellThatRunsNamedCommands(string line)
    {
        Assert.Empty(FindViolations(line));
    }

    /// <summary>
    /// Applies every matcher to one source line and describes each hit. The line fails when it:
    /// <list type="bullet">
    /// <item>contains a name from <see cref="ForbiddenLiterals"/> in any casing, or the word wmic;</item>
    /// <item>runs PowerShell held in a string or encoded (Invoke-Expression, iex, [scriptblock]::Create, InvokeScript, NewScriptBlock, -EncodedCommand, -enc, -ec);</item>
    /// <item>contains a name from <see cref="ForbiddenMethodNames"/> in double or single quotes, calls it as ".Name(" or ".Name.Invoke", or through ForEach-Object, % or .ForEach('Name');</item>
    /// <item>names a PowerShell command matched by <see cref="ForbiddenCmdletPattern"/>;</item>
    /// <item>invokes a WMI method (InvokeMethod, GetMethodParameters, ExecMethod, Invoke-CimMethod, Invoke-WmiMethod) that is not on <see cref="AllowedWmiMethods"/>, uses the [wmi], [wmiclass] or [wmisearcher] accelerator or a System.Management type in PowerShell, or calls Put();</item>
    /// <item>runs bcdedit with a write switch, or holds a bcdedit write switch as a quoted argument, in any casing;</item>
    /// <item>passes cctk any option other than exactly --version or --secureboot, holds a set-style "--name=" or "-name=" literal, or holds "-i" or "--infile" as a literal;</item>
    /// <item>writes the registry: calls Registry.SetValue or SetValue on a Registry base key; calls OpenSubKey with a second argument other than false (or $false), or with arguments that do not close on the line; names a *-ItemProperty write command or its alias; names an item-changing command (New-Item, Remove-Item and the like, or an alias) on a line that also names a registry path; or runs reg with a changing subcommand, regedit on a file, or regini. CreateSubKey, DeleteSubKey, DeleteValue and the native writers are in <see cref="ForbiddenLiterals"/>, and a WMI StdRegProv setter is off the WMI allow-list.</item>
    /// </list>
    /// </summary>
    /// <param name="line">One line of C#, XAML or PowerShell.</param>
    /// <returns>Zero or more violation descriptions.</returns>
    internal static IReadOnlyList<string> FindViolations(string line)
    {
        List<string> violations = new();
        foreach (string literal in ForbiddenLiterals)
        {
            if (line.Contains(literal, StringComparison.OrdinalIgnoreCase))
            {
                violations.Add($"contains \"{literal}\"");
            }
        }

        foreach (string method in ForbiddenMethodNames)
        {
            if (line.Contains("\"" + method + "\"", StringComparison.Ordinal) || line.Contains("'" + method + "'", StringComparison.Ordinal))
            {
                violations.Add($"names the state-changing WMI method \"{method}\"");
            }
        }

        foreach (Match match in ForbiddenMethodCallPattern.Matches(line).Concat(ForbiddenMemberNameCallPattern.Matches(line)))
        {
            violations.Add($"calls the state-changing WMI method {match.Groups["method"].Value}");
        }

        if (WmicPattern.IsMatch(line))
        {
            violations.Add("names wmic, which calls any WMI method by name");
        }

        if (DynamicPowerShellPattern.IsMatch(line))
        {
            violations.Add("runs PowerShell held in a string or encoded (Invoke-Expression, a script block from text, -EncodedCommand), which the scan cannot read");
        }

        foreach (Match match in ForbiddenCmdletPattern.Matches(line))
        {
            violations.Add($"names the PowerShell command {match.Value}, which changes Secure Boot, BitLocker, TPM or WMI state or reads a BitLocker secret");
        }

        if (WmiInvocationPattern.IsMatch(line) || HasPowerShellInvocationOffTheAllowList(line))
        {
            violations.Add("invokes a WMI method that is not on the read-only allow-list (" + string.Join(", ", AllowedWmiMethods) + ")");
        }

        if (WmiAcceleratorPattern.IsMatch(line))
        {
            violations.Add("uses a WMI type accelerator or System.Management type in PowerShell, whose objects expose WMI methods as direct calls");
        }

        if (WmiPutPattern.IsMatch(line))
        {
            violations.Add("writes a WMI instance back with Put()");
        }

        if (BcdEditWritePattern.IsMatch(line))
        {
            violations.Add("runs bcdedit with a switch that changes the boot configuration");
        }

        if (BcdEditSwitchLiteralPattern.IsMatch(line))
        {
            violations.Add("contains a bcdedit write switch as an argument literal");
        }

        foreach (string option in CctkOptionsOffTheAllowList(line))
        {
            violations.Add($"cctk invoked with {option}; only --version and --secureboot are allowed");
        }

        if (SetStyleArgumentLiteralPattern.IsMatch(line))
        {
            violations.Add("contains a set-style command-line argument literal (\"--name=value\" or \"-name=value\")");
        }

        if (CctkApplyFileLiteralPattern.IsMatch(line))
        {
            violations.Add("contains the cctk apply-file argument (-i or --infile) as a literal");
        }

        if (RegistrySetValuePattern.IsMatch(line))
        {
            violations.Add("sets a registry value through the Registry class");
        }

        violations.AddRange(RegistryOpensForWriting(line));

        if (ItemPropertyWriteCmdletPattern.IsMatch(line))
        {
            violations.Add("names a PowerShell command that changes item properties, registry values among them");
        }

        if (RegistryPathPattern.IsMatch(line) && ItemWriteCmdletPattern.IsMatch(line))
        {
            violations.Add("changes an item on a line that names a registry path");
        }

        if (RegistryToolWritePattern.IsMatch(line))
        {
            violations.Add("runs reg, regedit or regini to change the registry");
        }

        return violations;
    }

    /// <summary>
    /// The allowance that applies to a line: the entry of <see cref="WriteSiteAllowances"/> for
    /// this file whose literal the line holds, in any casing; null for every other line, which
    /// is judged by <see cref="FindViolations"/> alone.
    /// </summary>
    /// <param name="file">The file's path relative to the repository root, with "/" separators.</param>
    /// <param name="line">One line of the file.</param>
    /// <returns>The allowance, or null.</returns>
    internal static WriteSiteAllowance? AllowanceFor(string file, string line) =>
        WriteSiteAllowances.FirstOrDefault(allowance => allowance.File == file && line.Contains(allowance.Literal, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The violations of a line that lie outside its allowance: the line is judged as if it held
    /// the allowance's read-only counterpart wherever it holds the literal, in any casing, so the
    /// allowed name itself (and, for the suspender, the WMI invocation of its constant) is
    /// accepted and everything else on the line is checked exactly as on any other line.
    /// </summary>
    /// <param name="line">One line of the allow-listed file.</param>
    /// <param name="allowance">The allowance that applies to it.</param>
    /// <returns>Zero or more violation descriptions.</returns>
    internal static IReadOnlyList<string> ViolationsOutsideAllowance(string line, WriteSiteAllowance allowance) =>
        FindViolations(line.Replace(allowance.Literal, allowance.ReadOnlyCounterpart, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// What is wrong with the way a source file suspends BitLocker, read with its comments blanked: exactly one InvokeMethod call and exactly one
    /// GetMethodParameters call, each passing DisableKeyProtectorsMethod as the method and no
    /// string literal, the fetch first; neither handed on as a method group; nothing invoked or
    /// set through ExecMethod, SetPropertyValue or the Properties collection; exactly one indexer
    /// assignment in the file, ["DisableCount"] = OneRestart, between that fetch and that
    /// invocation, so no other parameter is set and the count is never a literal; and OneRestart
    /// never assigned outside its constant declaration. The protector list the suspender compares
    /// is read through the inspector's read-only seam, so no reader is invoked here. The constants
    /// themselves are pinned by reflection.
    /// </summary>
    /// <param name="source">The C# source of the file that suspends BitLocker.</param>
    /// <returns>One description per problem; empty when the file makes the one-restart suspend and nothing else.</returns>
    internal static IReadOnlyList<string> BitLockerSuspendViolations(string source)
    {
        string code = SourceCalls.WithoutComments(source);
        List<string> violations = new();
        Dictionary<string, int> callLines = new(StringComparer.Ordinal);
        foreach ((string method, string role) in new[] { ("GetMethodParameters", "the parameter fetch"), ("InvokeMethod", "the invocation") })
        {
            SourceCall[] calls = SourceCalls.Find(code, method).Where(site => !site.IsDeclaration).ToArray();
            if (calls.Length == 1)
            {
                callLines[method] = calls[0].Line;
            }
            else
            {
                violations.Add($"expected exactly one {method} call ({role}), found {calls.Length}");
            }

            foreach (SourceCall call in calls)
            {
                string? name = SourceCalls.ArgumentFor(call.Arguments, "methodName", 0);
                if (name != SuspendMethodConstant)
                {
                    violations.Add($"line {call.Line}: {method} is passed {name ?? "no recognisable method"}; only {SuspendMethodConstant} is allowed");
                }

                if (call.Arguments.Any(ContainsStringLiteral))
                {
                    violations.Add($"line {call.Line}: {method}({string.Join(", ", call.Arguments)}) passes a string literal");
                }
            }

            foreach (int line in SourceCalls.FindReferencesWithoutCall(code, method))
            {
                violations.Add($"line {line}: {method} is referenced without being called, so its method cannot be checked");
            }
        }

        foreach (int line in SourceCalls.LinesMatching(code, OtherWmiWritePattern))
        {
            violations.Add($"line {line}: invokes or sets WMI through ExecMethod, SetPropertyValue or the Properties collection");
        }

        int? fetchLine = callLines.TryGetValue("GetMethodParameters", out int fetch) ? fetch : null;
        int? invokeLine = callLines.TryGetValue("InvokeMethod", out int invoke) ? invoke : null;
        MatchCollection assignments = IndexerAssignmentPattern.Matches(code);
        if (assignments.Count != 1)
        {
            violations.Add($"expected exactly one indexer assignment, [\"DisableCount\"] = OneRestart, found {assignments.Count}");
        }

        foreach (Match assignment in assignments)
        {
            int line = SourceCalls.LineOf(code, assignment.Index);
            string key = assignment.Groups["key"].Value.Trim();
            string value = assignment.Groups["value"].Value.Trim();
            if (key != "\"DisableCount\"" || value != "OneRestart")
            {
                violations.Add($"line {line}: [{key}] = {value}; only [\"DisableCount\"] = OneRestart is allowed");
            }

            if (!(line >= fetchLine && line <= invokeLine))
            {
                violations.Add($"line {line}: [{key}] is set outside the lines from GetMethodParameters({SuspendMethodConstant}) to InvokeMethod({SuspendMethodConstant})");
            }
        }

        foreach (Match match in OneRestartAssignedPattern.Matches(code))
        {
            int line = SourceCalls.LineOf(code, match.Index);
            if (!Regex.IsMatch(code.Split('\n')[line - 1], @"\bconst\s+uint\s+OneRestart\b", CaseSensitive))
            {
                violations.Add($"line {line}: OneRestart is assigned ({match.Value.Trim()})");
            }
        }

        return violations;
    }

    /// <summary>A constant field of a type, public or not; fails the test when it is missing or not a constant.</summary>
    private static FieldInfo LiteralField(Type type, string name)
    {
        FieldInfo? field = type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.True(field is { IsLiteral: true }, $"{type.Name}.{name} must be a constant");
        return field!;
    }

    /// <summary>
    /// The OpenSubKey calls on a line that may open a registry key for writing: any call with a
    /// second argument other than false (or $false), whether a writable flag, a permission check or
    /// registry rights, and any call whose arguments do not close on the line, which cannot be read.
    /// </summary>
    private static IEnumerable<string> RegistryOpensForWriting(string line)
    {
        foreach (Match match in OpenSubKeyCallPattern.Matches(line))
        {
            int start = match.Index + match.Length;
            int close = ClosingParenthesis(line, start);
            if (close < 0)
            {
                yield return "calls OpenSubKey with arguments that do not close on this line, so the scan cannot tell it opens the key read-only";
                continue;
            }

            IReadOnlyList<string> arguments = SourceCalls.SplitTopLevelArguments(line[start..close]);
            if (arguments.Count > 1 && !ReadOnlyOpenArgumentPattern.IsMatch(StripArgumentName(arguments[1])))
            {
                yield return $"opens a registry key with OpenSubKey({string.Join(", ", arguments)}); only one argument, or false as the second, keeps it read-only";
            }
        }
    }

    /// <summary>The index of the ")" that closes an argument list starting at <paramref name="start"/> (just after its "("), skipping string literals; -1 when it does not close.</summary>
    private static int ClosingParenthesis(string text, int start)
    {
        int depth = 0;
        char quote = '\0';
        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (quote != '\0')
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            switch (c)
            {
                case '"':
                case '\'':
                    quote = c;
                    break;
                case '(':
                    depth++;
                    break;
                case ')' when depth == 0:
                    return i;
                case ')':
                    depth--;
                    break;
            }
        }

        return -1;
    }

    /// <summary>
    /// True when an Invoke-CimMethod or Invoke-WmiMethod on the line (up to the next such command)
    /// names no method, or names one that is not on <see cref="AllowedWmiMethods"/>, a variable included.
    /// </summary>
    private static bool HasPowerShellInvocationOffTheAllowList(string line)
    {
        MatchCollection invokers = PowerShellWmiInvokerPattern.Matches(line);
        for (int i = 0; i < invokers.Count; i++)
        {
            int end = i + 1 < invokers.Count ? invokers[i + 1].Index : line.Length;
            string segment = line[invokers[i].Index..end];
            MatchCollection names = PowerShellMethodArgumentPattern.Matches(segment);
            if (names.Count == 0 || names.Any(name => !AllowedWmiMethods.Contains(name.Groups["name"].Value.Trim('"', '\''), StringComparer.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The options after the word cctk (in any casing, so $CctkPath counts) that are not exactly
    /// --version or --secureboot: any other option, any option with "=value", and any single-dash
    /// option that is not a PowerShell parameter or operator.
    /// </summary>
    private static IEnumerable<string> CctkOptionsOffTheAllowList(string line)
    {
        int start = line.IndexOf("cctk", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            yield break;
        }

        foreach (Match match in CommandLineOptionPattern.Matches(line, start))
        {
            string option = match.Groups["option"].Value;
            bool setStyle = match.Groups["value"].Success;
            if (!setStyle && (AllowedCctkOptions.Contains(option, StringComparer.Ordinal) || PowerShellParameterOrOperatorPattern.IsMatch(option)))
            {
                continue;
            }

            yield return setStyle ? option + "=" : option;
        }
    }

    /// <summary>
    /// What is wrong with the way a source file runs cctk: exactly one RunCctk(string cctkPath,
    /// string argument, ...) may be declared; every call of it, positional or with named
    /// arguments, must pass VersionArgument or SecureBootArgument as its argument; it may not be
    /// handed on as a method group; and every ProcessRunner.Run call must pass, after the logger,
    /// that parameter alone, with no string literal anywhere and the parameter never reassigned.
    /// </summary>
    /// <param name="source">The C# source of the file that runs cctk.</param>
    /// <returns>One description per problem; empty when the file only ever runs the two read-only options.</returns>
    internal static IReadOnlyList<string> CctkLaunchViolations(string source)
    {
        List<string> violations = new();
        IReadOnlyList<SourceCall> sites = SourceCalls.Find(source, "RunCctk");
        SourceCall[] declarations = sites.Where(site => site.IsDeclaration).ToArray();
        if (declarations.Length != 1 || declarations[0].Arguments.Count < 2 || !Regex.IsMatch(declarations[0].Arguments[1], @"\bstring\s+argument$"))
        {
            violations.Add($"expected one RunCctk(string cctkPath, string argument, ...) declaration, found {declarations.Length}");
        }

        foreach (SourceCall call in sites.Where(site => !site.IsDeclaration))
        {
            string? argument = SourceCalls.ArgumentFor(call.Arguments, "argument", 1);
            if (argument is not ("VersionArgument" or "SecureBootArgument"))
            {
                violations.Add($"line {call.Line}: RunCctk is passed {argument ?? "no recognisable argument"}; only VersionArgument and SecureBootArgument are allowed");
            }
        }

        foreach (int line in SourceCalls.FindReferencesWithoutCall(source, "RunCctk"))
        {
            violations.Add($"line {line}: RunCctk is referenced without being called, so its argument cannot be checked");
        }

        IReadOnlyList<SourceCall> runs = SourceCalls.Find(source, "ProcessRunner.Run");
        if (runs.Count == 0)
        {
            violations.Add("no ProcessRunner.Run call found; the check would be vacuous");
        }

        foreach (SourceCall run in runs)
        {
            int logger = IndexOfLogger(run.Arguments);
            string[] commandLine = run.Arguments.Skip(logger + 1).Select(StripArgumentName).ToArray();
            if (run.IsDeclaration || logger < 0 || commandLine is not ["argument"] || run.Arguments.Any(ContainsStringLiteral))
            {
                violations.Add($"line {run.Line}: ProcessRunner.Run({string.Join(", ", run.Arguments)}) must pass the logger, then RunCctk's argument alone, and no literal");
            }
        }

        foreach (Match match in Regex.Matches(source, @"(?<![\w.])argument\s*(?:\?\?=|\+=|=(?![=>]))|\b(?:out|ref)\s+argument\b"))
        {
            violations.Add($"line {1 + source.AsSpan(0, match.Index).Count('\n')}: RunCctk's argument is reassigned ({match.Value.Trim()})");
        }

        return violations;
    }

    /// <summary>
    /// What is wrong with the way the Dell settings reader runs cctk, read with comments and string literals blanked: there must be exactly one declaration
    /// RunCctk(CctkReadOption option, TimeSpan bound), with no path or string parameter, and RunCctk
    /// is neither called nor named as a method group in the file; exactly one ProcessRunner.Run
    /// call, inside RunCctk, never a method group, whose program is the field _cctkPath, whose
    /// timeout is a local declared in RunCctk before the call as
    /// "TimeSpan limit = bound &lt; CctkSettingsProvider.RunTimeout ? bound : CctkSettingsProvider.RunTimeout;"
    /// and never assigned again, which passes the logger and then ArgumentFor(option) alone, with
    /// no string literal in any argument; option is never assigned, stepped or passed by
    /// reference; _cctkPath is a private readonly field with no initializer, assigned exactly once,
    /// in the constructor, from DellInterfaceProbe.FindCctk(logger), which names it nowhere else,
    /// and no init accessor with a body could set it; and ArgumentFor is declared once, with no
    /// string or character literal in its body, as one arm per member returning that member's
    /// constant and a discard arm that throws. ProcessRunner.Run is counted however it is laid
    /// out; the class is not partial; it has no "using static" or using alias; and the names the
    /// check trusts as text (DellInterfaceProbe, CctkSettingsProvider, ProcessRunner, ArgumentFor)
    /// are never declared or named in the file except as the real type or method. What the
    /// constants hold, what ArgumentFor returns, what the run timeout holds, the one constructor
    /// and the types those names bind to are pinned by reflection, and the reader's code as a
    /// whole by <see cref="CctkReader_CodeIsTheVersionReviewedAtGateGA"/>.
    /// </summary>
    /// <param name="source">The C# source of the reader.</param>
    /// <returns>One description per problem; empty when the reader only ever runs one read option, by its fixed path, within the clamped bound.</returns>
    internal static IReadOnlyList<string> CctkReaderLaunchViolations(string source)
    {
        List<string> violations = new();
        string code = SourceCalls.WithoutCommentsAndStrings(source);

        IReadOnlyList<SourceCall> sites = SourceCalls.Find(source, "RunCctk");
        SourceCall[] declarations = sites.Where(site => site.IsDeclaration).ToArray();
        if (declarations.Length != 1 || !declarations[0].Arguments.SequenceEqual(new[] { "CctkReadOption option", "TimeSpan bound" }, StringComparer.Ordinal))
        {
            violations.Add($"expected exactly one RunCctk(CctkReadOption option, TimeSpan bound) declaration, found {declarations.Length}: " + string.Join("; ", declarations.Select(declaration => "RunCctk(" + string.Join(", ", declaration.Arguments) + ")")));
        }

        foreach (SourceCall call in sites.Where(site => !site.IsDeclaration))
        {
            violations.Add($"line {call.Line}: RunCctk is called inside the reader; its one caller is the settings provider");
        }

        foreach (int line in SourceCalls.FindReferencesWithoutCall(source, "RunCctk"))
        {
            violations.Add($"line {line}: RunCctk is referenced without being called");
        }

        foreach (int line in SourceCalls.FindReferencesWithoutCall(source, "ProcessRunner.Run"))
        {
            violations.Add($"line {line}: ProcessRunner.Run is referenced without being called, so its arguments cannot be checked");
        }

        // SourceCalls.Find reads "ProcessRunner.Run(" as written; the mentions are counted however
        // they are laid out ("ProcessRunner" and ".Run(" on two lines, or "@Run"), so a second
        // launch cannot hide from the count, and a call through "using static" is refused below.
        string runCctkBody = string.Join("\n", SourceCalls.MethodBodies(source, "RunCctk"));
        Match runInBody = ProcessRunnerRunCallPattern.Match(runCctkBody);
        IReadOnlyList<SourceCall> runs = SourceCalls.Find(source, "ProcessRunner.Run");
        int runMentions = ProcessRunnerRunMentionPattern.Matches(code).Count;
        if (runs.Count != 1 || runMentions != 1 || !runInBody.Success)
        {
            violations.Add($"expected exactly one ProcessRunner.Run call, inside RunCctk; found {runs.Count} written as such and {runMentions} however laid out" + (runInBody.Success ? string.Empty : ", none of them in RunCctk"));
        }

        foreach (Match match in HidingUsingPattern.Matches(code))
        {
            violations.Add($"line {SourceCalls.LineOf(code, match.Index)}: a using static directive or a using alias hides what a name in the reader binds to; import namespaces only");
        }

        foreach (Match match in TrustedNameRebindingPattern.Matches(code))
        {
            violations.Add($"line {SourceCalls.LineOf(code, match.Index)}: {match.Value} is declared or used other than as the type the check trusts");
        }

        foreach (int line in SourceCalls.FindReferencesWithoutCall(source, "ArgumentFor"))
        {
            violations.Add($"line {line}: ArgumentFor is named without being called; a local or member of that name would replace the method");
        }

        if (Regex.IsMatch(code, @"\bpartial\b", CaseSensitive))
        {
            violations.Add("CctkReader must not be partial: another file could add a constructor that sets _cctkPath");
        }

        foreach (Match match in InitAccessorPattern.Matches(code))
        {
            violations.Add($"line {SourceCalls.LineOf(code, match.Index)}: an init accessor with a body may assign _cctkPath as the constructor can");
        }

        foreach (SourceCall run in runs)
        {
            string call = $"line {run.Line}: ProcessRunner.Run({string.Join(", ", run.Arguments)})";
            string? program = SourceCalls.ArgumentFor(run.Arguments, "fileName", 0);
            if (program != "_cctkPath")
            {
                violations.Add($"{call} runs {program ?? "no recognisable program"}; only the field _cctkPath, which the constructor sets");
            }

            string? timeout = SourceCalls.ArgumentFor(run.Arguments, "timeout", 2);
            if (!IsClampedBound(code, runCctkBody, runInBody, timeout))
            {
                violations.Add($"{call} waits for {timeout ?? "no recognisable bound"}, which is not a local clamped to CctkSettingsProvider.RunTimeout in RunCctk before the call and never assigned again");
            }

            int logger = IndexOfLogger(run.Arguments);
            string[] commandLine = run.Arguments.Skip(logger + 1).Select(argument => Regex.Replace(StripArgumentName(argument), @"\s+", string.Empty)).ToArray();
            if (run.IsDeclaration || logger < 0 || commandLine is not ["ArgumentFor(option)"])
            {
                violations.Add($"{call} must pass the logger, then ArgumentFor(option) alone");
            }

            if (run.Arguments.Any(ContainsStringLiteral))
            {
                violations.Add($"{call} passes a string literal");
            }
        }

        foreach (Match match in OptionAssignedPattern.Matches(code))
        {
            violations.Add($"line {SourceCalls.LineOf(code, match.Index)}: RunCctk's option is reassigned ({match.Value.Trim()})");
        }

        if (!CctkPathFieldPattern.IsMatch(code))
        {
            violations.Add("_cctkPath must be declared as a private readonly string? field without an initializer");
        }

        // The constructor names the field once, in that assignment, so a deconstruction such as
        // "(_cctkPath, _) = (path, 0);", which the assignment pattern does not read, cannot follow it.
        MatchCollection pathAssignments = CctkPathAssignedPattern.Matches(code);
        IReadOnlyList<string> constructors = SourceCalls.MethodBodies(source, "CctkReader");
        int inConstructor = constructors.Sum(body => CctkPathAssignedPattern.Matches(body).Count);
        int mentionsInConstructor = constructors.Sum(body => CctkPathMentionPattern.Matches(body).Count);
        if (pathAssignments.Count != 1 || inConstructor != 1 || mentionsInConstructor != 1 || !FindCctkValuePattern.IsMatch(pathAssignments[0].Groups["value"].Value.Trim()))
        {
            violations.Add($"_cctkPath must be assigned exactly once, in the constructor, from DellInterfaceProbe.FindCctk(logger), and named nowhere else there; found {mentionsInConstructor} mention(s) in the constructor and: " + string.Join("; ", pathAssignments.Select(match => match.Value.Trim())));
        }

        IReadOnlyList<string> argumentFor = SourceCalls.MethodBodies(WithLiteralsMarked(source), "ArgumentFor");
        if (argumentFor.Count != 1)
        {
            violations.Add($"expected exactly one ArgumentFor declaration with a body, found {argumentFor.Count}");
        }
        else if (argumentFor[0].Contains(LiteralMark, StringComparison.Ordinal))
        {
            violations.Add("ArgumentFor holds a string or character literal; it may return only the reader's option constants");
        }
        else if (!ArgumentForShapePattern.IsMatch(argumentFor[0]))
        {
            violations.Add("ArgumentFor must be one arm per CctkReadOption member returning that member's constant, then a discard arm that throws");
        }

        return violations;
    }

    /// <summary>
    /// True when <paramref name="timeout"/> names a local that RunCctk declares before the
    /// ProcessRunner.Run call as the bound clamped to CctkSettingsProvider.RunTimeout, exactly
    /// once, and that the file never assigns, steps or passes by reference anywhere else; the
    /// name stands in the code exactly twice, in that declaration and as the call's timeout, so
    /// a deconstruction ("(limit, _) = ...") or "in limit" cannot change it either.
    /// </summary>
    /// <param name="code">The whole source, comments and literals blanked.</param>
    /// <param name="runCctkBody">RunCctk's body, comments and literals blanked.</param>
    /// <param name="runInBody">The ProcessRunner.Run call in that body.</param>
    /// <param name="timeout">The call's timeout argument.</param>
    private static bool IsClampedBound(string code, string runCctkBody, Match runInBody, string? timeout)
    {
        if (timeout is null || !runInBody.Success || !Regex.IsMatch(timeout, @"^[A-Za-z_]\w*$", CaseSensitive))
        {
            return false;
        }

        string name = Regex.Escape(timeout);
        MatchCollection clamps = Regex.Matches(
            runCctkBody,
            @"\bTimeSpan\s+" + name + @"\s*=\s*bound\s*<\s*CctkSettingsProvider\s*\.\s*RunTimeout\s*\?\s*bound\s*:\s*CctkSettingsProvider\s*\.\s*RunTimeout\s*;",
            CaseSensitive);
        int assignments = Regex.Matches(code, @"(?<![\w.])" + name + @"\s*(?:[-+*/%&|^]=|<<=|>>=|\?\?=|\+\+|--|=(?![=>]))|(?:\+\+|--)\s*" + name + @"\b|\b(?:out|ref)\s+" + name + @"\b", CaseSensitive).Count;
        int mentions = Regex.Matches(code, @"(?<!\w)" + name + @"(?!\w)", CaseSensitive).Count;
        return clamps.Count == 1 && clamps[0].Index < runInBody.Index && assignments == 1 && mentions == 2;
    }

    /// <summary>
    /// True when a "=&gt;" stands between the start of the statement that holds
    /// <paramref name="index"/> (after the last ";" or "}") and the index: the call is the body of a
    /// lambda, or of an expression-bodied member, which a text scan cannot tell apart. A tripwire,
    /// not a parse.
    /// </summary>
    private static bool IsInsideLambda(string code, int index)
    {
        int start = index == 0 ? 0 : code.LastIndexOfAny(new[] { ';', '}' }, index - 1) + 1;
        return code.AsSpan(start, index - start).Contains("=>", StringComparison.Ordinal);
    }

    /// <summary>
    /// The source with its comments blanked and every character of a string or character literal
    /// that differs from a space (the quotes always do) replaced by <see cref="LiteralMark"/>, so a
    /// search over a method body, which blanks literals, can still see where one stood. Every line
    /// number and index is unchanged.
    /// </summary>
    private static string WithLiteralsMarked(string source)
    {
        string text = SourceCalls.WithoutComments(source);
        char[] marked = SourceCalls.WithoutCommentsAndStrings(source).ToCharArray();
        for (int i = 0; i < marked.Length; i++)
        {
            if (marked[i] != text[i])
            {
                marked[i] = LiteralMark;
            }
        }

        return new string(marked);
    }

    /// <summary>
    /// The SHA-256, in upper-case hexadecimal, of a source's code: comments blanked and every
    /// whitespace character removed, so a comment, the indentation, the line endings and the
    /// layout do not count, and every token and literal does (a literal's own spaces aside; what
    /// the reader's constants hold is pinned by reflection).
    /// </summary>
    private static string ReaderCodeDigest(string source)
    {
        string code = new(SourceCalls.WithoutComments(source).Where(character => !char.IsWhiteSpace(character)).ToArray());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
    }

    /// <summary>
    /// What is wrong with the way a source file runs PowerShell: every ProcessRunner.Run call must
    /// pass no string literal before the logger (so no literal names the executable) and, after
    /// it, only the switches of <see cref="AllowedPowerShellSwitches"/>, then "-Command" and the
    /// name of a constant whose statements (split at ";") are all in
    /// <see cref="AllowedPowerShellStatements"/>, the last being Confirm-SecureBootUEFI.
    /// </summary>
    /// <param name="source">The C# source of the file that runs PowerShell.</param>
    /// <param name="constants">The string constants the file declares, by name, with the values the compiler folded.</param>
    /// <returns>One description per problem; empty when the file only ever runs Confirm-SecureBootUEFI.</returns>
    internal static IReadOnlyList<string> PowerShellLaunchViolations(string source, IReadOnlyDictionary<string, string> constants)
    {
        List<string> violations = new();
        IReadOnlyList<SourceCall> runs = SourceCalls.Find(source, "ProcessRunner.Run");
        if (runs.Count == 0)
        {
            violations.Add("no ProcessRunner.Run call found; the check would be vacuous");
        }

        foreach (SourceCall run in runs)
        {
            string call = $"line {run.Line}: ProcessRunner.Run({string.Join(", ", run.Arguments)})";
            int logger = IndexOfLogger(run.Arguments);
            if (run.IsDeclaration || logger < 0)
            {
                violations.Add(call + " passes no logger");
                continue;
            }

            if (run.Arguments.Take(logger).Any(ContainsStringLiteral))
            {
                violations.Add(call + " passes a string literal before the logger");
            }

            string[] commandLine = run.Arguments.Skip(logger + 1).Select(StripArgumentName).ToArray();
            if (commandLine.Length < 2 || commandLine[^2] != "\"-Command\"")
            {
                violations.Add(call + " must end with \"-Command\" and the script constant");
                continue;
            }

            foreach (string powerShellSwitch in commandLine[..^2].Where(value => !AllowedPowerShellSwitches.Contains(value, StringComparer.Ordinal)))
            {
                violations.Add($"{call} passes {powerShellSwitch}; only {string.Join(", ", AllowedPowerShellSwitches)} may come before \"-Command\"");
            }

            if (!constants.TryGetValue(commandLine[^1], out string? script))
            {
                violations.Add($"{call}: the -Command script {commandLine[^1]} is not a string constant of the class");
                continue;
            }

            string[] statements = script.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (statements.Length == 0 || statements[^1] != "Confirm-SecureBootUEFI" || statements.Any(statement => !AllowedPowerShellStatements.Contains(statement, StringComparer.Ordinal)))
            {
                violations.Add($"{call}: the -Command script \"{script}\" may hold only {string.Join(" and ", AllowedPowerShellStatements)}, ending with Confirm-SecureBootUEFI");
            }
        }

        return violations;
    }

    /// <summary>
    /// The source <see cref="BitLockerSuspendViolations"/> is tested on, laid out as the suspender
    /// is: the seam method with the parameter fetch, the count and the invocation, and one extra
    /// line.
    /// </summary>
    private static string SuspendSource(string extraLine) =>
        "public uint SuspendKeyProtectors(uint disableCount)\n"
        + "{\n"
        + "    using ManagementBaseObject inParameters = _volume.GetMethodParameters(DisableKeyProtectorsMethod);\n"
        + "    inParameters[\"DisableCount\"] = OneRestart;\n"
        + "    using ManagementBaseObject outParameters = _volume.InvokeMethod(DisableKeyProtectorsMethod, inParameters, new InvokeMethodOptions());\n"
        + "}\n"
        + extraLine + "\n";

    /// <summary>The source <see cref="CctkLaunchViolations"/> is tested on: the probe's two calls, one extra line, and RunCctk as declared.</summary>
    private static string CctkLaunchSource(string extraLine) =>
        "cctkVersion = RunCctk(cctkPath, VersionArgument, errors);\n"
        + "cctkSecureBoot = RunCctk(cctkPath, SecureBootArgument, errors);\n"
        + extraLine + "\n"
        + "private string? RunCctk(string cctkPath, string argument, List<string> errors)\n"
        + "{\n"
        + "    ProcessRunResult run = ProcessRunner.Run(cctkPath, workingDirectory, CctkTimeout, _logger, argument);\n"
        + "}\n";

    /// <summary>
    /// The source <see cref="CctkReaderLaunchViolations"/> is tested on: the Dell settings reader as
    /// written, with two of its nine options, and a marker line, "// (extra)", where a test puts
    /// one more statement inside RunCctk.
    /// </summary>
    private const string CctkReaderSource =
        "internal sealed class CctkReader\n"
        + "{\n"
        + "    internal const string SecureBootArgument = \"--secureboot\";\n"
        + "    internal const string NumLockArgument = \"--numlock\";\n"
        + "    private const string NotFoundError = \"cctk.exe was not found\";\n"
        + "    private readonly ILogger _logger;\n"
        + "    private readonly string? _cctkPath;\n"
        + "\n"
        + "    public CctkReader(ILogger? logger)\n"
        + "    {\n"
        + "        _logger = logger ?? Serilog.Core.Logger.None;\n"
        + "        _cctkPath = DellInterfaceProbe.FindCctk(_logger);\n"
        + "    }\n"
        + "\n"
        + "    internal bool Found => _cctkPath is not null;\n"
        + "\n"
        + "    internal static string ArgumentFor(CctkReadOption option) => option switch\n"
        + "    {\n"
        + "        CctkReadOption.SecureBoot => SecureBootArgument,\n"
        + "        CctkReadOption.NumLock => NumLockArgument,\n"
        + "        _ => throw new ArgumentOutOfRangeException(nameof(option)),\n"
        + "    };\n"
        + "\n"
        + "    internal CctkRunOutcome RunCctk(CctkReadOption option, TimeSpan bound)\n"
        + "    {\n"
        + "        if (_cctkPath is null)\n"
        + "        {\n"
        + "            return new CctkRunOutcome(option, ArgumentFor(option), false, null, string.Empty, string.Empty, false, NotFoundError, 0);\n"
        + "        }\n"
        + "\n"
        + "        TimeSpan limit = bound < CctkSettingsProvider.RunTimeout ? bound : CctkSettingsProvider.RunTimeout;\n"
        + "        string? workingDirectory = Path.GetDirectoryName(_cctkPath);\n"
        + "        // (extra)\n"
        + "        ProcessRunResult run = ProcessRunner.Run(_cctkPath, workingDirectory, limit, _logger, ArgumentFor(option));\n"
        + "        return new CctkRunOutcome(option, ArgumentFor(option), run.Started, run.ExitCode, run.StandardOutput, run.StandardError, run.TimedOut, run.Error, run.ElapsedMilliseconds);\n"
        + "    }\n"
        + "}\n";

    /// <summary>The position of the logger among a ProcessRunner.Run call's arguments, or -1; the command-line arguments follow it.</summary>
    private static int IndexOfLogger(IReadOnlyList<string> arguments) =>
        arguments.ToList().FindIndex(argument => Regex.IsMatch(StripArgumentName(argument), @"^(?:this\.)?_?[Ll]og(?:ger)?$"));

    /// <summary>The argument without a leading "name:", so a named argument compares like a positional one.</summary>
    private static string StripArgumentName(string argument) => Regex.Replace(argument, @"^@?[A-Za-z_]\w*\s*:(?!:)\s*", string.Empty);

    /// <summary>True when the argument holds a string literal anywhere, alone or in a concatenation.</summary>
    private static bool ContainsStringLiteral(string argument) => argument.Contains('"', StringComparison.Ordinal);

    /// <summary>Every file the guard scans: C# and XAML under src/, PowerShell scripts and modules under tools/, build output excluded.</summary>
    /// <returns>Absolute paths, sorted ordinally.</returns>
    private static IReadOnlyList<string> ScannedFiles()
    {
        return RepositoryPaths.Enumerate("src", "*.cs", "*.xaml")
            .Concat(RepositoryPaths.Enumerate("tools", "*.ps1", "*.psm1"))
            .ToArray();
    }
}

/// <summary>One write site of M3 (see <see cref="NoWriteGuardTests.WriteSiteAllowances"/>).</summary>
/// <param name="File">The file's path relative to the repository root, with "/" separators.</param>
/// <param name="Literal">The one entry of the forbidden literals the file may hold.</param>
/// <param name="ReadOnlyCounterpart">The read-only name a line is judged as if it held instead, so everything else on the line is still checked.</param>
/// <param name="ExpectedLines">The exact number of lines of the committed file that hold the literal, comments and strings included.</param>
internal sealed record WriteSiteAllowance(string File, string Literal, string ReadOnlyCounterpart, int ExpectedLines);
