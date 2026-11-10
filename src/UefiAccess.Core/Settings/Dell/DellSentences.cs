// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;

namespace UefiAccess.Core.Settings.Dell;

/// <summary>
/// Every sentence and word of the Dell settings that a person hears or reads: the command line prints them and the window says them, so both
/// say the same. No sentence holds "|", an en dash, a numeric code or the words "read-only"; the
/// tool's names are written without the "|" of Dell's spelling, which a screen reader says aloud.
/// </summary>
public static class DellSentences
{
    /// <summary>The name of Dell Command | Monitor, as said.</summary>
    public const string SourceDcm = "Dell Command Monitor";

    /// <summary>The name of Dell Command | Configure, as said.</summary>
    public const string SourceCctk = "Dell Command Configure";

    /// <summary>The first part of the problem sentence.</summary>
    public const string UnavailableLead = "Dell BIOS settings cannot be read.";

    /// <summary>The cause when the process does not run on Windows (DF1).</summary>
    public const string NotWindowsCause = "They can be read on Windows only.";

    /// <summary>The cause when the manufacturer is not Dell (DF2); {0} is the manufacturer.</summary>
    public const string NotDellCauseFormat = "This computer's manufacturer is {0}, not Dell, so there are no Dell BIOS settings to show.";

    /// <summary>The cause when Dell Command | Monitor's namespace is absent (DF3).</summary>
    public const string DcmNotInstalled = "Dell Command Monitor is not installed.";

    /// <summary>The cause when the namespace is present and the enumeration class is not (DF4).</summary>
    public const string DcmClassMissing = "Dell Command Monitor is installed, but its BIOS settings class is missing.";

    /// <summary>The cause when the WMI provider did not load (DF5).</summary>
    public const string DcmProviderLoadFailure = "Dell Command Monitor is installed, but its WMI provider did not load.";

    /// <summary>The cause when Windows refused access (DF6).</summary>
    public const string DcmAccessDenied = "Windows refused access to Dell Command Monitor.";

    /// <summary>The cause when the query answered with no rows (DF7).</summary>
    public const string DcmNoInstances = "Dell Command Monitor answered with no BIOS settings.";

    /// <summary>The cause when Dell Command | Monitor did not answer in time (DF8); {0} is the number of seconds.</summary>
    public const string DcmTimedOutFormat = "Dell Command Monitor did not answer within {0} seconds.";

    /// <summary>The cause of any other WMI error (DF9).</summary>
    public const string DcmQueryFailed = "Dell Command Monitor could not be queried.";

    /// <summary>The cause when Dell Command | Configure was not found (DF14).</summary>
    public const string CctkNotFound = "Dell Command Configure was not found.";

    /// <summary>The cause when Dell Command | Configure needs administrator rights (DF15).</summary>
    public const string CctkNeedsAdministrator = "Dell Command Configure needs administrator rights.";

    /// <summary>The cause when Dell Command | Configure found no Dell BIOS interface (DF16).</summary>
    public const string CctkNoBiosInterface = "Dell Command Configure found no Dell BIOS interface on this computer.";

    /// <summary>The cause when a Dell Command | Configure run timed out (DF18); {0} is the number of seconds.</summary>
    public const string CctkTimedOutFormat = "Dell Command Configure did not answer within {0} seconds.";

    /// <summary>The cause when Dell Command | Configure stopped with an unexpected exit code or could not be started (DF20).</summary>
    public const string CctkRunFailed = "Dell Command Configure stopped with an error; the log has the details.";

    /// <summary>The cause when Dell Command | Configure's answer was not recognised (DF19).</summary>
    public const string CctkUnrecognisedOutput = "Dell Command Configure answered in a form this version does not recognise.";

    /// <summary>The cause when Dell Command | Configure offers none of the settings on this computer (DF17).</summary>
    public const string CctkOptionsNotSupported = "Dell Command Configure does not offer these settings on this computer.";

    /// <summary>The cause when the read budget ran out (DF21); {0} is the number of seconds.</summary>
    public const string BudgetExhaustedFormat = "Reading stopped at the time limit of {0} seconds.";

    /// <summary>The cause of an unexpected error (DF24).</summary>
    public const string UnexpectedCause = "An unexpected error occurred; the log has the details.";

    /// <summary>The clause when only the built-in Dell BIOS interface of newer models is present (DF22).</summary>
    public const string NativeInterfaceClause = "This computer has the built-in Dell BIOS interface of newer models, which this version does not read.";

    /// <summary>The advice when Dell Command | Monitor is not installed and the native interface is absent.</summary>
    public const string InstallAdvice = "Install the Microsoft Visual C++ 2015-2022 x64 runtime, then Dell Command Monitor, and try again; the Dell interface setup page of the documentation has the steps.";

    /// <summary>The advice when the WMI provider did not load.</summary>
    public const string RuntimeAdvice = "Install the Microsoft Visual C++ 2015-2022 x64 runtime, then try again.";

    /// <summary>The advice when the query answered with no rows.</summary>
    public const string ServiceAdvice = "Its data service, DSM SA Data Manager, is probably not running; restart Windows, then try again. This application never starts it.";

    /// <summary>The advice when access was refused or administrator rights are needed.</summary>
    public const string AdministratorAdvice = "Run it as administrator, then try again.";

    /// <summary>The advice when the enumeration class is missing.</summary>
    public const string RepairAdvice = "Repair or reinstall Dell Command Monitor, then try again.";

    /// <summary>The advice after a timeout, a query or run failure, the budget, an unrecognised answer or an unexpected error.</summary>
    public const string RetryAdvice = "Try again; if it fails again, restart Windows and try again.";

    /// <summary>The fallback sentence; {0} is Dell Command | Monitor's cause.</summary>
    public const string FallbackFormat = "{0} Dell Command Configure was used instead.";

    /// <summary>The warning when the BIOS is not the verified one (DF23); {0} the verified version, {1} this computer's.</summary>
    public const string OtherBiosFormat = "These names and values were checked against BIOS {0} only; this computer runs BIOS {1}.";

    /// <summary>The value words of a setting the interface did not report.</summary>
    public const string NotReportedValue = "not reported";

    /// <summary>The value words of a setting that was not read (the command line's; the window has its own).</summary>
    public const string NotReadValue = "not read";

    /// <summary>The value words of a setting reported without a current value.</summary>
    public const string NoValueReportedValue = "no value reported";

    /// <summary>The value words of a code the interface gives no name for; the code itself is never said.</summary>
    public const string UnnamedValue = "a value Dell does not name";

    /// <summary>The value words of a setting the BIOS setup hides; {0} the setting it depends on, {1} what that setting reads.</summary>
    public const string NotApplicableFormat = "not applicable while {0} is {1}";

    /// <summary>A row with possible values; {0} the name, {1} the value words, {2} the possible values.</summary>
    public const string RowFormat = "{0}: {1}. Possible values: {2}.";

    /// <summary>A row without possible values; {0} the name, {1} the value words.</summary>
    public const string RowWithoutPossibleFormat = "{0}: {1}.";

    /// <summary>What stands for the possible values that do not fit a row; {0} is how many.</summary>
    public const string MoreValuesFormat = "and {0} more";

    /// <summary>The note on Secure Boot when Dell Command | Monitor read it.</summary>
    public const string SecureBootDcmNote = "Dell Command Monitor lists only Enabled; the BIOS setup also offers Disabled.";

    /// <summary>The note on Secure Boot when Dell Command | Monitor reported no value.</summary>
    public const string SecureBootNoValueNote = "Dell Command Monitor reports no value while Secure Boot is off.";

    /// <summary>The note on a setting the fallback does not read (Boot list option).</summary>
    public const string NotReadByFallbackNote = "This version reads it through Dell Command Monitor only.";

    /// <summary>The most characters a row sentence holds (finding A2-7: a cut-off still carries the name).</summary>
    public const int RowSentenceLimit = 200;

    /// <summary>The name of a Dell interface, as said.</summary>
    /// <param name="source">The interface.</param>
    /// <returns><see cref="SourceDcm"/> or <see cref="SourceCctk"/>.</returns>
    public static string SourceName(DellSettingsSource source)
    {
        return source == DellSettingsSource.DellCommandConfigure ? SourceCctk : SourceDcm;
    }

    /// <summary>
    /// The words for a setting's current value, by its state: the value's text when it was read;
    /// "not applicable while TPM security is Disabled" when the setting the catalog says it depends
    /// on reads the other of its values (the clause names that setting and that value, from the
    /// catalog, so the reader marks a setting not applicable only while that setting reads exactly
    /// that value); and the fixed words of each other state. Never a code.
    /// </summary>
    /// <param name="setting">The setting.</param>
    /// <returns>The words.</returns>
    public static string ValueText(FirmwareSetting setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        return setting.State switch
        {
            SettingState.Read => setting.Current?.Text ?? NotReportedValue,
            SettingState.NotApplicable => NotApplicableText(setting),
            SettingState.NoValueReported => NoValueReportedValue,
            SettingState.Unnamed => UnnamedValue,
            SettingState.NotReported => NotReportedValue,
            _ => NotReadValue,
        };
    }

    /// <summary>
    /// The sentence of one row: the name first, then the value words, then the possible values,
    /// then the note after one space. A sentence longer than <see cref="RowSentenceLimit"/> loses
    /// possible values from the end, which <see cref="MoreValuesFormat"/> replaces, until it fits;
    /// one that still does not fit is cut at the limit, so it always starts with the name.
    /// </summary>
    /// <param name="displayName">The setting's name.</param>
    /// <param name="valueText">The words for its current value.</param>
    /// <param name="possibleTexts">The possible values' texts; empty for a row without them.</param>
    /// <param name="note">A sentence said after the row, or null.</param>
    /// <returns>The sentence, at most <see cref="RowSentenceLimit"/> characters.</returns>
    public static string RowSentence(string displayName, string valueText, IReadOnlyList<string> possibleTexts, string? note)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        ArgumentNullException.ThrowIfNull(valueText);
        ArgumentNullException.ThrowIfNull(possibleTexts);

        string[] possible = possibleTexts.Where(text => !string.IsNullOrEmpty(text)).ToArray();
        string Compose(int shown)
        {
            string body;
            if (possible.Length == 0)
            {
                body = Format(RowWithoutPossibleFormat, displayName, valueText);
            }
            else
            {
                IEnumerable<string> listed = possible.Take(shown);
                if (shown < possible.Length)
                {
                    listed = listed.Append(Format(MoreValuesFormat, possible.Length - shown));
                }

                body = Format(RowFormat, displayName, valueText, string.Join(", ", listed));
            }

            return string.IsNullOrEmpty(note) ? body : body + " " + note;
        }

        for (int shown = possible.Length; shown >= 0; shown--)
        {
            string sentence = Compose(shown);
            if (sentence.Length <= RowSentenceLimit)
            {
                return sentence;
            }
        }

        string longest = Compose(0);
        int cut = char.IsHighSurrogate(longest[RowSentenceLimit - 1]) ? RowSentenceLimit - 1 : RowSentenceLimit;
        return longest[..cut];
    }

    /// <summary>The sentence of one setting's row: <see cref="RowSentence(string, string, IReadOnlyList{string}, string)"/> with its name, <see cref="ValueText"/>, its possible values' texts and its note.</summary>
    /// <param name="setting">The setting.</param>
    /// <returns>The sentence, at most <see cref="RowSentenceLimit"/> characters.</returns>
    public static string RowSentence(FirmwareSetting setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        return RowSentence(setting.DisplayName, ValueText(setting), setting.PossibleValues.Select(value => value.Text).ToArray(), setting.Note);
    }

    /// <summary>
    /// The sentence that says what went wrong, for one problem. A timeout names the bound of the
    /// interface it happened in; the manufacturer of <see cref="DellProblemKind.NotDell"/> is the
    /// problem's detail.
    /// </summary>
    /// <param name="problem">The problem.</param>
    /// <returns>One sentence.</returns>
    public static string Cause(DellInterfaceProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        return problem.Kind switch
        {
            DellProblemKind.NotWindows => NotWindowsCause,
            DellProblemKind.NotDell => Format(NotDellCauseFormat, problem.Detail.Trim()),
            DellProblemKind.NotInstalled => DcmNotInstalled,
            DellProblemKind.ClassMissing => DcmClassMissing,
            DellProblemKind.ProviderLoadFailure => DcmProviderLoadFailure,
            DellProblemKind.AccessDenied => DcmAccessDenied,
            DellProblemKind.NoInstances => DcmNoInstances,
            DellProblemKind.TimedOut when problem.Interface == DellInterface.DellCommandConfigure => Format(CctkTimedOutFormat, Seconds(CctkSettingsProvider.RunTimeout)),
            DellProblemKind.TimedOut => Format(DcmTimedOutFormat, Seconds(DcmSettingsProvider.StepTimeout)),
            DellProblemKind.QueryFailed => DcmQueryFailed,
            DellProblemKind.ProgramNotFound => CctkNotFound,
            DellProblemKind.NeedsAdministrator => CctkNeedsAdministrator,
            DellProblemKind.NoBiosInterface => CctkNoBiosInterface,
            DellProblemKind.OptionNotSupported => CctkOptionsNotSupported,
            DellProblemKind.RunFailed => CctkRunFailed,
            DellProblemKind.UnrecognisedOutput => CctkUnrecognisedOutput,
            DellProblemKind.BudgetExhausted => Format(BudgetExhaustedFormat, Seconds(DellSettingsReader.ReadBudget)),
            DellProblemKind.NativeInterfaceOnly => NativeInterfaceClause,
            _ => UnexpectedCause,
        };
    }

    /// <summary>
    /// The problem sentence of a read that no interface answered; null
    /// unless the outcome is Unavailable. Its parts, joined with one space: the lead; then, when
    /// the reader or the machine stopped the read (not Windows, not a Dell), that cause, which ends
    /// the sentence; otherwise the cause of Dell Command | Monitor's first problem, the cause of
    /// the problem that stopped Dell Command | Configure and an unexpected error of the reader, in
    /// the order they were met; the budget sentence when the budget ran out; the native clause; and
    /// one piece of advice chosen by the first problem.
    /// </summary>
    /// <param name="result">The result of the read.</param>
    /// <returns>The sentence, or null when the read is usable.</returns>
    public static string? UnavailableSentence(DellSettingsResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Outcome != DellReadOutcome.Unavailable)
        {
            return null;
        }

        IReadOnlyList<DellInterfaceProblem> problems = result.Problems ?? Array.Empty<DellInterfaceProblem>();
        List<string> parts = new() { UnavailableLead };

        DellInterfaceProblem? stop = problems.FirstOrDefault(problem => problem.Kind is DellProblemKind.NotWindows or DellProblemKind.NotDell);
        if (stop is not null)
        {
            string? manufacturer = result.Machine?.Manufacturer;
            parts.Add(stop.Kind == DellProblemKind.NotDell && !string.IsNullOrWhiteSpace(manufacturer) ? Cause(stop with { Detail = manufacturer }) : Cause(stop));
            return string.Join(" ", parts);
        }

        DellInterfaceProblem? dcm = problems.FirstOrDefault(problem => problem.Interface == DellInterface.DellCommandMonitor && IsCause(problem.Kind));
        DellInterfaceProblem? cctk = problems.FirstOrDefault(problem => problem.Interface == DellInterface.DellCommandConfigure && StopsConfigure(problem.Kind))
            ?? problems.FirstOrDefault(problem => problem.Interface == DellInterface.DellCommandConfigure && IsCause(problem.Kind));
        IEnumerable<DellInterfaceProblem> own = problems.Where(problem => problem.Interface is DellInterface.Reader or DellInterface.Machine && IsCause(problem.Kind));
        foreach (DellInterfaceProblem problem in new[] { dcm, cctk }.OfType<DellInterfaceProblem>().Concat(own).Distinct().OrderBy(problem => IndexOf(problems, problem)))
        {
            AddOnce(parts, Cause(problem));
        }

        DellInterfaceProblem? budget = problems.FirstOrDefault(problem => problem.Kind == DellProblemKind.BudgetExhausted);
        if (budget is not null)
        {
            AddOnce(parts, Cause(budget));
        }

        bool native = result.NativeInterfacePresent || problems.Any(problem => problem.Kind == DellProblemKind.NativeInterfaceOnly);
        if (native)
        {
            AddOnce(parts, NativeInterfaceClause);
        }

        string? advice = problems.Count == 0 ? null : Advice(problems[0].Kind, native);
        if (advice is not null)
        {
            parts.Add(advice);
        }

        return string.Join(" ", parts);
    }

    /// <summary>The advice of the problem sentence, chosen by the kind of the first problem; null when there is none.</summary>
    private static string? Advice(DellProblemKind kind, bool nativeInterfacePresent)
    {
        return kind switch
        {
            DellProblemKind.NotInstalled => nativeInterfacePresent ? null : InstallAdvice,
            DellProblemKind.ProviderLoadFailure => RuntimeAdvice,
            DellProblemKind.NoInstances => ServiceAdvice,
            DellProblemKind.AccessDenied or DellProblemKind.NeedsAdministrator => AdministratorAdvice,
            DellProblemKind.ClassMissing => RepairAdvice,
            DellProblemKind.TimedOut or DellProblemKind.QueryFailed or DellProblemKind.BudgetExhausted or DellProblemKind.RunFailed
                or DellProblemKind.UnrecognisedOutput or DellProblemKind.Unexpected => RetryAdvice,
            _ => null,
        };
    }

    /// <summary>True for a kind whose cause stands in the causes of the problem sentence; the budget and the native interface have places of their own.</summary>
    private static bool IsCause(DellProblemKind kind) => kind is not (DellProblemKind.BudgetExhausted or DellProblemKind.NativeInterfaceOnly);

    /// <summary>True for a kind after which Dell Command | Configure runs no further option.</summary>
    private static bool StopsConfigure(DellProblemKind kind) => kind is DellProblemKind.ProgramNotFound or DellProblemKind.NeedsAdministrator
        or DellProblemKind.NoBiosInterface or DellProblemKind.TimedOut or DellProblemKind.RunFailed or DellProblemKind.Unexpected;

    /// <summary>
    /// The words of a setting the BIOS setup hides: the clause names the setting it depends on and
    /// the other of that setting's values (for TPM activation, TPM security and Disabled). Without
    /// a catalog entry that names exactly one other value, the value as read.
    /// </summary>
    private static string NotApplicableText(FirmwareSetting setting)
    {
        DellCatalogEntry? entry = DellAttributeCatalog.Find(setting.Id);
        DellCatalogEntry? dependsOn = entry?.DependsOnId is string id ? DellAttributeCatalog.Find(id) : null;
        if (entry is not null && dependsOn is not null)
        {
            string[] others = dependsOn.Values
                .Select(value => value.Text)
                .Where(text => !string.Equals(text, entry.DependsOnText, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (others.Length == 1)
            {
                return Format(NotApplicableFormat, dependsOn.DisplayName, others[0]);
            }
        }

        return setting.Current?.Text ?? NotReportedValue;
    }

    private static int IndexOf(IReadOnlyList<DellInterfaceProblem> problems, DellInterfaceProblem problem)
    {
        for (int i = 0; i < problems.Count; i++)
        {
            if (problems[i] == problem)
            {
                return i;
            }
        }

        return problems.Count;
    }

    private static void AddOnce(List<string> parts, string sentence)
    {
        if (!parts.Contains(sentence, StringComparer.Ordinal))
        {
            parts.Add(sentence);
        }
    }

    private static int Seconds(TimeSpan bound) => (int)Math.Round(bound.TotalSeconds);

    private static string Format(string format, params object[] arguments) => string.Format(CultureInfo.InvariantCulture, format, arguments);
}
