// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.AccessControl;
using System.Security.Principal;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using UefiAccess.Core.Privileges;

namespace UefiAccess.Core.Diagnostics;

/// <summary>Where <see cref="Logging"/> keeps its files.</summary>
/// <param name="DataDirectory">The tool's shared data root (ProgramData\UefiAccess on Windows).</param>
/// <param name="SharedLogDirectory">The shared log folder under the data root.</param>
/// <param name="UserLogDirectory">The per-user log folder used when the shared one cannot be written.</param>
internal sealed record LogLocations(string DataDirectory, string SharedLogDirectory, string UserLogDirectory);

/// <summary>
/// Serilog setup shared by the CLI, the WPF shell, and tests. Logs go to
/// ProgramData\UefiAccess\logs\uefiaccess-&lt;date&gt;.log on Windows (LocalApplicationData/UefiAccess/logs
/// elsewhere), optionally mirrored to the console on standard error so that JSON output on
/// standard output stays clean. Creating a logger never throws.
/// </summary>
/// <remarks>
/// <para>
/// The elevated tool writes logs and baselines under the data root and reads baselines from it,
/// so an elevated run writes only through folders that standard users cannot redirect
/// (<see cref="PrepareDataFolderFor(string)"/>). Every existing folder from ProgramData\UefiAccess
/// down to the one it writes must be a plain folder, not a junction, symbolic link or other
/// reparse point, and must be owned by SYSTEM, Administrators or TrustedInstaller, because the
/// owner of a folder can turn it into a link at any moment. No other owner is trusted, not even
/// the default owner of the elevated run's own token (<see cref="IsTrustedOwner"/> says why).
/// Missing folders, the data root included, are created with an explicit, protected access list
/// (<see cref="CreateDataDirectorySecurity"/>): SYSTEM and Administrators full control, Users read
/// and execute, inherited by everything inside; and with Administrators as their owner
/// (<see cref="CreationOwner"/>). An existing folder's access list and owner are never changed.
/// When a folder fails the check, the elevated run does not write there: its log goes to the
/// per-user folder, and an export into the data root is refused with the reason.
/// </para>
/// <para>
/// The per-user folder belongs to the user, so an elevated run that falls back to it checks it
/// the same way for links (<see cref="PrepareUserLogFolderForElevatedRun"/>): the local
/// application data folder, its UefiAccess folder and the logs folder must each be a plain
/// folder, and no existing log file there may be a link. When one is not, the elevated run keeps
/// no log file and says so in one line on standard error. The check is made when the per-user log
/// is opened; a program of the same account could still replace a folder after that moment. That
/// is the boundary between an account's elevated and non-elevated processes, which Windows does
/// not treat as a security boundary; closing it would need the log opened by handle with links
/// refused, which Serilog's file sink does not offer.
/// </para>
/// <para>
/// A process that is not elevated never creates anything under the data root. When the shared log
/// folder is missing or cannot be written (a standard user cannot write a folder or a day's file
/// that an elevated run created), the run logs to <see cref="UserLogDirectory"/> instead. The move
/// is recorded once, as a warning at the top of the per-user log. Nothing is written to standard
/// error for it, so a command's own first message (such as the sentence that refuses a command
/// without elevation) is the first thing a screen reader reads. Standard error gets one line only
/// when no log file can be kept at all. The switch is made on the first write that fails, because
/// the file sink opens its file lazily.
/// </para>
/// </remarks>
public static class Logging
{
    private const string FileTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{Component}] {Message:lj}{NewLine}{Exception}";
    private const string ConsoleTemplate = "[{Level:u3}] {Message:lj}{NewLine}{Exception}";
    private const string FileNamePattern = "uefiaccess-.log";

    /// <summary>The TrustedInstaller service account, which owns many Windows folders.</summary>
    private const string TrustedInstallerSid = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";

    /// <summary>
    /// Root data directory of the tool: ProgramData\UefiAccess on Windows,
    /// LocalApplicationData/UefiAccess elsewhere. Logs and baselines are subfolders of it.
    /// </summary>
    public static string DataDirectory
    {
        get
        {
            string root;
            try
            {
                root = OperatingSystem.IsWindows()
                    ? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
                    : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }
            catch (Exception)
            {
                root = string.Empty;
            }

            if (string.IsNullOrEmpty(root))
            {
                root = Path.GetTempPath();
            }

            return Path.Combine(root, "UefiAccess");
        }
    }

    /// <summary>
    /// Directory that receives the rolling log files (ProgramData\UefiAccess\logs on Windows,
    /// LocalApplicationData/UefiAccess/logs elsewhere).
    /// </summary>
    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    /// <summary>
    /// Per-user log directory, LocalApplicationData\UefiAccess\logs (normally
    /// C:\Users\&lt;name&gt;\AppData\Local\UefiAccess\logs): used by a run that cannot write
    /// <see cref="LogDirectory"/>, typically the command-line tool started without elevation. Off
    /// Windows it is the same folder as <see cref="LogDirectory"/>.
    /// </summary>
    public static string UserLogDirectory
    {
        get
        {
            string root;
            try
            {
                root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }
            catch (Exception)
            {
                root = string.Empty;
            }

            if (string.IsNullOrEmpty(root))
            {
                root = Path.GetTempPath();
            }

            return Path.Combine(root, "UefiAccess", "logs");
        }
    }

    /// <summary>
    /// Creates a logger for a component. The logger writes to a daily rolling file in
    /// <see cref="LogDirectory"/> or, when that folder cannot be written or an elevated run may not
    /// trust it, in <see cref="UserLogDirectory"/> (recorded once at the top of that log), and, when
    /// <paramref name="console"/> is true, to standard error. If no file sink can be created the
    /// logger degrades to console-only or to a silent logger.
    /// </summary>
    /// <param name="component">Short component name written into every event, for example "cli" or "app".</param>
    /// <param name="console">True to mirror events to the console (standard error).</param>
    /// <returns>A configured <see cref="ILogger"/>; never null and never throws.</returns>
    public static ILogger Create(string component, bool console)
    {
        TextWriter notices;
        try
        {
            notices = Console.Error;
        }
        catch (Exception)
        {
            notices = TextWriter.Null;
        }

        return Create(component, console, new LogLocations(DataDirectory, LogDirectory, UserLogDirectory), IsElevated(), notices, IsTrustedOwner);
    }

    /// <summary>
    /// Prepares an elevated write to the folder <paramref name="path"/> when it lies inside the data
    /// root <see cref="DataDirectory"/>: checks the root and every existing folder between it and
    /// <paramref name="path"/> (a plain folder owned by SYSTEM, Administrators or TrustedInstaller),
    /// and creates each missing one, the root included, with the protected access list of
    /// <see cref="CreateDataDirectorySecurity"/>. The caller then writes into
    /// <paramref name="path"/> itself. Does nothing for a path outside the data root, in a process
    /// that is not elevated, or off Windows. Never throws.
    /// </summary>
    /// <param name="path">The folder the caller is about to create or write, for example a baseline run folder.</param>
    /// <returns>Null when the caller may go ahead; otherwise the sentences that say why it must not write there and what to do.</returns>
    public static string? PrepareDataFolderFor(string path)
    {
        return PrepareDataFolderFor(path, DataDirectory, IsElevated(), IsTrustedOwner);
    }

    /// <summary>
    /// Prepares a write of a run's own file (the input trace of UefiAccess.App, the UI Automation
    /// recorder's trace) into one of the two log folders, with the checks this class makes before
    /// it writes its log there. <see cref="LogDirectory"/>: an elevated run checks the data root and
    /// the folder and creates what is missing (<see cref="PrepareDataFolderFor(string)"/>); a run
    /// without elevation creates nothing under the data root, so the folder must exist.
    /// <see cref="UserLogDirectory"/>: an elevated run checks the local application data folder, its
    /// UefiAccess folder and the logs folder for links and creates what is missing (the R18 checks
    /// of the per-user log); a run without elevation creates it. Any other folder is refused. The
    /// caller then creates its file there without overwriting anything (FileMode.CreateNew). Never
    /// throws.
    /// </summary>
    /// <param name="directory">The log folder the caller is about to write a file into.</param>
    /// <returns>Null when the caller may go ahead; otherwise the sentence that says why it must not write there.</returns>
    public static string? PrepareRunFileFolder(string directory)
    {
        return PrepareRunFileFolder(directory, new LogLocations(DataDirectory, LogDirectory, UserLogDirectory), IsElevated(), IsTrustedOwner);
    }

    /// <summary>Testable core of <see cref="Create(string, bool)"/>.</summary>
    /// <param name="component">Short component name.</param>
    /// <param name="console">True to mirror events to standard error.</param>
    /// <param name="locations">The data root and the two log folders.</param>
    /// <param name="elevated">Whether the process is elevated.</param>
    /// <param name="notices">Receives the one-line notice when the run can keep no log file at all.</param>
    /// <param name="isTrustedOwner">Decides which folder owners an elevated run trusts; <see cref="IsTrustedOwner"/> when null.</param>
    /// <returns>The logger; never throws.</returns>
    internal static ILogger Create(string component, bool console, LogLocations locations, bool elevated, TextWriter notices, Func<SecurityIdentifier?, bool>? isTrustedOwner = null)
    {
        string name = string.IsNullOrWhiteSpace(component) ? "UefiAccess" : component.Trim();
        try
        {
            LoggerConfiguration configuration = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.WithProperty("Component", name)
                .WriteTo.Sink(CreateFileSink(locations, elevated, notices, isTrustedOwner ?? IsTrustedOwner));
            if (console)
            {
                configuration = configuration.WriteTo.Console(
                    standardErrorFromLevel: LogEventLevel.Verbose,
                    outputTemplate: ConsoleTemplate);
            }

            return configuration.CreateLogger();
        }
        catch (Exception)
        {
            return CreateFallback(name, console);
        }
    }

    /// <summary>Testable core of <see cref="PrepareDataFolderFor(string)"/>.</summary>
    /// <param name="path">The folder the caller is about to create or write.</param>
    /// <param name="dataDirectory">The data root.</param>
    /// <param name="elevated">Whether the process is elevated.</param>
    /// <param name="isTrustedOwner">Decides which folder owners are trusted.</param>
    /// <returns>Null when the caller may go ahead; otherwise the refusal.</returns>
    internal static string? PrepareDataFolderFor(string path, string dataDirectory, bool elevated, Func<SecurityIdentifier?, bool> isTrustedOwner)
    {
        if (!elevated || !OperatingSystem.IsWindows())
        {
            return null;
        }

        string root;
        string target;
        try
        {
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory));
            target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception)
        {
            // The caller's own directory creation reports a malformed path.
            return null;
        }

        bool atRoot = string.Equals(target, root, StringComparison.OrdinalIgnoreCase);
        if (!atRoot && !target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string folder = root;
        string? refusal = CheckOrCreateFolder(folder, isTrustedOwner);
        if (refusal is not null || atRoot)
        {
            return refusal;
        }

        foreach (string part in target.Substring(root.Length + 1).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            folder = Path.Combine(folder, part);
            refusal = CheckOrCreateFolder(folder, isTrustedOwner);
            if (refusal is not null)
            {
                return refusal;
            }
        }

        return null;
    }

    /// <summary>Testable core of <see cref="PrepareRunFileFolder(string)"/>.</summary>
    /// <param name="directory">The log folder the caller is about to write a file into.</param>
    /// <param name="locations">The data root and the two log folders.</param>
    /// <param name="elevated">Whether the process is elevated.</param>
    /// <param name="isTrustedOwner">Decides which folder owners an elevated run trusts.</param>
    /// <returns>Null when the caller may go ahead; otherwise the refusal.</returns>
    internal static string? PrepareRunFileFolder(string directory, LogLocations locations, bool elevated, Func<SecurityIdentifier?, bool> isTrustedOwner)
    {
        try
        {
            string target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            string shared = Path.TrimEndingDirectorySeparator(Path.GetFullPath(locations.SharedLogDirectory));
            string user = Path.TrimEndingDirectorySeparator(Path.GetFullPath(locations.UserLogDirectory));
            if (string.Equals(target, shared, StringComparison.OrdinalIgnoreCase))
            {
                if (elevated)
                {
                    return PrepareDataFolderFor(target, locations.DataDirectory, elevated: true, isTrustedOwner);
                }

                // A run without elevation never creates anything under the data root.
                return Directory.Exists(target)
                    ? null
                    : $"{target} does not exist, and a run without elevation creates nothing under {locations.DataDirectory}.";
            }

            if (string.Equals(target, user, StringComparison.OrdinalIgnoreCase))
            {
                if (elevated)
                {
                    return PrepareUserLogFolderForElevatedRun(target);
                }

                Directory.CreateDirectory(target);
                return null;
            }

            return $"{directory} is not a log folder of UefiAccess ({shared} or {user}).";
        }
        catch (Exception ex)
        {
            return $"{directory} cannot be checked: {ex.Message}";
        }
    }

    /// <summary>
    /// The folder owners an elevated run trusts not to redirect its writes: SYSTEM, Administrators
    /// and TrustedInstaller, and no one else.
    /// </summary>
    /// <remarks>
    /// Decision (22 September 2026): the default owner of this process's own token is not trusted
    /// as such. An elevated token's default owner is Administrators, which is trusted anyway. Only
    /// under the non-default policy "System objects: Default owner for objects created by members
    /// of the Administrators group" = "Object creator" is it the administrator's own account, and
    /// that account's processes without elevation have the same user SID: they own such a folder
    /// too and could turn it into a link. Trusting that owner would also accept a folder the
    /// account created without elevation, such as a data root an older build or test run left. The
    /// folders an elevated run creates are given Administrators as their owner explicitly
    /// (<see cref="CreationOwner"/>), so they pass the check under either policy.
    /// </remarks>
    /// <param name="owner">The folder's owner, or null when it is unknown.</param>
    /// <returns>True when the owner is SYSTEM, Administrators or TrustedInstaller.</returns>
    internal static bool IsTrustedOwner(SecurityIdentifier? owner)
    {
        return owner is not null
            && (owner.IsWellKnown(WellKnownSidType.LocalSystemSid)
                || owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)
                || string.Equals(owner.Value, TrustedInstallerSid, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The owner <see cref="CreateProtectedDirectory"/> gives a folder it creates: Administrators
    /// when this process's token holds the Administrators group enabled (an elevated administrator,
    /// or SYSTEM), so the folder is owned by Administrators whatever the "Default owner" policy
    /// says; otherwise null, and the folder gets the token's default owner (a token without the
    /// group enabled, such as a test host without elevation, may not assign it). Never throws.
    /// </summary>
    /// <returns>The Administrators SID, or null.</returns>
    internal static SecurityIdentifier? CreationOwner()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)
                ? new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The access list the data root and every folder an elevated run creates in it are created
    /// with: protected from inheriting ProgramData's entries (which let Users add files and
    /// folders), SYSTEM and Administrators full control, Users read and execute, each inherited by
    /// every folder and file inside.
    /// </summary>
    /// <returns>A new security descriptor for a directory.</returns>
    internal static DirectorySecurity CreateDataDirectorySecurity()
    {
        const InheritanceFlags Inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        DirectorySecurity security = new();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, Inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, Inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, Inherit, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }

    /// <summary>
    /// Creates <paramref name="path"/> with <see cref="CreateDataDirectorySecurity"/> applied at
    /// creation, so there is no moment in which the folder carries the inherited entries, and with
    /// <see cref="CreationOwner"/> as its owner when there is one. The parent must exist
    /// (ProgramData always does): .NET would otherwise create every missing parent with the same
    /// access list. An existing folder is left unchanged.
    /// </summary>
    /// <param name="path">The folder to create.</param>
    /// <exception cref="DirectoryNotFoundException">The parent folder does not exist.</exception>
    internal static void CreateProtectedDirectory(string path)
    {
        DirectoryInfo directory = new(path);
        if (directory.Parent is not { Exists: true })
        {
            throw new DirectoryNotFoundException("The parent of '" + directory.FullName + "' does not exist.");
        }

        DirectorySecurity security = CreateDataDirectorySecurity();
        SecurityIdentifier? owner = CreationOwner();
        if (owner is not null)
        {
            security.SetOwner(owner);
        }

        directory.Create(security);
    }

    /// <summary>
    /// Checks the per-user log folder before an elevated run writes its log there (the shared log
    /// was refused or could not be written). The folder belongs to the user, so a program the same
    /// account runs without elevation could make it a junction or symbolic link and so redirect
    /// the elevated run's log file into a folder only administrators may write. The folder two
    /// levels up (the local application data folder), the one above (UefiAccess) and
    /// <paramref name="userLogDirectory"/> itself must each be a plain folder, not a reparse point;
    /// a missing one is created and then checked. No existing log file in it may be a reparse
    /// point either. Owners are not checked: these folders are the user's by design. Never throws.
    /// </summary>
    /// <param name="userLogDirectory">The per-user log folder, LocalApplicationData\UefiAccess\logs.</param>
    /// <returns>Null when the elevated run may write its log there; otherwise the refusal.</returns>
    internal static string? PrepareUserLogFolderForElevatedRun(string userLogDirectory)
    {
        string logs;
        try
        {
            logs = Path.TrimEndingDirectorySeparator(Path.GetFullPath(userLogDirectory));
        }
        catch (Exception ex)
        {
            return $"{userLogDirectory} cannot be checked: {ex.Message}";
        }

        List<string> chain = new() { logs };
        for (string? parent = Path.GetDirectoryName(logs); parent is not null && chain.Count < 3; parent = Path.GetDirectoryName(parent))
        {
            chain.Insert(0, parent);
        }

        foreach (string folder in chain)
        {
            try
            {
                FileAttributes? attributes = ReadAttributes(folder);
                if (attributes is null)
                {
                    try
                    {
                        Directory.CreateDirectory(folder);
                    }
                    catch (Exception ex)
                    {
                        return $"{folder} cannot be created: {ex.Message}";
                    }

                    attributes = ReadAttributes(folder);
                    if (attributes is null)
                    {
                        return $"{folder} cannot be created.";
                    }
                }

                if ((attributes.Value & FileAttributes.ReparsePoint) != 0)
                {
                    return UserFolderRefusal(folder, $"{folder} is a junction, symbolic link or other reparse point, not a plain folder");
                }

                if ((attributes.Value & FileAttributes.Directory) == 0)
                {
                    return $"{folder} is a file, not a folder.";
                }
            }
            catch (Exception ex)
            {
                return $"{folder} cannot be checked: {ex.Message}";
            }
        }

        try
        {
            foreach (string file in Directory.EnumerateFiles(logs, "uefiaccess-*.log"))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                {
                    return UserFolderRefusal(file, $"{file} is a symbolic link or other reparse point, not a plain file");
                }
            }
        }
        catch (Exception ex)
        {
            return $"{logs} cannot be checked: {ex.Message}";
        }

        return null;
    }

    /// <summary>
    /// Checks one folder on the way to an elevated write, or creates it with the protected access
    /// list when it is missing (its parent was checked or created just before). A folder created
    /// here is checked as well, because one that appeared in the meantime was not created by this
    /// run. Never throws.
    /// </summary>
    /// <param name="folder">The full path of the folder.</param>
    /// <param name="isTrustedOwner">Decides which owners are trusted.</param>
    /// <returns>Null when the folder may be written through; otherwise the refusal.</returns>
    private static string? CheckOrCreateFolder(string folder, Func<SecurityIdentifier?, bool> isTrustedOwner)
    {
        try
        {
            FileAttributes? attributes = ReadAttributes(folder);
            if (attributes is null)
            {
                try
                {
                    CreateProtectedDirectory(folder);
                }
                catch (Exception ex)
                {
                    return $"{folder} cannot be created: {ex.Message}";
                }

                attributes = ReadAttributes(folder);
                if (attributes is null)
                {
                    return $"{folder} cannot be created.";
                }
            }

            if ((attributes.Value & FileAttributes.ReparsePoint) != 0)
            {
                return Refusal(folder, $"{folder} is a junction, symbolic link or other reparse point, not a plain folder");
            }

            if ((attributes.Value & FileAttributes.Directory) == 0)
            {
                return $"{folder} is a file, not a folder.";
            }

            SecurityIdentifier? owner;
            try
            {
                owner = new DirectoryInfo(folder).GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            }
            catch (Exception ex)
            {
                return Refusal(folder, $"the owner of {folder} cannot be read ({ex.Message.TrimEnd('.')})");
            }

            return isTrustedOwner(owner) ? null : Refusal(folder, $"{folder} is owned by {AccountName(owner)}, not by Administrators or SYSTEM");
        }
        catch (Exception ex)
        {
            return $"{folder} cannot be checked: {ex.Message}";
        }
    }

    /// <summary>The attributes of a file-system entry without following it, or null when nothing is there.</summary>
    private static FileAttributes? ReadAttributes(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    /// <summary>The refusal for a folder standard users could redirect, with what to do about it.</summary>
    private static string Refusal(string folder, string reason)
    {
        return $"{reason}, so a standard user could redirect what this elevated run writes there. Rename or delete {folder} from an administrator prompt; the next elevated run creates it again with safe permissions.";
    }

    /// <summary>The refusal for a per-user folder or log file a program without elevation could redirect, with what to do about it.</summary>
    private static string UserFolderRefusal(string path, string reason)
    {
        return $"{reason}, so a program running without elevation could redirect what this elevated run writes there. Rename or delete {path}; the next run creates it again.";
    }

    /// <summary>The account name of a security identifier, or the identifier itself when it cannot be translated.</summary>
    private static string AccountName(SecurityIdentifier? sid)
    {
        if (sid is null)
        {
            return "an unknown account";
        }

        try
        {
            return sid.Translate(typeof(NTAccount)).Value;
        }
        catch (Exception)
        {
            return sid.Value;
        }
    }

    private static bool IsElevated()
    {
        try
        {
            return PrivilegeManager.ComputeIsElevated();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static FallbackLogSink CreateFileSink(LogLocations locations, bool elevated, TextWriter notices, Func<SecurityIdentifier?, bool> isTrustedOwner)
    {
        string shared = locations.SharedLogDirectory;
        string user = locations.UserLogDirectory;
        bool separate = !string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(shared)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(user)),
            StringComparison.OrdinalIgnoreCase);

        ILogEventSink? primary = null;
        string? refusal = null;
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(shared);
                primary = CreateRollingFile(shared);
            }
            else if (elevated)
            {
                // An elevated run writes only through folders standard users cannot redirect, and
                // creates the missing ones (the data root and logs) with the protected access list.
                refusal = PrepareDataFolderFor(shared, locations.DataDirectory, elevated: true, isTrustedOwner);
                if (refusal is null)
                {
                    primary = CreateRollingFile(shared);
                }
            }
            else if (Directory.Exists(shared))
            {
                // A standard user only appends to a shared log folder that already exists.
                primary = CreateRollingFile(shared);
            }
        }
        catch (Exception)
        {
            primary = null;
        }

        Func<ILogEventSink>? createFallback = null;
        if (separate)
        {
            // An elevated run checks the user's folder for links first (the class remarks say why).
            createFallback = elevated && OperatingSystem.IsWindows()
                ? () => CreateRollingFile(PrepareUserLogFolderOrThrow(user))
                : () => CreateRollingFile(Directory.CreateDirectory(user).FullName);
        }

        return new FallbackLogSink(
            primary,
            createFallback,
            notices,
            shared,
            separate ? user : null,
            refusal);
    }

    /// <summary>The per-user log folder for an elevated run, after <see cref="PrepareUserLogFolderForElevatedRun"/> accepted it.</summary>
    /// <exception cref="LogFolderRefusedException">The folder was refused; the message is the refusal.</exception>
    private static string PrepareUserLogFolderOrThrow(string userLogDirectory)
    {
        string? refusal = PrepareUserLogFolderForElevatedRun(userLogDirectory);
        if (refusal is not null)
        {
            throw new LogFolderRefusedException(refusal);
        }

        return Path.GetFullPath(userLogDirectory);
    }

    private static ILogEventSink CreateRollingFile(string directory)
    {
        return LoggerSinkConfiguration.CreateSink(sink => sink.File(
            Path.Combine(directory, FileNamePattern),
            rollingInterval: RollingInterval.Day,
            shared: true,
            outputTemplate: FileTemplate));
    }

    private static ILogger CreateFallback(string component, bool console)
    {
        if (!console)
        {
            return Serilog.Core.Logger.None;
        }

        try
        {
            return new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.WithProperty("Component", component)
                .WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose, outputTemplate: ConsoleTemplate)
                .CreateLogger();
        }
        catch (Exception)
        {
            return Serilog.Core.Logger.None;
        }
    }
}
