// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using UefiAccess.Cli.Output;
using UefiAccess.Cli.Services;
using UefiAccess.Core.Settings;
using UefiAccess.Core.Settings.Dell;

namespace UefiAccess.Cli.Commands;

/// <summary>The JSON document of the dell command.</summary>
/// <param name="Command">"dell".</param>
/// <param name="Ok">False only when the outcome is Unavailable.</param>
/// <param name="Outcome">"Complete", "Partial" or "Unavailable".</param>
/// <param name="Source">The interface that answered, "DellCommandMonitor" or "DellCommandConfigure"; null when the outcome is Unavailable.</param>
/// <param name="ReadAt">When the read started, local time with its offset ("2026-10-05T09:12:44+02:00").</param>
/// <param name="ElapsedMs">How long the read took, in milliseconds.</param>
/// <param name="Machine">The machine's manufacturer, model and BIOS version; never a serial number or service tag.</param>
/// <param name="CheckedAgainstBios">The BIOS version the names and values were checked against.</param>
/// <param name="ListStatus">Whether the list of agreed settings is approved.</param>
/// <param name="NativeInterfacePresent">True when nothing answered, Dell Command Monitor is not installed, and the built-in Dell BIOS interface of newer models is present, which this version does not read.</param>
/// <param name="Settings">One entry per agreed setting, in the order of the list, all from the one interface that answered.</param>
/// <param name="AllAttributes">With --all, every attribute Dell Command Monitor reported, codes and Dell's names as it reported them; otherwise, and when it did not answer, null.</param>
/// <param name="Fallback">The fallback sentence when Dell Command Configure answered after Dell Command Monitor did not; otherwise null.</param>
/// <param name="Problems">Every problem the read met, in the order met.</param>
/// <param name="Warnings">The sentences that qualify a usable result (another BIOS version than the verified one); also on standard error.</param>
/// <param name="Sentence">The problem sentence when the outcome is Unavailable (also on standard error); otherwise null.</param>
internal sealed record DellSettingsDocument(
    string Command,
    bool Ok,
    string Outcome,
    string? Source,
    string ReadAt,
    long ElapsedMs,
    DellMachine Machine,
    string CheckedAgainstBios,
    string ListStatus,
    bool NativeInterfacePresent,
    IReadOnlyList<DellSettingDocument> Settings,
    IReadOnlyList<DellVendorAttribute>? AllAttributes,
    string? Fallback,
    IReadOnlyList<DellProblemDocument> Problems,
    IReadOnlyList<string> Warnings,
    string? Sentence);

/// <summary>One agreed Dell BIOS setting in the dell document.</summary>
/// <param name="Id">The catalog id, for example "NumLock".</param>
/// <param name="Name">Dell Command Monitor's attribute name, for example "Num Lock".</param>
/// <param name="DisplayName">The name shown, for example "Num Lock at boot".</param>
/// <param name="Category">The BIOS setup page group, for example "POST Behavior".</param>
/// <param name="SetupLocation">Where the setting is in the BIOS setup, for example "POST Behavior, Numlock LED".</param>
/// <param name="State">"Read", "NotApplicable", "NoValueReported", "Unnamed", "NotReported" or "NotRead".</param>
/// <param name="Current">The current value: the interface's token, Dell's name for it and the words shown; null when there is none.</param>
/// <param name="Text">The words for the current value by its state, as the text table shows them; never a code.</param>
/// <param name="PossibleValues">The values the setting can take, each with its token, Dell's name and the words shown.</param>
/// <param name="PossibleValuesOrigin">"Interface", or "Catalog" when the interface lists none (Dell Command Configure).</param>
/// <param name="VendorMarksReadOnly">Dell's IsReadOnly as reported; null when the interface reports none.</param>
/// <param name="DisplayFromVendor">True when the current value's words are Dell's own because the list has none for it.</param>
/// <param name="Note">A sentence about the row, or null.</param>
/// <param name="Sentence">The row as one sentence, the name first.</param>
internal sealed record DellSettingDocument(
    string Id,
    string Name,
    string DisplayName,
    string Category,
    string SetupLocation,
    string State,
    SettingValue? Current,
    string Text,
    IReadOnlyList<SettingValue> PossibleValues,
    string PossibleValuesOrigin,
    bool? VendorMarksReadOnly,
    bool DisplayFromVendor,
    string? Note,
    string Sentence);

/// <summary>One problem of a Dell read in the dell document.</summary>
/// <param name="Interface">Where the problem lay: "DellCommandMonitor", "DellCommandConfigure", "NativeBiosInterface", "Machine" or "Reader".</param>
/// <param name="Kind">What went wrong, a problem kind name such as "NotInstalled".</param>
/// <param name="Detail">A one-line technical detail (the WMI error, the exit code); for "NotDell", the manufacturer.</param>
internal sealed record DellProblemDocument(string Interface, string Kind, string Detail);

/// <summary>
/// "uefiaccess dell": the agreed Dell BIOS settings, read once through the Core Dell settings reader: Dell Command | Monitor, and Dell Command
/// | Configure only when Dell Command | Monitor gives no answer, or one of the two as --source
/// asks. It changes nothing. The text output is a field table, the settings table and one note
/// line per setting with a note; --all adds every attribute Dell Command | Monitor reports.
/// The fallback sentence and every warning go to standard error. Exit code 0 when every agreed
/// setting was read, 6 when an interface answered for only some of them, 5 when none answered
/// (the problem sentence alone on standard error, and with --json the document with ok false).
/// </summary>
internal static class DellCommand
{
    /// <summary>The command's name, also the document's "command".</summary>
    internal const string CommandName = "dell";

    /// <summary>The value of the source option for the default: Dell Command | Monitor, then the fallback.</summary>
    internal const string AutoSource = "auto";

    /// <summary>The value of the source option for Dell Command | Monitor only.</summary>
    internal const string DcmSource = "dcm";

    /// <summary>The value of the source option for Dell Command | Configure only.</summary>
    internal const string CctkSource = "cctk";

    /// <summary>The usage error of the full enumeration with Dell Command | Configure as the only source.</summary>
    internal const string AllNeedsDcmMessage = "The --all option needs Dell Command Monitor; leave out --source or choose dcm.";

    /// <summary>The usage error of a source value the parser would already refuse; {0} the value.</summary>
    internal const string UnknownSourceFormat = "The value '{0}' given to --source is not one of auto, dcm and cctk.";

    /// <summary>The value of the Changes row: this command changes nothing.</summary>
    internal const string ChangesValue = "none: this tool shows Dell BIOS settings and changes none of them";

    /// <summary>The prefix of the line printed for each setting that has a note; the setting's name and ": " follow, then the note.</summary>
    internal const string NotePrefix = "Note: ";

    /// <summary>The headers and labels of the text output.</summary>
    private const string FieldHeader = "Field";

    private const string ValueHeader = "Value";

    private const string SourceLabel = "Source";

    private const string LoadedLabel = "Loaded";

    private const string BiosLabel = "BIOS";

    private const string ChangesLabel = "Changes";

    private const string SettingHeader = "Setting";

    private const string CurrentValueHeader = "Current value";

    private const string PossibleValuesHeader = "Possible values";

    private const string AttributeHeader = "Attribute";

    private const string CurrentHeader = "Current";

    private const string PossibleHeader = "Possible";

    private const string ReadOnlyFlagHeader = "Dell read-only flag";

    /// <summary>Between a setting's name and its note on a note line.</summary>
    private const string NoteSeparator = ": ";

    private const string Description = "Shows the agreed Dell BIOS settings, read through Dell Command Monitor or, when that does not answer, Dell Command Configure. Changes nothing.";

    private const string SourceOption = "--source";

    private const string SourceDescription = "Where to read from: auto (the default), dcm or cctk.";

    private const string AllOption = "--all";

    private const string AllDescription = "Also lists every attribute Dell Command Monitor reports; not with the cctk source.";

    /// <summary>The time the read started in the Loaded row, with its offset.</summary>
    private const string LoadedTimeFormat = "yyyy-MM-dd HH:mm:ss zzz";

    /// <summary>The Loaded row: {0} the time, {1} the milliseconds the read took.</summary>
    private const string LoadedFormat = "{0} ({1} ms)";

    /// <summary>The BIOS row: {0} this computer's BIOS version, {1} the version the names and values were checked against.</summary>
    private const string BiosFormat = "{0} (names and values checked against {1})";

    /// <summary>The time the read started in the document, ISO 8601 with its offset.</summary>
    private const string ReadAtFormat = "yyyy-MM-dd'T'HH:mm:sszzz";

    /// <summary>A value the read could not tell, as every command prints it.</summary>
    private const string UnknownText = "(unknown)";

    /// <summary>The joint of a possible value's code and Dell's name in the full enumeration ("1=Off").</summary>
    private const string CodeNameSeparator = "=";

    private const string ListSeparator = ", ";

    /// <summary>Builds the command over the production services.</summary>
    /// <param name="verbose">The global --verbose option.</param>
    /// <returns>The command.</returns>
    public static Command Create(Option<bool> verbose)
    {
        return Create(verbose, CliServices.Create);
    }

    /// <summary>
    /// Builds the command over the services from <paramref name="servicesFactory"/>. The source
    /// option accepts its three values only, and the full enumeration with Dell Command | Configure
    /// as the only source is refused, both when the command line is parsed, so either is exit code
    /// 2 before the preconditions are checked or anything is read.
    /// </summary>
    /// <param name="verbose">The global --verbose option.</param>
    /// <param name="servicesFactory">Creates the services for an invocation; receives the --verbose flag.</param>
    /// <returns>The command.</returns>
    public static Command Create(Option<bool> verbose, Func<bool, CliServices> servicesFactory)
    {
        Option<bool> json = CommonOptions.Json();
        Option<string?> source = new(SourceOption)
        {
            Description = SourceDescription,
        };
        source.AcceptOnlyFromAmong(AutoSource, DcmSource, CctkSource);
        Option<bool> all = new(AllOption)
        {
            Description = AllDescription,
        };
        Command command = new(CommandName, Description);
        command.Add(json);
        command.Add(source);
        command.Add(all);
        command.Validators.Add(result =>
        {
            if (FlagOf(result, all) && string.Equals(ValueOf(result, source), CctkSource, StringComparison.Ordinal))
            {
                result.AddError(AllNeedsDcmMessage);
            }
        });
        command.SetAction(parseResult => CommandRunner.Run(
            parseResult,
            verbose,
            json,
            CommandName,
            servicesFactory,
            context => Execute(context, CommandRunner.GetString(parseResult, source), CommandRunner.GetFlag(parseResult, all))));
        return command;
    }

    /// <summary>
    /// The preference a source value asks for: none or auto is Automatic, dcm Dell Command |
    /// Monitor only, and the third value Dell Command | Configure only; null for any other value.
    /// </summary>
    /// <param name="value">The source option's value, or null when it was not given.</param>
    /// <returns>The preference, or null.</returns>
    internal static DellSourcePreference? PreferenceOf(string? value)
    {
        return value switch
        {
            null or AutoSource => DellSourcePreference.Automatic,
            DcmSource => DellSourcePreference.DellCommandMonitor,
            CctkSource => DellSourcePreference.DellCommandConfigure,
            _ => null,
        };
    }

    /// <summary>
    /// The document of one read (pure): the outcome, the source and the machine as read; every
    /// agreed setting with its words, its tokens and its sentence; the full enumeration when it
    /// was kept; the problems with their details; and the problem sentence when nothing answered.
    /// </summary>
    /// <param name="result">The read.</param>
    /// <returns>The document.</returns>
    internal static DellSettingsDocument ToDocument(DellSettingsResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        DellMachine machine = result.Machine ?? new DellMachine(null, null, null);
        return new DellSettingsDocument(
            CommandName,
            result.Outcome != DellReadOutcome.Unavailable,
            result.Outcome.ToString(),
            result.Source?.ToString(),
            result.ReadAt.ToString(ReadAtFormat, CultureInfo.InvariantCulture),
            result.ElapsedMs,
            machine,
            DellAttributeCatalog.VerifiedBiosVersion,
            DellAttributeCatalog.ListStatus,
            result.NativeInterfacePresent,
            SettingsOf(result).Select(ToSettingDocument).ToArray(),
            result.AllAttributes,
            result.FallbackSentence,
            (result.Problems ?? Array.Empty<DellInterfaceProblem>())
                .Where(problem => problem is not null)
                .Select(problem => new DellProblemDocument(problem.Interface.ToString(), problem.Kind.ToString(), problem.Detail))
                .ToArray(),
            WarningsOf(result),
            DellSentences.UnavailableSentence(result));
    }

    private static int Execute(CommandContext context, string? sourceText, bool includeAll)
    {
        if (context.Services.DellSettings is null)
        {
            return context.Fail(ExitCodes.NativeFailure, CliPlatform.NoDellSettingsReaderMessage);
        }

        // The parser already refuses both; nothing is read for a command line it would refuse.
        if (PreferenceOf(sourceText) is not DellSourcePreference preference)
        {
            return context.Fail(ExitCodes.Usage, string.Format(CultureInfo.InvariantCulture, UnknownSourceFormat, sourceText));
        }

        if (includeAll && preference == DellSourcePreference.DellCommandConfigure)
        {
            return context.Fail(ExitCodes.Usage, AllNeedsDcmMessage);
        }

        DellSettingsResult result = SafeInspect.DellSettings(context.Services, new DellReadOptions(preference, includeAll));
        DellSettingsDocument document = ToDocument(result);
        if (result.Outcome == DellReadOutcome.Unavailable)
        {
            // No table: the problem sentence alone on standard error, and with --json the document
            // with ok false (written first, so Fail adds no error document).
            if (context.Json)
            {
                context.WriteJson(document);
            }

            return context.Fail(ExitCodes.NativeFailure, document.Sentence ?? DellSentences.UnavailableLead);
        }

        if (!string.IsNullOrWhiteSpace(result.FallbackSentence))
        {
            context.Warn(result.FallbackSentence);
        }

        foreach (string warning in document.Warnings)
        {
            context.Warn(warning);
        }

        if (context.Json)
        {
            context.WriteJson(document);
        }
        else
        {
            WriteText(context.Out, result);
        }

        return result.Outcome == DellReadOutcome.Complete ? ExitCodes.Success : ExitCodes.Partial;
    }

    /// <summary>
    /// The text output: the field table (source, when the read started
    /// and how long it took, the BIOS version against the verified one, and that nothing is
    /// changed); the settings table, one row per agreed setting in the words of the BIOS setup;
    /// one note line per setting with a note; and, when the full enumeration was kept, the table
    /// of every attribute with its codes and Dell's names.
    /// </summary>
    private static void WriteText(TextWriter output, DellSettingsResult result)
    {
        DellMachine machine = result.Machine ?? new DellMachine(null, null, null);
        TableWriter fields = new(FieldHeader, ValueHeader);
        fields.AddRow(SourceLabel, result.Source is DellSettingsSource source ? DellSentences.SourceName(source) : UnknownText);
        fields.AddRow(LoadedLabel, string.Format(CultureInfo.InvariantCulture, LoadedFormat, result.ReadAt.ToString(LoadedTimeFormat, CultureInfo.InvariantCulture), result.ElapsedMs));
        fields.AddRow(BiosLabel, string.Format(CultureInfo.InvariantCulture, BiosFormat, Format.Text(machine.BiosVersion, UnknownText), DellAttributeCatalog.VerifiedBiosVersion));
        fields.AddRow(ChangesLabel, ChangesValue);
        fields.Write(output);

        IReadOnlyList<FirmwareSetting> settings = SettingsOf(result);
        output.WriteLine();
        TableWriter table = new(SettingHeader, CurrentValueHeader, PossibleValuesHeader);
        foreach (FirmwareSetting setting in settings)
        {
            table.AddRow(setting.DisplayName, DellSentences.ValueText(setting), string.Join(ListSeparator, PossibleTexts(setting)));
        }

        table.Write(output);

        FirmwareSetting[] noted = settings.Where(setting => !string.IsNullOrWhiteSpace(setting.Note)).ToArray();
        if (noted.Length > 0)
        {
            output.WriteLine();
            foreach (FirmwareSetting setting in noted)
            {
                output.WriteLine(NotePrefix + setting.DisplayName + NoteSeparator + setting.Note);
            }
        }

        if (result.AllAttributes is { } attributes)
        {
            output.WriteLine();
            TableWriter all = new(AttributeHeader, CurrentHeader, PossibleHeader, ReadOnlyFlagHeader);
            foreach (DellVendorAttribute attribute in attributes.Where(attribute => attribute is not null))
            {
                IReadOnlyList<string> possible = attribute.PossibleValues ?? Array.Empty<string>();
                IReadOnlyList<string> names = attribute.PossibleNames ?? Array.Empty<string>();
                all.AddRow(
                    attribute.Name,
                    Format.Join(CurrentWithNames(attribute.CurrentValues ?? Array.Empty<string>(), possible, names)),
                    Format.Join(PossibleWithNames(possible, names)),
                    Format.Bool(attribute.IsReadOnly));
            }

            all.Write(output);
        }
    }

    /// <summary>The settings of a result; empty when it has none.</summary>
    private static IReadOnlyList<FirmwareSetting> SettingsOf(DellSettingsResult result)
    {
        return (result.Settings ?? Array.Empty<FirmwareSetting>()).Where(setting => setting is not null).ToArray();
    }

    /// <summary>The warnings of a result; empty when it has none.</summary>
    private static IReadOnlyList<string> WarningsOf(DellSettingsResult result)
    {
        return (result.Warnings ?? Array.Empty<string>()).Where(warning => !string.IsNullOrWhiteSpace(warning)).ToArray();
    }

    private static DellSettingDocument ToSettingDocument(FirmwareSetting setting)
    {
        return new DellSettingDocument(
            setting.Id,
            setting.Name,
            setting.DisplayName,
            setting.Category,
            setting.SetupLocation,
            setting.State.ToString(),
            setting.Current,
            DellSentences.ValueText(setting),
            setting.PossibleValues ?? Array.Empty<SettingValue>(),
            setting.PossibleValuesOrigin.ToString(),
            setting.VendorMarksReadOnly,
            setting.DisplayFromVendor,
            setting.Note,
            DellSentences.RowSentence(setting));
    }

    /// <summary>The words of a setting's possible values, in the interface's order.</summary>
    private static IEnumerable<string> PossibleTexts(FirmwareSetting setting)
    {
        return (setting.PossibleValues ?? Array.Empty<SettingValue>()).Where(value => value is not null).Select(value => value.Text);
    }

    /// <summary>Each current code with Dell's name for it ("1=Off"), or the code alone when Dell names it nowhere.</summary>
    private static IEnumerable<string> CurrentWithNames(IReadOnlyList<string> codes, IReadOnlyList<string> possible, IReadOnlyList<string> names)
    {
        foreach (string code in codes.Where(code => !string.IsNullOrEmpty(code)))
        {
            int index = -1;
            for (int i = 0; i < possible.Count; i++)
            {
                if (string.Equals(possible[i], code, StringComparison.Ordinal))
                {
                    index = i;
                    break;
                }
            }

            yield return WithName(code, index < 0 ? null : NameAt(names, index));
        }
    }

    /// <summary>Each possible code with Dell's name for it, index for index ("1=Off, 2=Last, 3=On"); blank codes are left out.</summary>
    private static IEnumerable<string> PossibleWithNames(IReadOnlyList<string> possible, IReadOnlyList<string> names)
    {
        for (int i = 0; i < possible.Count; i++)
        {
            if (!string.IsNullOrEmpty(possible[i]))
            {
                yield return WithName(possible[i], NameAt(names, i));
            }
        }
    }

    private static string? NameAt(IReadOnlyList<string> names, int index)
    {
        return index < names.Count && !string.IsNullOrEmpty(names[index]) ? names[index] : null;
    }

    private static string WithName(string code, string? name)
    {
        return name is null ? code : code + CodeNameSeparator + name;
    }

    /// <summary>A flag's value inside a command validator; false when absent or unreadable (a validator never throws).</summary>
    private static bool FlagOf(CommandResult result, Option<bool> option)
    {
        try
        {
            return result.GetValue(option);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>A text option's value inside a command validator; null when absent or unreadable.</summary>
    private static string? ValueOf(CommandResult result, Option<string?> option)
    {
        try
        {
            return result.GetValue(option);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
