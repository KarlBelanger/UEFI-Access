// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics.CodeAnalysis;
using UefiAccess.Cli.Output;
using UefiAccess.Cli.Services;
using UefiAccess.Core.Safety;
using UefiAccess.Core.Variables;

namespace UefiAccess.Cli.Commands;

/// <summary>One entry of a boot list in the write document, decoded.</summary>
/// <param name="Position">The 1-based position in the list.</param>
/// <param name="Name">The Boot#### name.</param>
/// <param name="Description">The load option's description; empty for an entry the plan does not describe.</param>
/// <param name="Active">LOAD_OPTION_ACTIVE.</param>
/// <param name="Hidden">LOAD_OPTION_HIDDEN.</param>
/// <param name="Category">"Boot", "App" or the hexadecimal category; empty for a missing entry.</param>
internal sealed record WriteEntryDocument(int Position, string Name, string Description, bool Active, bool Hidden, string Category);

/// <summary>One value of the target in the write document: before the change, proposed, or read back.</summary>
/// <param name="Present">False when the variable does not exist (a BootNext not set, or the value of a clear).</param>
/// <param name="Attributes">The attributes read, or the attributes the change passes for a value not yet on the firmware.</param>
/// <param name="Flags">The short flags of <paramref name="Attributes"/>, such as "NV BS RT".</param>
/// <param name="Size">The size in bytes.</param>
/// <param name="Sha256">The lowercase SHA-256 of the bytes, the value index.json carries.</param>
/// <param name="Hex">The bytes as lowercase hexadecimal without separators.</param>
/// <param name="Entries">The entries the bytes encode, in order.</param>
internal sealed record WriteImageDocument(bool Present, uint Attributes, string Flags, int Size, string Sha256, string Hex, IReadOnlyList<WriteEntryDocument> Entries);

/// <summary>The Writability verdict of "write check".</summary>
/// <param name="Verdict">The verdict.</param>
/// <param name="Sentence">The Writability line.</param>
/// <param name="Changeable">True for the two verdicts under which this tool can change the variable.</param>
internal sealed record WritabilityDocument(Writability Verdict, string Sentence, bool Changeable);

/// <summary>The BitLocker facts of a write command. Never key material.</summary>
/// <param name="State">The state: the one the dry run assessed, or the one recorded after a suspend that failed or did not answer.</param>
/// <param name="Sentence">The BitLocker state sentence.</param>
/// <param name="Risk">The risk sentence of a reorder or a one-time boot while protectors may exist, or null.</param>
/// <param name="RecoveryKey">The recovery-key reminder while protectors may exist, or null.</param>
/// <param name="SuspendOffered">True when the change needs a choice: --bitlocker suspend or --bitlocker continue.</param>
/// <param name="Choice">The choice given with --bitlocker, or null when the option was absent.</param>
/// <param name="SuspendedForOneRestart">True when the write suspended BitLocker for one restart (it stays suspended whatever happened after).</param>
internal sealed record WriteBitLockerDocument(
    BitLockerState State,
    string Sentence,
    string? Risk,
    string? RecoveryKey,
    bool SuspendOffered,
    BitLockerChoice? Choice,
    bool SuspendedForOneRestart);

/// <summary>The JSON document of the write commands bootorder, bootnext, restore and check.</summary>
/// <param name="Command">"write".</param>
/// <param name="Operation">"Reorder", "SetBootNext", "ClearBootNext", "Check", or "Restore" for a restore refused before it was resolved.</param>
/// <param name="TransactionId">The transaction id: the snapshot folder name, or the name it would have had.</param>
/// <param name="Ok">True when the exit code is 0.</param>
/// <param name="ExitCode">The exit code: 0, 3, 4, 7, 8 or 9.</param>
/// <param name="DryRun">True without --confirm (and for check): nothing could be written.</param>
/// <param name="Confirmed">True when the confirmation passed (stage S11).</param>
/// <param name="Variable">The target variable, or null when the attempt was refused before it was known.</param>
/// <param name="Requested">What was asked: the order ("Boot0003,Boot0002"), the target, "clear", the snapshot folder, or the checked name.</param>
/// <param name="Before">The target before the change, from the plan; null when no plan was made.</param>
/// <param name="Proposed">The value the change writes, from the plan; null when no plan was made.</param>
/// <param name="After">The value read back after the write call; null when no call was made or nothing was read.</param>
/// <param name="Diff">The before/after diff by entry name, one row per position; empty when no plan was made.</param>
/// <param name="Checks">Every check that ran, in stage order, the failed one last.</param>
/// <param name="BlockedBy">The name of the check that refused the change, or null.</param>
/// <param name="Warnings">The warning sentences of the dry run (also on standard error).</param>
/// <param name="Writability">The Writability verdict of check, or null for the other commands and an absent variable.</param>
/// <param name="BitLocker">The BitLocker facts, or null when they were not read.</param>
/// <param name="SecureBoot">The Secure Boot facts, or null when they were not read.</param>
/// <param name="SnapshotFolder">The snapshot folder of the write, or null when none was created.</param>
/// <param name="ApiCalled">True when the one write call was made.</param>
/// <param name="Api">What the write call returned, or null when it was not made.</param>
/// <param name="Verification">What the verification read found, or null when it was not made.</param>
/// <param name="Sentence">The sentence the command printed.</param>
/// <param name="Record">The write record, as appended to write-log.jsonl.</param>
internal sealed record WriteDocument(
    string Command,
    string Operation,
    string TransactionId,
    bool Ok,
    int ExitCode,
    bool DryRun,
    bool Confirmed,
    WriteRecordVariable? Variable,
    string Requested,
    WriteImageDocument? Before,
    WriteImageDocument? Proposed,
    WriteImageDocument? After,
    IReadOnlyList<DiffRow> Diff,
    IReadOnlyList<CheckResult> Checks,
    string? BlockedBy,
    IReadOnlyList<string> Warnings,
    WritabilityDocument? Writability,
    WriteBitLockerDocument? BitLocker,
    WriteRecordSecureBoot? SecureBoot,
    string? SnapshotFolder,
    bool ApiCalled,
    WriteRecordApi? Api,
    WriteRecordVerification? Verification,
    string Sentence,
    WriteRecord Record);

/// <summary>The JSON document of "write log".</summary>
/// <param name="Command">"write log".</param>
/// <param name="Ok">Always true: a missing list shows no record, and a list that exists but cannot be read is a failure with the error document instead (exit 5).</param>
/// <param name="File">The full path of write-log.jsonl.</param>
/// <param name="Total">The number of records in the file.</param>
/// <param name="NotRecords">The number of lines of the file that are not whole records (a crash while a line was written); they are not shown, and the write budget counts each one of this start as a possible change.</param>
/// <param name="Records">The last records asked for with --last, oldest first.</param>
internal sealed record WriteLogDocument(string Command, bool Ok, string File, int Total, int NotRecords, IReadOnlyList<WriteRecord> Records);

/// <summary>
/// "uefiaccess write": the command-line harness of the write pipeline.
/// bootorder, bootnext and restore build a typed request from Boot#### names or a snapshot folder
/// and run the dry run (stages S1 to S10) with the --bitlocker choice; without --confirm nothing
/// more happens, and with --confirm a plan that passed is confirmed with exactly that choice and
/// committed once, with Ctrl+C ignored while the commit runs. check runs the pre-flight of one
/// variable; log prints the last records of write-log.jsonl. Every refusal sentence, the
/// BitLocker sentences and the exit codes come from Core, so the command, the record and the
/// application say the same thing. No option skips a safety check.
/// </summary>
internal static class WriteCommand
{
    /// <summary>The command name, and the command of the write document.</summary>
    internal const string CommandName = "write";

    /// <summary>The command of the write log document.</summary>
    internal const string LogCommandName = "write log";

    /// <summary>The value of --bitlocker that suspends BitLocker for one restart.</summary>
    internal const string SuspendValue = "suspend";

    /// <summary>The value of --bitlocker that writes with protection on.</summary>
    internal const string ContinueValue = "continue";

    /// <summary>What a clear asks for, in the write document.</summary>
    internal const string ClearRequested = "clear";

    /// <summary>The number of records write log prints without --last.</summary>
    internal const int DefaultLastRecords = 20;

    /// <summary>Standard error after every run without --confirm.</summary>
    internal const string DryRunSentence = "Dry run: nothing was written.";

    /// <summary>Standard error when BitLocker needs a choice and --bitlocker was not given.</summary>
    internal const string BitLockerHint = "Add --bitlocker suspend to suspend it for one restart, or --bitlocker continue to write with protection on.";

    /// <summary>The first of the three BitLocker lines on standard error; {0} the state sentence.</summary>
    internal const string BitLockerStateLineFormat = "BitLocker state: {0}";

    /// <summary>The second of the three BitLocker lines; {0} the risk sentence.</summary>
    internal const string RiskLineFormat = "Risk: {0}";

    /// <summary>The third of the three BitLocker lines; {0} the recovery-key reminder.</summary>
    internal const string RecoveryKeyLineFormat = "Recovery key: {0}";

    /// <summary>Standard error beside a removable-entry warning (rule V17); {0} the entry, {1} its device path node types.</summary>
    internal const string DeviceNodeTypesFormat = "Device path of {0}: {1}.";

    /// <summary>Standard error just before the commit starts.</summary>
    internal const string CommitStarting = "Writing the change, please wait. Do not restart the machine; Ctrl+C is ignored until the result is in.";

    /// <summary>Standard error when Ctrl+C is pressed while the commit runs.</summary>
    internal const string CtrlCIgnored = "Ctrl+C is ignored while the change is written; wait for its result.";

    /// <summary>The usage error for --dry-run together with --confirm.</summary>
    internal const string DryRunAndConfirmTogether = "--dry-run and --confirm cannot be given together: without --confirm every write command is a dry run.";

    /// <summary>
    /// The usage error for --snapshot-root together with --confirm: the option exists so that a
    /// refused root can be shown in a dry run (failure case FC-20), and a confirmed write keeps its
    /// snapshot where "snapshots" and Restore previous value look for it.
    /// </summary>
    internal const string SnapshotRootWithConfirm = "--snapshot-root cannot be given with --confirm: a confirmed change keeps its snapshot under the default snapshot root, where snapshots and restore find it; use --snapshot-root in a dry run only.";

    /// <summary>The usage error for a name that is not a Boot#### name; {0} the name.</summary>
    internal const string NotABootEntryFormat = "'{0}' is not a Boot#### name: Boot followed by four hexadecimal digits in capitals, for example Boot0002.";

    /// <summary>The usage error for an --order without a name.</summary>
    internal const string NoOrderGiven = "--order needs at least one Boot#### name, for example --order Boot0002,Boot0003.";

    /// <summary>The usage error of bootnext without an entry and without --clear, or with both.</summary>
    internal const string EntryOrClearRequired = "Name one Boot#### entry or give --clear, not both.";

    /// <summary>The usage error for a --guid that is not a GUID; {0} the value.</summary>
    internal const string NotAGuidFormat = "The value '{0}' given to --guid is not a GUID.";

    /// <summary>The usage error for a --snapshot-root that is not a folder path; {0} the value, {1} the reason.</summary>
    internal const string BadSnapshotRootFormat = "The value '{0}' given to --snapshot-root is not a valid folder path: {1}";

    /// <summary>The usage error for a --last below 1.</summary>
    internal const string LastMustBePositive = "--last needs a number of records of 1 or more.";

    /// <summary>write log's line when the list holds no record; {0} the file.</summary>
    internal const string NoRecordsFormat = "No write attempts are recorded in {0}.";

    /// <summary>write log's last line; {0} the records shown, {1} the records in the file, {2} the file.</summary>
    internal const string RecordsShownFormat = "{0} of {1} records in {2}.";

    /// <summary>write log's failure when write-log.jsonl exists but cannot be read; {0} the file, {1} the reason from Core.</summary>
    internal const string LogUnreadableFormat = "The write log {0} could not be shown: {1}";

    /// <summary>write log's line after the records when the file holds lines that are not whole records; {0} how many.</summary>
    internal const string NotRecordsFormat = "{0} lines of the file are not whole records (a crash while a line was written) and are not shown; the write budget counts each one of this start as a possible change.";

    /// <summary>The reason of <see cref="LogUnreadableFormat"/> when the file could be read once but not a second time.</summary>
    internal const string LogSecondReadFailed = "the file could be read once but not a second time";

    private const string BootOrderCommandName = "bootorder";
    private const string BootNextCommandName = "bootnext";
    private const string RestoreCommandName = "restore";
    private const string CheckCommandName = "check";
    private const string LogCommand = "log";

    private const string OrderOption = "--order";
    private const string AllowNoChangeOption = "--allow-no-change";
    private const string DryRunOption = "--dry-run";
    private const string ConfirmOption = "--confirm";
    private const string BitLockerOption = "--bitlocker";
    private const string SnapshotRootOption = "--snapshot-root";
    private const string ClearOption = "--clear";
    private const string LastOption = "--last";
    private const string EntryArgument = "entry";
    private const string FolderArgument = "folder";
    private const string NameArgument = "name";
    private const string OrderHelpName = "Boot####,Boot####";
    private const string DirectoryHelpName = "dir";
    private const string CountHelpName = "n";

    private const string WriteDescription = "Change BootOrder (reorder) or BootNext through the write pipeline: every subcommand is a dry run unless --confirm is given, and --confirm makes exactly one write call after a snapshot, followed by a verification read.";
    private const string BootOrderDescription = "Put BootOrder's current entries in a new order (no entry added, removed or repeated).";
    private const string BootNextDescription = "Boot once from an existing entry at the next start (BootNext), or remove that choice with --clear.";
    private const string RestoreDescription = "Put back the value a snapshot folder recorded, through the same checks as any change.";
    private const string CheckDescription = "Run the pre-flight checks for one variable and print whether this tool can change it; never writes.";
    private const string LogDescription = "Print the last records of write-log.jsonl, one per write attempt, blocked ones included.";
    private const string OrderDescription = "The new order as Boot#### names separated by commas, for example Boot0003,Boot0002; it must hold every current entry once.";
    private const string AllowNoChangeDescription = "Accept an order equal to the current one (the first-write drill); turns off the unchanged-order refusal and nothing else.";
    private const string DryRunDescription = "Run the checks and show the plan without writing (the default; cannot be combined with --confirm).";
    private const string ConfirmDescription = "Write the change after every check passed: exactly one write call, after the snapshot, then one verification read.";
    private const string BitLockerDescription = "The BitLocker choice when protection is on or unknown: suspend (for one restart) or continue (write with protection on). Accepted in a dry run.";
    private const string SnapshotRootDescription = "The snapshot root instead of the default one, in a dry run only (not with --confirm); it must lie under the data folder and passes the same folder check.";
    private const string EntryDescription = "The Boot#### entry to start once, for example Boot0002.";
    private const string ClearDescription = "Remove the one-time boot choice (BootNext) instead of setting it.";
    private const string FolderDescription = "The snapshot folder to restore, as a full path or as its name under the snapshot root.";
    private const string CheckNameDescription = "Variable name, case-sensitive, for example BootOrder, BootNext, Timeout or PK.";
    private const string CheckGuidDescription = "Vendor GUID. Default: the EFI global variable GUID, or the image security database GUID for db, dbx, dbt and dbr.";
    private const string LastDescription = "How many of the newest records to print; default 20.";

    private const string BootCurrentName = "BootCurrent";
    private const char OrderSeparator = ',';

    private const string FieldHeader = "Field";
    private const string ValueHeader = "Value";
    private const string OperationLabel = "Operation";
    private const string VariableLabel = "Variable";
    private const string RequestedLabel = "Requested";
    private const string TransactionLabel = "Transaction";
    private const string DryRunLabel = "Dry run";
    private const string ConfirmedLabel = "Confirmed";
    private const string OutcomeLabel = "Outcome";
    private const string BlockedByLabel = "Blocked by";
    private const string ExitCodeLabel = "Exit code";
    private const string VerdictLabel = "Verdict";
    private const string WritabilityLabel = "Writability";
    private const string BitLockerLabel = "BitLocker";
    private const string SnapshotFolderLabel = "Snapshot folder";
    private const string ApiCalledLabel = "Write call made";
    private const string ApiResultLabel = "Write call result";
    private const string NtStatusLabel = "NTSTATUS";
    private const string VerificationLabel = "Verification read";
    private const string VariableFormat = "{0} ({1})";
    private const string ApiSucceededText = "succeeded";
    private const string ApiFailedFormat = "failed with error {0} {1}: {2}";
    private const string NtStatusFormat = "0x{0:X8} {1} ({2})";
    private const string VerificationFormat = "{0}; holds the new value: {1}; holds the old value: {2}";
    private const string BeforeTitle = "Before:";
    private const string AfterTitle = "After:";
    private const string ReadBackTitle = "Read back:";
    private const string ChangesTitle = "Changes:";
    private const string ChecksTitle = "Checks:";
    private const string NotSetText = "(not set)";
    private const string PositionHeader = "Position";
    private const string BeforeHeader = "Before";
    private const string AfterHeader = "After";
    private const string EntryHeader = "Entry";
    private const string DescriptionHeader = "Description";
    private const string FlagsHeader = "Flags";
    private const string CheckHeader = "Check";
    private const string ResultHeader = "Result";
    private const string ReasonHeader = "Reason";
    private const string PassedText = "passed";
    private const string WarningText = "warning";
    private const string FailedText = "failed";
    private const string TimeHeader = "Time (UTC)";
    private const string ExitHeader = "Exit";
    private const string SentenceHeader = "Sentence";
    private const string ListSeparator = ", ";

    private static int _cancelKeyHandlers;

    /// <summary>
    /// How many Ctrl+C handlers the write commands have installed and not yet removed: 1 while a
    /// commit runs, 0 at every other moment. The tests read it from inside a fake writer and after
    /// every command line. It counts beside the subscription and does not prove it: the tests pin
    /// the subscription itself in source and raise <see cref="IgnoreCancelKey"/> directly.
    /// </summary>
    internal static int CancelKeyHandlersInstalled => Volatile.Read(ref _cancelKeyHandlers);

    /// <summary>Builds the command over the production services.</summary>
    /// <param name="verbose">The global --verbose option.</param>
    /// <returns>The command.</returns>
    public static Command Create(Option<bool> verbose)
    {
        return Create(verbose, CliServices.Create);
    }

    /// <summary>Builds the command over the services from <paramref name="servicesFactory"/>.</summary>
    /// <param name="verbose">The global --verbose option.</param>
    /// <param name="servicesFactory">Creates the services for an invocation; receives the --verbose flag.</param>
    /// <returns>The command.</returns>
    public static Command Create(Option<bool> verbose, Func<bool, CliServices> servicesFactory)
    {
        Command write = new(CommandName, WriteDescription);
        write.Add(CreateBootOrder(verbose, servicesFactory));
        write.Add(CreateBootNext(verbose, servicesFactory));
        write.Add(CreateRestore(verbose, servicesFactory));
        write.Add(CreateCheck(verbose, servicesFactory));
        write.Add(CreateLog(verbose, servicesFactory));
        return write;
    }

    /// <summary>
    /// Reads a list of Boot#### names separated by commas, as --order takes it: each name is
    /// "Boot" and four hexadecimal digits in capitals (<see cref="KnownVariableNames.IsBootEntryName"/>),
    /// spaces around a name are ignored, and at least one name is needed. Duplicates are kept: the
    /// pipeline refuses them with its own sentence.
    /// </summary>
    /// <param name="text">The option's value.</param>
    /// <param name="entries">The entry numbers in order.</param>
    /// <param name="problem">The usage error, or null.</param>
    /// <returns>True when every name is a Boot#### name.</returns>
    internal static bool TryParseEntries(string? text, out ushort[] entries, [NotNullWhen(false)] out string? problem)
    {
        entries = Array.Empty<ushort>();
        if (string.IsNullOrWhiteSpace(text))
        {
            problem = NoOrderGiven;
            return false;
        }

        List<ushort> parsed = new();
        foreach (string part in text.Split(OrderSeparator))
        {
            if (!TryParseEntry(part, out ushort entry, out problem))
            {
                return false;
            }

            parsed.Add(entry);
        }

        entries = parsed.ToArray();
        problem = null;
        return true;
    }

    /// <summary>Reads one Boot#### name, spaces around it ignored.</summary>
    /// <param name="text">The name.</param>
    /// <param name="entry">The entry number.</param>
    /// <param name="problem">The usage error, or null.</param>
    /// <returns>True for a Boot#### name.</returns>
    internal static bool TryParseEntry(string? text, out ushort entry, [NotNullWhen(false)] out string? problem)
    {
        string name = (text ?? string.Empty).Trim();
        if (KnownVariableNames.IsBootEntryName(name, out int index))
        {
            entry = (ushort)index;
            problem = null;
            return true;
        }

        entry = 0;
        problem = CheckSentences.Format(NotABootEntryFormat, name);
        return false;
    }

    /// <summary>
    /// The command the documents of a parsed write command carry, for the error document of a
    /// command line that does not parse, so a parse error and a failed precondition name the
    /// command the same way: "write" for write itself and for bootorder, bootnext, restore and
    /// check, "write log" for log. Null for a command outside write.
    /// </summary>
    /// <param name="result">The command the parser stopped in.</param>
    /// <returns>The command name of the documents, or null.</returns>
    internal static string? DocumentCommandOf(CommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (IsWriteCommand(result))
        {
            return CommandName;
        }

        if (result.Parent is CommandResult parent && IsWriteCommand(parent))
        {
            return string.Equals(result.Command.Name, LogCommand, StringComparison.Ordinal) ? LogCommandName : CommandName;
        }

        return null;
    }

    /// <summary>True for the write command directly under the root command.</summary>
    private static bool IsWriteCommand(CommandResult result)
    {
        return string.Equals(result.Command.Name, CommandName, StringComparison.Ordinal)
            && result.Parent is CommandResult { Command: RootCommand };
    }

    // ---- the subcommands ------------------------------------------------------------------

    /// <summary>"write bootorder --order Boot####,Boot####": a reorder (W1).</summary>
    private static Command CreateBootOrder(Option<bool> verbose, Func<bool, CliServices> servicesFactory)
    {
        Option<string?> order = new(OrderOption)
        {
            Description = OrderDescription,
            HelpName = OrderHelpName,
            Required = true,
        };
        order.Validators.Add(result =>
        {
            if (!TryParseEntries(ValueOf(result), out _, out string? problem))
            {
                result.AddError(problem);
            }
        });
        Option<bool> allowNoChange = new(AllowNoChangeOption)
        {
            Description = AllowNoChangeDescription,
        };
        Command command = new(BootOrderCommandName, BootOrderDescription);
        command.Add(order);
        command.Add(allowNoChange);
        ChangeOptions options = ChangeOptions.AddTo(command);
        command.SetAction(parseResult => CommandRunner.Run(
            parseResult,
            verbose,
            options.Json,
            CommandName,
            servicesFactory,
            context => ExecuteBootOrder(
                context,
                CommandRunner.GetString(parseResult, order),
                CommandRunner.GetFlag(parseResult, allowNoChange),
                options.Read(parseResult))));
        return command;
    }

    /// <summary>"write bootnext &lt;Boot####&gt;" (W2) or "write bootnext --clear" (W3): exactly one of the two.</summary>
    private static Command CreateBootNext(Option<bool> verbose, Func<bool, CliServices> servicesFactory)
    {
        Argument<string?> entry = new(EntryArgument)
        {
            Description = EntryDescription,
            Arity = ArgumentArity.ZeroOrOne,
        };
        entry.Validators.Add(result =>
        {
            string? text = ValueOf(result);
            if (!string.IsNullOrEmpty(text) && !TryParseEntry(text, out _, out string? problem))
            {
                result.AddError(problem);
            }
        });
        Option<bool> clear = new(ClearOption)
        {
            Description = ClearDescription,
        };
        Command command = new(BootNextCommandName, BootNextDescription);
        command.Add(entry);
        command.Add(clear);
        ChangeOptions options = ChangeOptions.AddTo(command);
        command.Validators.Add(result =>
        {
            bool named = !string.IsNullOrEmpty(ValueOf(result, entry));
            if (named == IsGiven(result, clear))
            {
                result.AddError(EntryOrClearRequired);
            }
        });
        command.SetAction(parseResult => CommandRunner.Run(
            parseResult,
            verbose,
            options.Json,
            CommandName,
            servicesFactory,
            context => ExecuteBootNext(
                context,
                ValueOf(parseResult, entry),
                CommandRunner.GetFlag(parseResult, clear),
                options.Read(parseResult))));
        return command;
    }

    /// <summary>"write restore &lt;folder&gt;": the value a snapshot recorded, resolved to W1, W2 or W3 by the pipeline.</summary>
    private static Command CreateRestore(Option<bool> verbose, Func<bool, CliServices> servicesFactory)
    {
        Argument<string> folder = new(FolderArgument)
        {
            Description = FolderDescription,
        };
        Command command = new(RestoreCommandName, RestoreDescription);
        command.Add(folder);
        ChangeOptions options = ChangeOptions.AddTo(command);
        command.SetAction(parseResult => CommandRunner.Run(
            parseResult,
            verbose,
            options.Json,
            CommandName,
            servicesFactory,
            context => ExecuteRestore(context, parseResult.GetValue(folder) ?? string.Empty, options.Read(parseResult))));
        return command;
    }

    /// <summary>"write check &lt;name&gt; [--guid G]": the pre-flight of one variable; never a plan.</summary>
    private static Command CreateCheck(Option<bool> verbose, Func<bool, CliServices> servicesFactory)
    {
        Option<bool> json = CommonOptions.Json();
        Argument<string> name = new(NameArgument)
        {
            Description = CheckNameDescription,
        };
        Option<string?> guid = CommonOptions.Guid(CheckGuidDescription);
        guid.Validators.Add(result =>
        {
            string? text = ValueOf(result);
            if (text is not null && !Format.TryParseGuid(text, out _))
            {
                result.AddError(CheckSentences.Format(NotAGuidFormat, text));
            }
        });
        Command command = new(CheckCommandName, CheckDescription);
        command.Add(name);
        command.Add(json);
        command.Add(guid);
        command.SetAction(parseResult => CommandRunner.Run(
            parseResult,
            verbose,
            json,
            CommandName,
            servicesFactory,
            context => ExecuteCheck(context, parseResult.GetValue(name) ?? string.Empty, CommandRunner.GetString(parseResult, guid))));
        return command;
    }

    /// <summary>"write log [--last N]": the newest records of write-log.jsonl.</summary>
    private static Command CreateLog(Option<bool> verbose, Func<bool, CliServices> servicesFactory)
    {
        Option<bool> json = CommonOptions.Json();
        Option<int> last = new(LastOption)
        {
            Description = LastDescription,
            HelpName = CountHelpName,
            DefaultValueFactory = _ => DefaultLastRecords,
        };
        last.Validators.Add(result =>
        {
            if (CountOf(result) < 1)
            {
                result.AddError(LastMustBePositive);
            }
        });
        Command command = new(LogCommand, LogDescription);
        command.Add(json);
        command.Add(last);
        command.SetAction(parseResult => CommandRunner.Run(
            parseResult,
            verbose,
            json,
            LogCommandName,
            servicesFactory,
            context => ExecuteLog(context, CountOf(parseResult, last))));
        return command;
    }

    // ---- the bodies -----------------------------------------------------------------------

    /// <summary>Builds the reorder request from the --order names and runs it.</summary>
    private static int ExecuteBootOrder(CommandContext context, string? orderText, bool allowNoChange, ChangeSettings settings)
    {
        if (!TryParseEntries(orderText, out ushort[] entries, out string? problem))
        {
            return context.Fail(ExitCodes.Usage, problem);
        }

        ReorderRequest request = new(ChangeRequest.CliHost, entries, allowNoChange);
        string requested = string.Join(OrderSeparator, entries.Select(entry => KnownVariableNames.BootName(entry)));
        return RunChange(context, request, requested, settings);
    }

    /// <summary>Builds the one-time boot request, or the clear with --clear, and runs it.</summary>
    private static int ExecuteBootNext(CommandContext context, string? entryText, bool clear, ChangeSettings settings)
    {
        if (clear)
        {
            return RunChange(context, new ClearBootNextRequest(ChangeRequest.CliHost), ClearRequested, settings);
        }

        if (!TryParseEntry(entryText, out ushort target, out string? problem))
        {
            return context.Fail(ExitCodes.Usage, problem);
        }

        return RunChange(context, new BootNextRequest(ChangeRequest.CliHost, target), KnownVariableNames.BootName(target), settings);
    }

    /// <summary>Builds the restore request from the folder as given and runs it.</summary>
    private static int ExecuteRestore(CommandContext context, string folder, ChangeSettings settings)
    {
        return RunChange(context, new RestoreRequest(ChangeRequest.CliHost, folder), folder, settings);
    }

    /// <summary>
    /// The dry run of a change, then, with --confirm and a plan that passed, the one commit. The
    /// BitLocker choice is an input of the dry run: without --bitlocker the pipeline gets
    /// <see cref="BitLockerChoice.NotNeeded"/>, so stage S8 refuses a state that needs a choice,
    /// in a dry run as with --confirm; the confirmation carries the same choice.
    /// </summary>
    private static int RunChange(CommandContext context, ChangeRequest request, string requested, ChangeSettings settings)
    {
        // The parser already refuses this pair; a commit never runs over another snapshot root.
        if (settings.Confirm && settings.SnapshotRoot is not null)
        {
            return context.Fail(ExitCodes.Usage, SnapshotRootWithConfirm);
        }

        if (!TryResolveSnapshotRoot(settings.SnapshotRoot, out string? root, out string? rootProblem))
        {
            return context.Fail(ExitCodes.Usage, rootProblem);
        }

        SnapshotStore snapshots = context.Services.CreateSnapshotStore(root);
        IWritePipeline pipeline = context.Services.CreateWritePipeline(snapshots, context.Services.CreateRecordLog());
        BitLockerChoice choice = settings.BitLocker ?? BitLockerChoice.NotNeeded;
        PrepareResult prepared = pipeline.PrepareAsync(request, choice, CancellationToken.None).GetAwaiter().GetResult();
        string? risk = RiskSentence(prepared, context.Services);
        ReportChecks(context, prepared, risk, settings);

        TransactionResult? committed = null;
        if (settings.Confirm && prepared.Plan is ChangePlan plan)
        {
            context.Diagnostic(CommitStarting);
            committed = CommitIgnoringCtrlC(pipeline, plan, plan.Confirm(choice));
        }

        WriteDocument document = ChangeDocument(prepared, committed, snapshots, requested, risk, settings);
        return Finish(context, document, dryRunLine: !settings.Confirm);
    }

    /// <summary>
    /// Runs the check request (stages S1 to S9 without a plan) and prints the Writability verdict;
    /// the default roots are prepared at S9 as in any dry run.
    /// </summary>
    private static int ExecuteCheck(CommandContext context, string name, string? guidText)
    {
        Guid guid;
        if (guidText is null)
        {
            guid = KnownGuids.DefaultGuidFor(name);
        }
        else if (!Format.TryParseGuid(guidText, out guid))
        {
            return context.Fail(ExitCodes.Usage, CheckSentences.Format(NotAGuidFormat, guidText));
        }

        SnapshotStore snapshots = context.Services.CreateSnapshotStore(null);
        IWritePipeline pipeline = context.Services.CreateWritePipeline(snapshots, context.Services.CreateRecordLog());
        PrepareResult prepared = pipeline.PrepareAsync(new CheckRequest(ChangeRequest.CliHost, name, guid), CancellationToken.None).GetAwaiter().GetResult();
        foreach (string warning in prepared.Warnings)
        {
            context.Warn(warning);
        }

        // Exit 0 only when this tool can change the variable: a check on the wrong machine, or of
        // a variable that is read-only, exits 7 with the Writability line. An absent variable (a
        // BootNext not set) can be created only on the M3 test unit. Every exit 7 names the check
        // that refused (section 4.2). The pipeline itself refuses a check on a machine that is not
        // the M3 test unit (expectedModel for another model or BIOS version, expectedUnit for
        // another OptiPlex 7020 on A18, so the record in write-log.jsonl says the same); the
        // fallback below only keeps that rule should a passed check ever not be changeable. The
        // record stays the pipeline's, as appended to write-log.jsonl.
        WritabilityVerdict? verdict = prepared.Writability;
        bool machineEnabled = prepared.Record.Machine is { Confirmed: true, UnitConfirmed: true };
        bool changeable = prepared.Passed && (verdict?.IsChangeable ?? machineEnabled);
        int exitCode = !prepared.Passed ? prepared.ExitCode : changeable ? ExitCodes.Success : ExitCodes.Blocked;
        WriteRecord record = prepared.Record;
        string? blockedBy = record.BlockedBy;
        if (prepared.Passed && !changeable && !machineEnabled)
        {
            blockedBy = record.Machine?.Confirmed == true ? CheckNames.ExpectedUnit : CheckNames.ExpectedModel;
        }

        WriteDocument document = new(
            CommandName,
            record.Operation,
            record.TransactionId,
            exitCode == ExitCodes.Success,
            exitCode,
            true,
            record.Confirmed,
            record.Variable,
            name,
            null,
            null,
            null,
            Array.Empty<DiffRow>(),
            record.Checks,
            blockedBy,
            prepared.Warnings,
            verdict is null ? null : new WritabilityDocument(verdict.Verdict, verdict.Sentence, verdict.IsChangeable),
            BitLockerDocument(prepared.BitLocker, record, null, null, false),
            record.SecureBoot,
            record.SnapshotFolder,
            record.ApiCalled,
            record.Api,
            record.Verification,
            prepared.Sentence,
            record);
        return Finish(context, document, dryRunLine: false);
    }

    /// <summary>Prints the newest <paramref name="last"/> records, oldest first, as a table or the log document.</summary>
    private static int ExecuteLog(CommandContext context, int last)
    {
        if (last < 1)
        {
            return context.Fail(ExitCodes.Usage, LastMustBePositive);
        }

        WriteRecordLog log = context.Services.CreateRecordLog();

        // ReadAll shows nothing for a list that exists but cannot be read, which would read as "no
        // write attempts". The budget count reads the file the same way and names that problem, so
        // it is asked first (from the earliest time, so every line is read), and the problem is a
        // failure here, as it is a refusal for the write budget.
        if (log.CountSinceStart(DateTimeOffset.MinValue).Problem is string unreadable)
        {
            return context.Fail(ExitCodes.NativeFailure, CheckSentences.Format(LogUnreadableFormat, log.FilePath, unreadable));
        }

        // The lines ReadAll skips are shown as a number, so a write budget refusal that counted
        // them can be traced here; a file that stopped being readable shows no number at all.
        if (log.CountLinesThatAreNotRecords() is not int notRecords)
        {
            return context.Fail(ExitCodes.NativeFailure, CheckSentences.Format(LogUnreadableFormat, log.FilePath, LogSecondReadFailed));
        }

        IReadOnlyList<WriteRecord> all = log.ReadAll();
        IReadOnlyList<WriteRecord> shown = all.Count <= last ? all : all.Skip(all.Count - last).ToArray();
        context.Logger.Information("write log: {Shown} of {Total} records in {File}, {NotRecords} lines that are not records", shown.Count, all.Count, log.FilePath, notRecords);
        if (context.Json)
        {
            context.WriteJson(new WriteLogDocument(LogCommandName, true, log.FilePath, all.Count, notRecords, shown), JsonOutput.KeyPreservingOptions);
            return ExitCodes.Success;
        }

        if (shown.Count == 0)
        {
            context.Out.WriteLine(CheckSentences.Format(NoRecordsFormat, log.FilePath));
            WriteNotRecords(context, notRecords);
            return ExitCodes.Success;
        }

        TableWriter table = new(TimeHeader, TransactionLabel, OperationLabel, VariableLabel, OutcomeLabel, ExitHeader, ApiCalledLabel, BlockedByLabel, SentenceHeader);
        foreach (WriteRecord record in shown)
        {
            table.AddRow(
                record.TimestampUtc,
                record.TransactionId,
                record.Operation,
                Format.Text(record.Variable?.Name),
                record.Outcome.ToString(),
                record.ExitCode,
                record.ApiCalled,
                Format.Text(record.BlockedBy),
                record.Sentence);
        }

        table.Write(context.Out);
        context.Out.WriteLine();
        context.Out.WriteLine(CheckSentences.Format(RecordsShownFormat, shown.Count, all.Count, log.FilePath));
        WriteNotRecords(context, notRecords);
        return ExitCodes.Success;
    }

    /// <summary>write log's line about the lines that are not whole records, when there are any.</summary>
    private static void WriteNotRecords(CommandContext context, int notRecords)
    {
        if (notRecords > 0)
        {
            context.Out.WriteLine(CheckSentences.Format(NotRecordsFormat, notRecords));
        }
    }

    // ---- the commit -----------------------------------------------------------------------

    /// <summary>
    /// Commits the plan once. Ctrl+C is ignored from the moment the commit starts (stage S11)
    /// until its result is in (stage S17): the handler is installed before the call and removed
    /// in the finally block, so it is gone on every path, an exception included.
    /// </summary>
    private static TransactionResult CommitIgnoringCtrlC(IWritePipeline pipeline, ChangePlan plan, ConfirmationToken token)
    {
        Console.CancelKeyPress += IgnoreCancelKey;
        Interlocked.Increment(ref _cancelKeyHandlers);
        try
        {
            return pipeline.CommitAsync(plan, token).GetAwaiter().GetResult();
        }
        finally
        {
            Interlocked.Decrement(ref _cancelKeyHandlers);
            Console.CancelKeyPress -= IgnoreCancelKey;
        }
    }

    /// <summary>
    /// The Ctrl+C handler of a commit: keeps the process running and says why. The key is
    /// cancelled before the line is written, so it stays ignored should standard error fail.
    /// Internal so that the tests raise it with the arguments the runtime passes for Ctrl+C; that
    /// <see cref="CommitIgnoringCtrlC"/> subscribes it before the commit and removes it in its
    /// finally block is pinned by a source check in the tests.
    /// </summary>
    internal static void IgnoreCancelKey(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        try
        {
            Console.Error.WriteLine(CtrlCIgnored);
        }
        catch (Exception)
        {
            // Telling the person is a courtesy; the key is ignored whether or not the line was written.
        }
    }

    // ---- reporting ------------------------------------------------------------------------

    /// <summary>
    /// Standard error after the dry run: every warning, the node types beside a removable-entry
    /// warning, the three BitLocker lines while protectors may exist, and the hint when BitLocker
    /// needs a choice and --bitlocker was not given.
    /// </summary>
    private static void ReportChecks(CommandContext context, PrepareResult prepared, string? risk, ChangeSettings settings)
    {
        foreach (string warning in prepared.Warnings)
        {
            context.Warn(warning);
        }

        WriteRecord record = prepared.Record;
        if (prepared.Checks.Any(check => check.Name == CheckNames.RemovableAhead && check.IsWarning))
        {
            foreach (KeyValuePair<string, IReadOnlyList<string>> entry in record.DeviceNodeTypes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                context.Diagnostic(CheckSentences.Format(DeviceNodeTypesFormat, entry.Key, string.Join(ListSeparator, entry.Value)));
            }
        }

        // A change that passed its rules (the record has the proposed value) while protectors may exist.
        if (prepared.BitLocker is { WarningShown: true } assessment && record.After is not null)
        {
            context.Diagnostic(CheckSentences.Format(BitLockerStateLineFormat, assessment.StateSentence));
            if (risk is not null)
            {
                context.Diagnostic(CheckSentences.Format(RiskLineFormat, risk));
            }

            if (assessment.RecoveryKeySentence is string reminder)
            {
                context.Diagnostic(CheckSentences.Format(RecoveryKeyLineFormat, reminder));
            }
        }

        // Only when stage S8 refused for want of a choice: a refusal by an earlier rule is not
        // lifted by --bitlocker, so the hint would mislead there.
        if (settings.BitLocker is null && prepared.BlockedBy == CheckNames.BitLocker && record.BitLocker?.SuspendOffered == true)
        {
            context.Diagnostic(BitLockerHint);
        }
    }

    /// <summary>
    /// Writes the result: the document (JSON) or the tables (text), then the sentence: on
    /// standard output after the tables when the exit code is 0, on standard error otherwise; and
    /// the dry-run line on standard error for every run without --confirm.
    /// </summary>
    private static int Finish(CommandContext context, WriteDocument document, bool dryRunLine)
    {
        context.Logger.Information(
            "write {Operation} {TransactionId}: outcome {Outcome}, exit {ExitCode}, blocked by {BlockedBy}",
            document.Operation,
            document.TransactionId,
            document.Record.Outcome,
            document.ExitCode,
            document.BlockedBy);
        if (context.Json)
        {
            context.WriteJson(document, JsonOutput.KeyPreservingOptions);
        }
        else
        {
            WriteText(context.Out, document);
        }

        if (document.ExitCode == ExitCodes.Success)
        {
            if (!context.Json)
            {
                context.Out.WriteLine();
                context.Out.WriteLine(document.Sentence);
            }
        }
        else
        {
            context.Error.WriteLine(document.Sentence);
        }

        if (dryRunLine)
        {
            context.Diagnostic(DryRunSentence);
        }

        return document.ExitCode;
    }

    /// <summary>The text form of the document: the summary table, the entry tables, the changes and the checks.</summary>
    private static void WriteText(TextWriter output, WriteDocument document)
    {
        TableWriter summary = new(FieldHeader, ValueHeader);
        summary.AddRow(OperationLabel, document.Operation);
        summary.AddRow(VariableLabel, document.Variable is null ? Format.Text(null) : CheckSentences.Format(VariableFormat, document.Variable.Name, document.Variable.Guid));
        summary.AddRow(RequestedLabel, document.Requested);
        summary.AddRow(TransactionLabel, document.TransactionId);
        summary.AddRow(DryRunLabel, document.DryRun);
        summary.AddRow(ConfirmedLabel, document.Confirmed);
        summary.AddRow(OutcomeLabel, document.Record.Outcome.ToString());
        summary.AddRow(BlockedByLabel, Format.Text(document.BlockedBy));
        summary.AddRow(ExitCodeLabel, document.ExitCode);
        if (document.Writability is WritabilityDocument writability)
        {
            summary.AddRow(VerdictLabel, writability.Verdict.ToString());
            summary.AddRow(WritabilityLabel, writability.Sentence);
        }

        if (document.BitLocker is WriteBitLockerDocument bitLocker)
        {
            summary.AddRow(BitLockerLabel, bitLocker.Sentence);
        }

        if (document.SnapshotFolder is string folder)
        {
            summary.AddRow(SnapshotFolderLabel, folder);
        }

        if (!document.DryRun)
        {
            summary.AddRow(ApiCalledLabel, document.ApiCalled);
        }

        if (document.Api is WriteRecordApi api)
        {
            summary.AddRow(ApiResultLabel, api.Succeeded ? ApiSucceededText : CheckSentences.Format(ApiFailedFormat, api.Win32Error, api.Win32Name, api.Win32Text));
            summary.AddRow(NtStatusLabel, api.NtStatus is uint status ? CheckSentences.Format(NtStatusFormat, status, api.NtStatusText, api.NtStatusSource) : api.NtStatusSource);
        }

        if (document.Verification is WriteRecordVerification verification)
        {
            summary.AddRow(VerificationLabel, CheckSentences.Format(VerificationFormat, verification.ReadStatus, Format.Bool(verification.MatchesAfter), Format.Bool(verification.MatchesBefore)));
        }

        summary.Write(output);
        WriteImage(output, BeforeTitle, document.Before);
        WriteImage(output, AfterTitle, document.Proposed);
        WriteImage(output, ReadBackTitle, document.After);
        if (document.Diff.Count > 0)
        {
            output.WriteLine();
            output.WriteLine(ChangesTitle);
            TableWriter diff = new(PositionHeader, BeforeHeader, AfterHeader, DescriptionHeader, FlagsHeader);
            foreach (DiffRow row in document.Diff)
            {
                diff.AddRow(row.Position, Format.Text(row.Before, NotSetText), Format.Text(row.After, NotSetText), row.Description, row.Flags);
            }

            diff.Write(output);
        }

        output.WriteLine();
        output.WriteLine(ChecksTitle);
        TableWriter checks = new(CheckHeader, ResultHeader, ReasonHeader);
        foreach (CheckResult check in document.Checks)
        {
            checks.AddRow(check.Name, !check.Passed ? FailedText : check.IsWarning ? WarningText : PassedText, check.Reason);
        }

        checks.Write(output);
    }

    /// <summary>One value as a titled table of its entries by position, or "(not set)" for an absent value.</summary>
    private static void WriteImage(TextWriter output, string title, WriteImageDocument? image)
    {
        if (image is null)
        {
            return;
        }

        output.WriteLine();
        output.WriteLine(title);
        if (!image.Present)
        {
            output.WriteLine(NotSetText);
            return;
        }

        TableWriter table = new(PositionHeader, EntryHeader, DescriptionHeader, FlagsHeader);
        foreach (WriteEntryDocument entry in image.Entries)
        {
            table.AddRow(entry.Position, entry.Name, entry.Description, EntryFlags(entry));
        }

        table.Write(output);
    }

    /// <summary>An entry's flags as the decoders spell them (Core's one spelling); empty for an entry the plan does not describe.</summary>
    private static string EntryFlags(WriteEntryDocument entry)
    {
        if (string.IsNullOrEmpty(entry.Category))
        {
            return string.Empty;
        }

        EntryLine line = new(entry.Position, entry.Name, entry.Description, entry.Active, entry.Hidden, entry.Category, string.Empty);
        return line.Flags;
    }

    // ---- the document ---------------------------------------------------------------------

    /// <summary>The document of a change: the dry run's plan and checks, and the commit's record, call and read back when there was one.</summary>
    private static WriteDocument ChangeDocument(PrepareResult prepared, TransactionResult? committed, SnapshotStore snapshots, string requested, string? risk, ChangeSettings settings)
    {
        WriteRecord record = committed?.Record ?? prepared.Record;
        ChangePlan? plan = committed?.Plan ?? prepared.Plan;
        int exitCode = committed?.ExitCode ?? prepared.ExitCode;
        return new WriteDocument(
            CommandName,
            record.Operation,
            record.TransactionId,
            exitCode == ExitCodes.Success,
            exitCode,
            !settings.Confirm,
            record.Confirmed,
            record.Variable,
            requested,
            plan is null ? null : Image(plan.Before, plan.BeforeEntries),
            plan is null ? null : Image(plan.After, plan.AfterEntries),
            committed is null ? null : ReadBackImage(committed, snapshots),
            plan?.Diff ?? Array.Empty<DiffRow>(),
            record.Checks,
            record.BlockedBy,
            prepared.Warnings,
            null,
            BitLockerDocument(prepared.BitLocker, record, risk, settings.BitLocker, committed?.BitLockerSuspended ?? false),
            record.SecureBoot,
            record.SnapshotFolder,
            record.ApiCalled,
            record.Api,
            record.Verification,
            committed?.Sentence ?? prepared.Sentence,
            record);
    }

    /// <summary>The BitLocker block from the dry run's assessment and the record; null when BitLocker was not assessed.</summary>
    private static WriteBitLockerDocument? BitLockerDocument(BitLockerAssessment? assessment, WriteRecord record, string? risk, BitLockerChoice? given, bool suspended)
    {
        if (assessment is null)
        {
            return null;
        }

        return new WriteBitLockerDocument(
            record.BitLocker?.State ?? assessment.State,
            assessment.StateSentence,
            risk,
            assessment.RecoveryKeySentence,
            record.BitLocker?.SuspendOffered ?? false,
            given,
            suspended);
    }

    /// <summary>A value of the plan with its decoded entry lines.</summary>
    private static WriteImageDocument Image(VariableImage image, IReadOnlyList<EntryLine> lines)
    {
        return new WriteImageDocument(
            image.Present,
            image.Attributes,
            Format.ShortFlags((VariableAttributes)image.Attributes),
            image.Size,
            image.Sha256,
            image.Hex,
            lines.Select(line => new WriteEntryDocument(line.Position, line.Name, line.Description, line.Active, line.Hidden, line.Category)).ToArray());
    }

    /// <summary>
    /// The value the verification read found: the proposed or the previous value when it matched
    /// one of them, an absent value when the variable was not found, the bytes snapshot.json
    /// recorded when it matched neither, and null when nothing was read.
    /// </summary>
    private static WriteImageDocument? ReadBackImage(TransactionResult committed, SnapshotStore snapshots)
    {
        if (committed.Plan is not ChangePlan plan || committed.Verification is not VerificationResult verification)
        {
            return null;
        }

        if (verification.MatchesAfter)
        {
            return Image(plan.After, plan.AfterEntries);
        }

        if (verification.MatchesBefore)
        {
            return Image(plan.Before, plan.BeforeEntries);
        }

        if (verification.ReadStatus == ReadStatus.NotFound)
        {
            return Image(VariableImage.Absent(0), Array.Empty<EntryLine>());
        }

        if (verification.ReadStatus != ReadStatus.Success)
        {
            return null;
        }

        byte[] bytes = ReadBackBytes(committed.SnapshotFolder, snapshots) ?? Array.Empty<byte>();
        VariableImage image = VariableImage.FromBytes(bytes, verification.Attributes ?? 0);
        EntryLine[] known = plan.BeforeEntries.Concat(plan.AfterEntries).ToArray();
        EntryLine[] lines = BootListCodec.Decode(bytes).Names
            .Select((name, index) => Line(known, name, index + 1))
            .ToArray();
        return Image(image, lines) with { Sha256 = verification.Sha256 ?? image.Sha256 };
    }

    /// <summary>The bytes the verification read found, as snapshot.json recorded them, or null when they cannot be read.</summary>
    private static byte[]? ReadBackBytes(string? folder, SnapshotStore snapshots)
    {
        try
        {
            string? hex = folder is null ? null : snapshots.Read(folder).Document?.After?.Hex;
            return hex is null ? null : Convert.FromHexString(hex);
        }
        catch (Exception)
        {
            // The document's hash still says what was read; the bytes are only shown when readable.
            return null;
        }
    }

    /// <summary>The plan's line for an entry at a new position, or a bare line for an entry the plan does not describe.</summary>
    private static EntryLine Line(IReadOnlyList<EntryLine> known, string name, int position)
    {
        EntryLine? line = known.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
        return line is null
            ? new EntryLine(position, name, string.Empty, false, false, string.Empty, string.Empty)
            : line with { Position = position };
    }

    /// <summary>
    /// The risk sentence of the BitLocker lines while protectors may
    /// exist: for a one-time boot the entry it starts; for a reorder the plain sentence, or the
    /// one that names the first entry for a reorder that keeps BootCurrent's entry first, that is
    /// first before the change and first after it (the rule the application's dialog follows). An
    /// order that moves BootCurrent's entry up to first changes the start: BootCurrent not being
    /// first means the last start went past the first entry. BootCurrent is taken from what the
    /// rules read; a dry run that stopped before the plan reads it once more.
    /// </summary>
    private static string? RiskSentence(PrepareResult prepared, CliServices services)
    {
        WriteRecord record = prepared.Record;
        if (prepared.BitLocker is not { WarningShown: true } || record.After is not { Present: true } proposed || proposed.Entries.Count == 0)
        {
            return null;
        }

        string first = proposed.Entries[0];
        if (string.Equals(record.Operation, nameof(ChangeOperation.SetBootNext), StringComparison.Ordinal))
        {
            return CheckSentences.Format(BitLockerPreflight.BootNextRiskFormat, first);
        }

        if (!string.Equals(record.Operation, nameof(ChangeOperation.Reorder), StringComparison.Ordinal))
        {
            return null;
        }

        bool firstStays = string.Equals(FirstEntryBefore(prepared.Plan, record), first, StringComparison.Ordinal)
            && string.Equals(BootCurrentEntry(prepared.Plan, services), first, StringComparison.Ordinal);
        return firstStays
            ? CheckSentences.Format(BitLockerPreflight.ReorderRiskFirstEntryStaysFormat, first)
            : BitLockerPreflight.ReorderRiskSentence;
    }

    /// <summary>
    /// The first entry of the order before the change: the plan's, or, when the dry run stopped
    /// before a plan was made (at the BitLocker check, for example), the one the record holds;
    /// null when the order was absent or empty.
    /// </summary>
    private static string? FirstEntryBefore(ChangePlan? plan, WriteRecord record)
    {
        if (plan is not null)
        {
            return plan.BeforeEntries.Count > 0 ? plan.BeforeEntries[0].Name : null;
        }

        return record.Before is { Present: true } before && before.Entries.Count > 0 ? before.Entries[0] : null;
    }

    /// <summary>The Boot#### BootCurrent names, from the plan's dependencies or a direct read; null when absent or not one entry.</summary>
    private static string? BootCurrentEntry(ChangePlan? plan, CliServices services)
    {
        VariableDependency? known = plan?.Dependencies.FirstOrDefault(dependency =>
            dependency.Guid == KnownGuids.EfiGlobalVariable && string.Equals(dependency.Name, BootCurrentName, StringComparison.Ordinal));
        byte[]? bytes = known is not null
            ? (known.Image.Present ? known.Image.Bytes : null)
            : ReadBootCurrent(services);
        return bytes is { Length: BootListCodec.EntrySize } ? BootListCodec.Decode(bytes).Names[0] : null;
    }

    /// <summary>One read-only read of BootCurrent through the store; null when it fails.</summary>
    private static byte[]? ReadBootCurrent(CliServices services)
    {
        try
        {
            ReadResult read = services.Store.Read(BootCurrentName, KnownGuids.EfiGlobalVariable);
            return read.Status == ReadStatus.Success ? read.Variable?.Data : null;
        }
        catch (Exception ex)
        {
            services.Logger.Warning(ex, "write: BootCurrent could not be read for the risk sentence");
            return null;
        }
    }

    // ---- option values --------------------------------------------------------------------

    /// <summary>
    /// The --snapshot-root value as a full path, or null for the default root. Whether the root
    /// may be used is the snapshot store's decision at stage S9; this only turns the text into a path.
    /// </summary>
    private static bool TryResolveSnapshotRoot(string? text, out string? root, [NotNullWhen(false)] out string? problem)
    {
        root = null;
        problem = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        try
        {
            root = Path.GetFullPath(text.Trim());
            return true;
        }
        catch (Exception ex)
        {
            problem = CheckSentences.Format(BadSnapshotRootFormat, text, ex.Message);
            return false;
        }
    }

    /// <summary>The choice a --bitlocker value stands for, or null when the option was absent.</summary>
    private static BitLockerChoice? ChoiceOf(string? value)
    {
        return value switch
        {
            SuspendValue => BitLockerChoice.SuspendForOneRestart,
            ContinueValue => BitLockerChoice.KeepProtection,
            _ => null,
        };
    }

    /// <summary>True when the option appears on the command line (not only as a default).</summary>
    private static bool IsGiven(CommandResult result, Option option)
    {
        return result.GetResult(option) is { Implicit: false };
    }

    /// <summary>A text option's value inside a validator; null when absent or unreadable (a validator never throws).</summary>
    private static string? ValueOf(OptionResult result)
    {
        try
        {
            return result.GetValueOrDefault<string?>();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>A text argument's value inside a validator; null when absent or unreadable.</summary>
    private static string? ValueOf(ArgumentResult result)
    {
        try
        {
            return result.GetValueOrDefault<string?>();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>A text argument's value inside a command validator; null when absent or unreadable.</summary>
    private static string? ValueOf(CommandResult result, Argument<string?> argument)
    {
        try
        {
            return result.GetValue(argument);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>A text argument's value in the action; null when absent or unreadable.</summary>
    private static string? ValueOf(ParseResult parseResult, Argument<string?> argument)
    {
        try
        {
            return parseResult.GetValue(argument);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The --last value inside its validator; the default when it cannot be read.</summary>
    private static int CountOf(OptionResult result)
    {
        try
        {
            return result.GetValueOrDefault<int>();
        }
        catch (Exception)
        {
            // A value that is not a number already has its own parse error.
            return DefaultLastRecords;
        }
    }

    /// <summary>The --last value in the action; the default when it cannot be read.</summary>
    private static int CountOf(ParseResult parseResult, Option<int> option)
    {
        try
        {
            return parseResult.GetValue(option);
        }
        catch (Exception)
        {
            return DefaultLastRecords;
        }
    }

    /// <summary>The options bootorder, bootnext and restore share; each subcommand gets its own instances.</summary>
    /// <param name="Json">--json.</param>
    /// <param name="DryRun">--dry-run, the explicit spelling of the default.</param>
    /// <param name="Confirm">--confirm.</param>
    /// <param name="BitLocker">--bitlocker suspend|continue.</param>
    /// <param name="SnapshotRoot">--snapshot-root.</param>
    private sealed record ChangeOptions(Option<bool> Json, Option<bool> DryRun, Option<bool> Confirm, Option<string?> BitLocker, Option<string?> SnapshotRoot)
    {
        /// <summary>
        /// Creates the options, adds them to <paramref name="command"/> with their rules: --bitlocker
        /// accepts suspend and continue only, --snapshot-root must be a folder path and is for a
        /// dry run only, so --snapshot-root with --confirm is a usage error, as is --dry-run with
        /// --confirm. Every rule is checked when the command line is parsed, so a violation is exit
        /// code 2 before any check or read runs.
        /// </summary>
        /// <param name="command">The subcommand.</param>
        /// <returns>The options.</returns>
        public static ChangeOptions AddTo(Command command)
        {
            ChangeOptions options = new(
                CommonOptions.Json(),
                new Option<bool>(DryRunOption) { Description = DryRunDescription },
                new Option<bool>(ConfirmOption) { Description = ConfirmDescription },
                new Option<string?>(BitLockerOption) { Description = BitLockerDescription },
                new Option<string?>(SnapshotRootOption) { Description = SnapshotRootDescription, HelpName = DirectoryHelpName });
            options.BitLocker.AcceptOnlyFromAmong(SuspendValue, ContinueValue);
            options.SnapshotRoot.Validators.Add(result =>
            {
                if (!TryResolveSnapshotRoot(ValueOf(result), out _, out string? problem))
                {
                    result.AddError(problem);
                }
            });
            command.Add(options.Json);
            command.Add(options.DryRun);
            command.Add(options.Confirm);
            command.Add(options.BitLocker);
            command.Add(options.SnapshotRoot);
            command.Validators.Add(result =>
            {
                if (IsGiven(result, options.DryRun) && IsGiven(result, options.Confirm))
                {
                    result.AddError(DryRunAndConfirmTogether);
                }

                if (IsGiven(result, options.SnapshotRoot) && IsGiven(result, options.Confirm))
                {
                    result.AddError(SnapshotRootWithConfirm);
                }
            });
            return options;
        }

        /// <summary>What the options asked for.</summary>
        /// <param name="parseResult">The parse result.</param>
        /// <returns>The settings.</returns>
        public ChangeSettings Read(ParseResult parseResult)
        {
            return new ChangeSettings(
                CommandRunner.GetFlag(parseResult, Confirm),
                ChoiceOf(CommandRunner.GetString(parseResult, BitLocker)),
                CommandRunner.GetString(parseResult, SnapshotRoot));
        }
    }

    /// <summary>What the shared options of a change asked for.</summary>
    /// <param name="Confirm">True with --confirm: the plan is committed when every check passed.</param>
    /// <param name="BitLocker">The --bitlocker choice, or null when the option was absent.</param>
    /// <param name="SnapshotRoot">The --snapshot-root value as given, or null.</param>
    private sealed record ChangeSettings(bool Confirm, BitLockerChoice? BitLocker, string? SnapshotRoot);
}
