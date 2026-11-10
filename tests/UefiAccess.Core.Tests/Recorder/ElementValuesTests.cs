// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Text.RegularExpressions;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Presentation.Resources;
using UefiAccess.UiaTrace.Recording;
using Xunit;

namespace UefiAccess.Core.Tests.Recorder;

/// <summary>
/// The sentence= and copy= of the recorder's LiveRegionChanged lines (<see cref="ElementValues"/>).
/// LiveRegionChanged carries no text, and the recorder's name of the Status box is the one read
/// when the event arrived, which in M4-H4 (2 October 2026) was often a later sentence; and once the
/// Open baseline folder dialog had been opened, most events of the window (the Status box's among
/// them, not the focus events) came a second time, the copy without a value. The fixtures are the
/// details of UIA-RECV lines of that day's recorder logs (M4-H4's uia-trace.log and the two runs of
/// its ab2 check), cut to the lines that matter, with a baseline folder's path replaced by a
/// made-up one; they are replayed through ElementValues as the handlers pass them. Each
/// LiveRegionChanged then names the sentence its element held when the event was raised, the name
/// stays as received, every other line is written as before, a copy is marked and counted, and
/// another element's valueless change (the Writability box's id ends in StatusBox too) marks
/// nothing. Synthetic tags only.
/// </summary>
public sealed class ElementValuesTests
{
    private const int AppProcess = 4242;

    private const string Loading = "Loading variables, please wait.";

    private const string DellLoading = "Loading the Dell BIOS settings, please wait.";

    private const string Loaded78 = "Loaded 78 variables from Live firmware (NtEnumerate)";

    private const string Loaded13 = "Loaded 13 variables from Baseline (offline): C:\\Baselines\\sample-baseline (2026-09-15T12:00:00+00:00) (InMemory)";

    private const string Cancelled = "Cancelled; nothing changed.";

    /// <summary>
    /// M4-H4, NVDA part, uia-trace.log lines 7254-7273, then 7790, 7793 and 8064-8067: Open
    /// baseline folder with the sample baseline after the dialog had been used, then F6 to the
    /// detail pane. Every LiveRegionChanged was received with the name "Status: Detail region",
    /// the last of the three sentences; each sentence came once and once more as a copy.
    /// </summary>
    private static readonly string[] BaselineLoadThenDetail =
    {
        "PropertyChanged Value=\"" + Loading + "\" (was \"" + Loaded78 + "\") Edit id=StatusBox name=\"Status: Detail region\"",
        "LiveRegionChanged Edit id=StatusBox name=\"Status: Detail region\"",
        "PropertyChanged Value=(none) Edit id=StatusBox name=\"Status: Detail region\"",
        "LiveRegionChanged Edit id=StatusBox name=\"Status: Detail region\"",
        "FocusChanged DataGrid id=- name=\"EFI global variable rows\"",
        "PropertyChanged IsSelected=False (was True) ListItem id=- name=\"Boot0002, Windows Boot Manager\"",
        "ElementRemovedFromSelection ListItem id=- name=\"Boot0002, Windows Boot Manager\"",
        "PropertyChanged Value=(172 characters) (was (179 characters)) Edit id=DellSourceBox name=\"Dell settings source\"",
        "PropertyChanged Value=(20 characters) (was (85 characters)) Edit id=ReadOnlyStatusBox name=\"Writability\"",
        "PropertyChanged Value=\"" + Loaded13 + "\" (was \"" + Loading + "\") Edit id=StatusBox name=\"Status: Detail region\"",
        "LiveRegionChanged Edit id=StatusBox name=\"Status: Detail region\"",
        "PropertyChanged Value=(none) Edit id=DellSourceBox name=\"Dell settings source\"",
        "PropertyChanged Value=(none) Edit id=ReadOnlyStatusBox name=\"Writability\"",
        "PropertyChanged Value=(none) Edit id=StatusBox name=\"Status: Detail region\"",
        "LiveRegionChanged Edit id=StatusBox name=\"Status: Detail region\"",
        "PropertyChanged Value=(80 characters) (was (20 characters)) Edit id=ReadOnlyStatusBox name=\"Writability\"",
        "PropertyChanged Value=(none) Edit id=ReadOnlyStatusBox name=\"Writability\"",
        "PropertyChanged Value=\"Detail region\" (was \"" + Loaded13 + "\") Edit id=StatusBox name=\"Status: Detail region\"",
        "LiveRegionChanged Edit id=StatusBox name=\"Status: Detail region\"",
        "PropertyChanged Value=(none) Edit id=StatusBox name=\"Status: Detail region\"",
        "LiveRegionChanged Edit id=StatusBox name=\"Status: Detail region\"",
    };

    /// <summary>
    /// M4-H4, NVDA part, uia-trace.log lines 3805-3814: three different sentences, each raised and
    /// received once, all three received with the name of the last.
    /// </summary>
    private static readonly string[] ThreeSentencesReadAsTheLast =
    {
        "PropertyChanged Value=\"" + DellLoading + "\" (was \"" + Loaded78 + "\") Edit id=StatusBox name=\"Status: " + Loaded78 + "\"",
        "LiveRegionChanged Edit id=StatusBox name=\"Status: " + Loaded78 + "\"",
        "PropertyChanged Value=\"" + Loading + "\" (was \"" + DellLoading + "\") Edit id=StatusBox name=\"Status: " + Loaded78 + "\"",
        "LiveRegionChanged Edit id=StatusBox name=\"Status: " + Loaded78 + "\"",
        "FocusChanged DataGrid id=- name=\"EFI global variable rows\"",
        "PropertyChanged IsSelected=False (was True) ListItem id=- name=\"\"",
        "ElementRemovedFromSelection ListItem id=- name=\"\"",
        "ElementSelected ListItem id=- name=\"Boot0002, Windows Boot Manager\"",
        "PropertyChanged Value=\"" + Loaded78 + "\" (was \"" + Loading + "\") Edit id=StatusBox name=\"Status: " + Loaded78 + "\"",
        "LiveRegionChanged Edit id=StatusBox name=\"Status: " + Loaded78 + "\"",
    };

    /// <summary>
    /// The ab2 check, build m4-hw0, uia-trace.log lines 3684-3730 (cut): the events of the three
    /// F6 presses made before Alt+O arrived only after the dialog was cancelled, all read with the
    /// cancel sentence; the first copies came at the second F6 after it. The application's input
    /// trace raised six LiveRegionChanged here (Detail, Log, Dell settings, the cancel, Toolbar,
    /// Filter).
    /// </summary>
    private static readonly string[] AfterTheCancelledDialogOnM4 =
    {
        "PropertyChanged Value=\"Detail region\" (was \"" + Loaded78 + "\") Edit id=StatusBox name=\"Status: " + Cancelled + "\"",
        "LiveRegionChanged Edit id=StatusBox name=\"Status: " + Cancelled + "\"",
        "PropertyChanged Name=\"Status: Detail region\" (was \"Status: " + Loaded78 + "\") Edit id=StatusBox name=\"Status: " + Cancelled + "\"",
        "StructureChanged ChildAdded child=7.16632.37131542 ListItem id=- name=\"INFO 20:07:52: Detail region\"",
        "PropertyChanged Name=\"INFO 20:07:52: Detail region\" ListItem id=- name=\"INFO 20:07:52: Detail region\"",
        "PropertyChanged Value=\"Log region\" (was \"Detail region\") Edit id=StatusBox name=\"Status: " + Cancelled + "\"",
        "LiveRegionChanged Edit id=StatusBox name=\"Status: " + Cancelled + "\"",
        "PropertyChanged Name=\"Status: Log region\" (was \"Status: Detail region\") Edit id=StatusBox name=\"Status: " + Cancelled + "\"",
        "PropertyChanged Value=\"Dell settings region\" (was \"Log region\") Edit id=StatusBox name=\"Status: " + Cancelled + "\"",
        "LiveRegionChanged Edit id=StatusBox name=\"Status: " + Cancelled + "\"",
        "PropertyChanged Name=\"Status: Dell settings region\" (was \"Status: Log region\") Edit id=StatusBox name=\"Status: " + Cancelled + "\"",
        "FocusChanged DataItem id=- name=\"Boot list option: not loaded yet.\"",
        "PropertyChanged Value=\"" + Cancelled + "\" (was \"Dell settings region\") Edit id=StatusBox name=\"Status: " + Cancelled + "\"",
        "LiveRegionChanged Edit id=StatusBox name=\"Status: " + Cancelled + "\"",
        "PropertyChanged Name=\"Status: " + Cancelled + "\" (was \"Status: Dell settings region\") Edit id=StatusBox name=\"Status: " + Cancelled + "\"",
        "PropertyChanged Name=\"Status: " + Cancelled + "\" Edit id=StatusBox name=\"Status: " + Cancelled + "\"",
        "FocusChanged Edit id=StatusBox name=\"Status: " + Cancelled + "\"",
        "FocusChanged Button id=RefreshButton name=\"Refresh\"",
        "PropertyChanged Value=\"Toolbar region\" (was \"" + Cancelled + "\") Edit id=StatusBox name=\"Status: Toolbar region\"",
        "LiveRegionChanged Edit id=StatusBox name=\"Status: Toolbar region\"",
        "PropertyChanged Name=\"Status: Toolbar region\" (was \"Status: " + Cancelled + "\") Edit id=StatusBox name=\"Status: Toolbar region\"",
        "PropertyChanged Value=(none) Edit id=StatusBox name=\"Status: Toolbar region\"",
        "LiveRegionChanged Edit id=StatusBox name=\"Status: Toolbar region\"",
        "PropertyChanged Name=\"Status: Toolbar region\" Edit id=StatusBox name=\"Status: Toolbar region\"",
        "FocusChanged Edit id=FilterBox name=\"Filter variables\"",
        "PropertyChanged Value=\"Filter region\" (was \"Toolbar region\") Edit id=StatusBox name=\"Status: Filter region\"",
        "LiveRegionChanged Edit id=StatusBox name=\"Status: Filter region\"",
        "PropertyChanged Name=\"Status: Filter region\" (was \"Status: Toolbar region\") Edit id=StatusBox name=\"Status: Filter region\"",
        "PropertyChanged Value=(none) Edit id=StatusBox name=\"Status: Filter region\"",
        "LiveRegionChanged Edit id=StatusBox name=\"Status: Filter region\"",
        "PropertyChanged Name=\"Status: Filter region\" Edit id=StatusBox name=\"Status: Filter region\"",
    };

    /// <summary>
    /// The ab2 check, build main 330d69c (before M4), uia-trace.log lines 3716-3755 (cut): the
    /// first three F6 presses after the cancelled dialog; the Status box's events came twice, its
    /// name changes as well, the log panel's structure changes more than once, and the copied Value
    /// carried no value; the focus events came once.
    /// </summary>
    private static readonly string[] AfterTheCancelledDialogBeforeM4 =
    {
        "PropertyChanged Value=\"Toolbar region\" (was \"" + Cancelled + "\") Edit id=StatusBox name=\"Status: Toolbar region\"",
        "LiveRegionChanged Edit id=StatusBox name=\"Status: Toolbar region\"",
        "StructureChanged ChildAdded child=7.16216.19119237 ListItem id=- name=\"INFO 20:07:09: " + Cancelled + "\"",
        "PropertyChanged Name=\"Status: Toolbar region\" (was \"Status: " + Cancelled + "\") Edit id=StatusBox name=\"Status: Toolbar region\"",
        "PropertyChanged Value=(none) Edit id=StatusBox name=\"Status: Toolbar region\"",
        "LiveRegionChanged Edit id=StatusBox name=\"Status: Toolbar region\"",
        "StructureChanged ChildAdded child=7.16216.19119237 ListItem id=- name=\"INFO 20:07:09: " + Cancelled + "\"",
        "StructureChanged ChildAdded child=7.16216.19119237 ListItem id=- name=\"INFO 20:07:09: " + Cancelled + "\"",
        "PropertyChanged Name=\"Status: Toolbar region\" Edit id=StatusBox name=\"Status: Toolbar region\"",
        "FocusChanged Edit id=FilterBox name=\"Filter variables\"",
        "PropertyChanged Value=\"Filter region\" (was \"Toolbar region\") Edit id=StatusBox name=\"Status: Filter region\"",
        "LiveRegionChanged Edit id=StatusBox name=\"Status: Filter region\"",
        "PropertyChanged Name=\"Status: Filter region\" (was \"Status: Toolbar region\") Edit id=StatusBox name=\"Status: Filter region\"",
        "PropertyChanged Value=(none) Edit id=StatusBox name=\"Status: Filter region\"",
        "LiveRegionChanged Edit id=StatusBox name=\"Status: Filter region\"",
        "PropertyChanged Name=\"Status: Filter region\" Edit id=StatusBox name=\"Status: Filter region\"",
        "PropertyChanged Value=\"List region\" (was \"Filter region\") Edit id=StatusBox name=\"Status: List region\"",
        "LiveRegionChanged Edit id=StatusBox name=\"Status: List region\"",
        "PropertyChanged Name=\"Status: List region\" (was \"Status: Filter region\") Edit id=StatusBox name=\"Status: List region\"",
        "PropertyChanged Value=(none) Edit id=StatusBox name=\"Status: List region\"",
        "LiveRegionChanged Edit id=StatusBox name=\"Status: List region\"",
        "PropertyChanged Name=\"Status: List region\" Edit id=StatusBox name=\"Status: List region\"",
    };

    /// <summary>A PropertyChanged line: the property, its new value, the old one when given, and the element.</summary>
    private static readonly Regex PropertyLine = new(
        @"^PropertyChanged (?<property>\w+)=(?<new>""(?:[^""]|"""")*""|\([^()]*\)|\w+)(?: \(was (?<old>""(?:[^""]|"""")*""|\([^()]*\)|\w+)\))? (?<element>.+)$",
        RegexOptions.CultureInvariant);

    /// <summary>A LiveRegionChanged line as the recorder wrote it before sentence= and copy=.</summary>
    private static readonly Regex LiveLine = new(@"^LiveRegionChanged (?<element>.+)$", RegexOptions.CultureInvariant);

    /// <summary>The element at the end of a line: control type, automation id or "-", and the quoted name.</summary>
    private static readonly Regex ElementText = new(@"^(?<type>\w+) id=(?<id>\S+) name=""(?<name>(?:[^""]|"""")*)""$", RegexOptions.CultureInvariant);

    /// <summary>A value written as its length.</summary>
    private static readonly Regex LengthText = new(@"^\((?<length>\d+) characters?\)$", RegexOptions.CultureInvariant);

    [Fact]
    public void TheFixturesSentences_AreTheBuilds()
    {
        Assert.Equal(Strings.LoadingStatus, Loading);
        Assert.Equal(Strings.DellLoadingStatus, DellLoading);
        Assert.Equal(Strings.DialogCancelledStatus, Cancelled);
        Assert.Equal(Loaded78, string.Format(CultureInfo.InvariantCulture, Strings.LoadedFormat, "78 variables", "Live firmware", "NtEnumerate"));
        Assert.Equal(Loaded13, string.Format(CultureInfo.InvariantCulture, Strings.LoadedFormat, "13 variables", "Baseline (offline): C:\\Baselines\\sample-baseline (2026-09-15T12:00:00+00:00)", "InMemory"));
    }

    [Fact]
    public void BaselineLoadThenDetail_EachSentenceOnce_PlusOneCopy_UnderTheNameReadOnArrival()
    {
        Replayed replayed = Replay(BaselineLoadThenDetail);

        Assert.Equal(
            new[]
            {
                "LiveRegionChanged sentence=\"" + Loading + "\" Edit id=StatusBox name=\"Status: Detail region\"",
                "LiveRegionChanged sentence=\"" + Loading + "\" copy=yes Edit id=StatusBox name=\"Status: Detail region\"",
                "LiveRegionChanged sentence=\"" + Loaded13 + "\" Edit id=StatusBox name=\"Status: Detail region\"",
                "LiveRegionChanged sentence=\"" + Loaded13 + "\" copy=yes Edit id=StatusBox name=\"Status: Detail region\"",
                "LiveRegionChanged sentence=\"Detail region\" Edit id=StatusBox name=\"Status: Detail region\"",
                "LiveRegionChanged sentence=\"Detail region\" copy=yes Edit id=StatusBox name=\"Status: Detail region\"",
            },
            LiveLines(replayed.Lines));
        Assert.Equal(new[] { Loading, Loaded13, "Detail region" }, Sentences(replayed.Lines, copies: false));
        Assert.Equal(new[] { Loading, Loaded13, "Detail region" }, Sentences(replayed.Lines, copies: true));
        AssertTheOtherLinesAreWrittenAsBefore(BaselineLoadThenDetail, replayed.Lines);
        Assert.Equal(6, replayed.Counts.Get(UiaEventNames.LiveRegionChanged));
        Assert.Equal(3, replayed.Counts.Get(UiaEventNames.LiveRegionChangedCopies));
    }

    [Fact]
    public void ThreeSentencesReadAsTheLast_AreThreeDifferentSentences_OnceEach()
    {
        Replayed replayed = Replay(ThreeSentencesReadAsTheLast);

        Assert.Equal(
            new[]
            {
                "LiveRegionChanged sentence=\"" + DellLoading + "\" Edit id=StatusBox name=\"Status: " + Loaded78 + "\"",
                "LiveRegionChanged sentence=\"" + Loading + "\" Edit id=StatusBox name=\"Status: " + Loaded78 + "\"",
                "LiveRegionChanged sentence=\"" + Loaded78 + "\" Edit id=StatusBox name=\"Status: " + Loaded78 + "\"",
            },
            LiveLines(replayed.Lines));
        AssertTheOtherLinesAreWrittenAsBefore(ThreeSentencesReadAsTheLast, replayed.Lines);
        Assert.Equal(3, replayed.Counts.Get(UiaEventNames.LiveRegionChanged));
        Assert.Equal(0, replayed.Counts.Get(UiaEventNames.LiveRegionChangedCopies));
    }

    [Fact]
    public void AfterTheCancelledDialog_OnM4_TheCopiesAreMarked_AndTheRestIsWhatWasRaised()
    {
        Replayed replayed = Replay(AfterTheCancelledDialogOnM4);

        Assert.Equal(
            new[]
            {
                "LiveRegionChanged sentence=\"Detail region\" Edit id=StatusBox name=\"Status: " + Cancelled + "\"",
                "LiveRegionChanged sentence=\"Log region\" Edit id=StatusBox name=\"Status: " + Cancelled + "\"",
                "LiveRegionChanged sentence=\"Dell settings region\" Edit id=StatusBox name=\"Status: " + Cancelled + "\"",
                "LiveRegionChanged sentence=\"" + Cancelled + "\" Edit id=StatusBox name=\"Status: " + Cancelled + "\"",
                "LiveRegionChanged sentence=\"Toolbar region\" Edit id=StatusBox name=\"Status: Toolbar region\"",
                "LiveRegionChanged sentence=\"Toolbar region\" copy=yes Edit id=StatusBox name=\"Status: Toolbar region\"",
                "LiveRegionChanged sentence=\"Filter region\" Edit id=StatusBox name=\"Status: Filter region\"",
                "LiveRegionChanged sentence=\"Filter region\" copy=yes Edit id=StatusBox name=\"Status: Filter region\"",
            },
            LiveLines(replayed.Lines));
        AssertTheOtherLinesAreWrittenAsBefore(AfterTheCancelledDialogOnM4, replayed.Lines);

        // What was received, less the copies: the six the application raised, one per sentence.
        long received = replayed.Counts.Get(UiaEventNames.LiveRegionChanged);
        long copies = replayed.Counts.Get(UiaEventNames.LiveRegionChangedCopies);
        Assert.Equal(8, received);
        Assert.Equal(2, copies);
        Assert.Equal(6, received - copies);
        Assert.Equal(new[] { "Detail region", "Log region", "Dell settings region", Cancelled, "Toolbar region", "Filter region" }, Sentences(replayed.Lines, copies: false));
    }

    [Fact]
    public void AfterTheCancelledDialog_BeforeM4_EveryLiveRegionComesTwice_TheSecondMarked()
    {
        Replayed replayed = Replay(AfterTheCancelledDialogBeforeM4);

        Assert.Equal(
            new[]
            {
                "LiveRegionChanged sentence=\"Toolbar region\" Edit id=StatusBox name=\"Status: Toolbar region\"",
                "LiveRegionChanged sentence=\"Toolbar region\" copy=yes Edit id=StatusBox name=\"Status: Toolbar region\"",
                "LiveRegionChanged sentence=\"Filter region\" Edit id=StatusBox name=\"Status: Filter region\"",
                "LiveRegionChanged sentence=\"Filter region\" copy=yes Edit id=StatusBox name=\"Status: Filter region\"",
                "LiveRegionChanged sentence=\"List region\" Edit id=StatusBox name=\"Status: List region\"",
                "LiveRegionChanged sentence=\"List region\" copy=yes Edit id=StatusBox name=\"Status: List region\"",
            },
            LiveLines(replayed.Lines));
        AssertTheOtherLinesAreWrittenAsBefore(AfterTheCancelledDialogBeforeM4, replayed.Lines);
        Assert.Equal(3, replayed.Counts.Get(UiaEventNames.LiveRegionChangedCopies));
    }

    [Fact]
    public void AnotherElementsValuelessChange_DoesNotMarkTheStatusBox()
    {
        ElementValues values = new();
        ElementFacts writability = new("Edit", "ReadOnlyStatusBox", "Writability", AppProcess, true);
        ElementFacts scrollBar = new("ScrollBar", "VerticalScrollBar", string.Empty, AppProcess, true);

        values.PropertyChanged("Value", null, "Detail region", Status("Detail region"));
        values.LiveRegionChanged(Status("Detail region"), out bool first);
        values.PropertyChanged("Value", null, null, writability);
        values.PropertyChanged("Value", null, null, scrollBar);

        // The same sentence announced again changes no value, so only the live region event comes.
        string again = values.LiveRegionChanged(Status("Detail region"), out bool second);

        Assert.False(first);
        Assert.False(second);
        Assert.Equal("LiveRegionChanged sentence=\"Detail region\" Edit id=StatusBox name=\"Status: Detail region\"", again);
    }

    [Fact]
    public void AValuelessChangeThatNoLiveRegionFollows_MarksNoLaterEvent()
    {
        // The Status box has keyboard focus: UefiAccess raises no live region event for its new
        // text, so the copy is the valueless Value change and the name change, and nothing else.
        foreach (Action<ElementValues> between in new Action<ElementValues>[]
                 {
                     stream => stream.PropertyChanged("Name", null, "Status: " + Cancelled, Status(Cancelled)),
                     stream => stream.PropertyChanged("HasKeyboardFocus", null, false, Status(Cancelled)),
                     stream => stream.PropertyChanged("Value", null, Cancelled, Status(Cancelled)),
                 })
        {
            ElementValues values = new();
            values.PropertyChanged("Value", "Log region", Cancelled, Status(Cancelled));
            values.PropertyChanged("Name", "Status: Log region", "Status: " + Cancelled, Status(Cancelled));
            values.PropertyChanged("Value", null, null, Status(Cancelled));
            between(values);

            string again = values.LiveRegionChanged(Status(Cancelled), out bool copy);

            Assert.False(copy);
            Assert.Equal("LiveRegionChanged sentence=\"" + Cancelled + "\" Edit id=StatusBox name=\"Status: " + Cancelled + "\"", again);
        }

        // Straight after the valueless change it is a copy, and the mark is used up.
        ElementValues direct = new();
        direct.PropertyChanged("Value", null, "Toolbar region", Status("Toolbar region"));
        direct.PropertyChanged("Value", null, null, Status("Toolbar region"));
        Assert.Contains(" copy=yes ", direct.LiveRegionChanged(Status("Toolbar region"), out bool marked), StringComparison.Ordinal);
        Assert.True(marked);
        Assert.DoesNotContain("copy=", direct.LiveRegionChanged(Status("Toolbar region"), out bool next), StringComparison.Ordinal);
        Assert.False(next);
    }

    [Fact]
    public void WithoutAValueOfItsElement_TheSentenceIsUnknown()
    {
        ElementValues values = new();

        // Nothing received yet (or recorded without the properties family).
        Assert.Equal(
            "LiveRegionChanged sentence=(unknown) Edit id=StatusBox name=\"Status: Ready\"",
            values.LiveRegionChanged(Status("Ready"), out bool copy));
        Assert.False(copy);

        // A valueless copy before any value: a copy of a sentence the stream did not show.
        values.PropertyChanged("Value", null, null, Status("Ready"));
        Assert.Equal(
            "LiveRegionChanged sentence=(unknown) copy=yes Edit id=StatusBox name=\"Status: Ready\"",
            values.LiveRegionChanged(Status("Ready"), out copy));
        Assert.True(copy);

        // An element without an automation id is not followed, and an element that was gone has no facts.
        ElementFacts noId = new("Edit", null, "Status", AppProcess, true);
        values.PropertyChanged("Value", null, "Detail region", noId);
        values.PropertyChanged("Value", null, null, noId);
        Assert.Equal("LiveRegionChanged sentence=(unknown) Edit id=- name=\"Status\"", values.LiveRegionChanged(noId, out copy));
        Assert.False(copy);
        Assert.Equal("LiveRegionChanged sentence=(unknown) " + UiaEventText.UnavailableElement, values.LiveRegionChanged(ElementFacts.Unavailable, out copy));
        Assert.False(copy);
    }

    [Fact]
    public void TheElementsAreToldApartByProcessControlTypeAndAutomationId()
    {
        ElementValues values = new();
        values.PropertyChanged("Value", null, "Detail region", Status("Detail region"));
        values.PropertyChanged("Value", null, null, Status("Detail region"));

        ElementFacts otherProcess = new("Edit", "StatusBox", "Status", 777, true);
        ElementFacts otherType = new("Text", "StatusBox", "Status", AppProcess, true);

        Assert.Equal("LiveRegionChanged sentence=(unknown) Edit id=StatusBox name=\"Status\"", values.LiveRegionChanged(otherProcess, out bool first));
        Assert.Equal("LiveRegionChanged sentence=(unknown) Text id=StatusBox name=\"Status\"", values.LiveRegionChanged(otherType, out bool second));
        Assert.False(first);
        Assert.False(second);
        Assert.Equal(
            "LiveRegionChanged sentence=\"Detail region\" copy=yes Edit id=StatusBox name=\"Status: Detail region\"",
            values.LiveRegionChanged(Status("Detail region"), out bool own));
        Assert.True(own);
    }

    [Fact]
    public void TheSentence_IsTheValueAsItsPropertyChangedLineWroteIt()
    {
        ElementValues values = new();

        // The text after a "Service tag:" label is replaced, whatever the tag; inner quotes are doubled.
        string tagged = values.PropertyChanged("Value", null, "Identity of \"unit\": Service tag: OTHER99, model 7020", Status("x"));
        string live = values.LiveRegionChanged(Status("x"), out _);
        Assert.Equal("PropertyChanged Value=\"Identity of \"\"unit\"\": Service tag: [service tag], model 7020\" Edit id=StatusBox name=\"Status: x\"", tagged);
        Assert.Equal("LiveRegionChanged sentence=\"Identity of \"\"unit\"\": Service tag: [service tag], model 7020\" Edit id=StatusBox name=\"Status: x\"", live);
        Assert.DoesNotContain("OTHER99", live, StringComparison.Ordinal);

        // Cut at 200 characters, as the Value line is.
        values.PropertyChanged("Value", null, new string('s', 300), Status("x"));
        Assert.StartsWith("LiveRegionChanged sentence=\"" + new string('s', 199) + "\u2026\" Edit id=StatusBox", values.LiveRegionChanged(Status("x"), out _), StringComparison.Ordinal);

        // Any element but the four whose values are no firmware data: its length only.
        ElementFacts field = new("Edit", "DellSourceBox", "Dell settings source", AppProcess, true);
        Assert.Equal(
            "PropertyChanged Value=(172 characters) Edit id=DellSourceBox name=\"Dell settings source\"",
            values.PropertyChanged("Value", null, new string('d', 172), field));
        string fieldLive = values.LiveRegionChanged(field, out _);
        Assert.Equal("LiveRegionChanged sentence=(172 characters) Edit id=DellSourceBox name=\"Dell settings source\"", fieldLive);
        Assert.DoesNotContain("ddd", fieldLive, StringComparison.Ordinal);
    }

    [Fact]
    public void WrittenToTheTraceFile_ALiveRegionLineKeepsTheSixFields_AndTheTagIsReplacedInItsSentence()
    {
        ElementValues values = new();
        string folder = "Loaded 13 variables from Baseline (offline): C:\\Baselines\\testtag7 (2026-09-15T12:00:00+00:00) (InMemory)";
        values.PropertyChanged("Value", null, folder, Status("Detail region"));
        values.PropertyChanged("Value", null, null, Status("Detail region"));
        string details = values.LiveRegionChanged(Status("Detail region"), out _);

        StringWriter output = new();
        using (TraceFileWriter writer = new(output, TraceOutput.Title, () => "TESTTAG7", "0.1.0+test", 1_000, new DateTime(2026, 10, 2, 15, 12, 10, DateTimeKind.Local), AppProcess))
        {
            writer.Write(2_000, new DateTime(2026, 10, 2, 15, 12, 10, 664, DateTimeKind.Local), 8, TraceKind.UiaReceived, details);
        }

        string line = Assert.Single(output.ToString().Split('\n').Select(text => text.TrimEnd('\r')), text => text.Length > 0 && !text.StartsWith(TraceLine.CommentPrefix, StringComparison.Ordinal));
        string[] fields = line.Split(TraceLine.Separator);
        Assert.Equal(6, fields.Length);
        Assert.Equal(TraceKind.UiaReceived, fields[4]);
        Assert.Equal(
            "LiveRegionChanged sentence=\"Loaded 13 variables from Baseline (offline): C:\\Baselines\\" + TraceRedactor.Replacement + " (2026-09-15T12:00:00+00:00) (InMemory)\" copy=yes Edit id=StatusBox name=\"Status: Detail region\"",
            fields[5]);
        Assert.DoesNotContain("TESTTAG7", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromSeveralThreads_EachElementKeepsItsOwnSentence()
    {
        ElementValues values = new();
        const int Threads = 8;
        const int PerThread = 2000;
        int wrong = 0;

        Parallel.For(0, Threads, new ParallelOptions { MaxDegreeOfParallelism = Threads }, thread =>
        {
            ElementFacts box = new("Edit", "Box" + thread.ToString(CultureInfo.InvariantCulture), "Box", AppProcess, true);
            for (int i = 1; i <= PerThread; i++)
            {
                values.PropertyChanged("Value", null, new string('v', i), box);
                bool valueless = i % 2 == 0;
                if (valueless)
                {
                    values.PropertyChanged("Value", null, null, box);
                }

                string line = values.LiveRegionChanged(box, out bool copy);
                if (copy != valueless || !line.StartsWith("LiveRegionChanged sentence=(" + i.ToString(CultureInfo.InvariantCulture) + " character", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref wrong);
                }
            }
        });

        Assert.Equal(0, wrong);
    }

    [Fact]
    public void TheRecordersHandlers_PassTheCachedFactsThroughElementValues()
    {
        // The recorder itself needs the Windows Desktop runtime, so its source is read as text.
        string recorder = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "src", "UefiAccess.UiaTrace", "UiaRecorder.cs"));
        string property = MethodBody(recorder, "private void OnPropertyChanged(");
        string automation = MethodBody(recorder, "private void OnAutomationEvent(");

        Assert.Contains("private readonly ElementValues _values = new();", recorder, StringComparison.Ordinal);
        Assert.Contains("_values.PropertyChanged(property, e.OldValue, e.NewValue, facts)", property, StringComparison.Ordinal);
        Assert.DoesNotContain("UiaEventText.PropertyChanged(", property, StringComparison.Ordinal);
        Assert.Contains("_ when string.Equals(name, UiaEventNames.LiveRegionChanged, StringComparison.Ordinal) => _values.LiveRegionChanged(facts, out copy),", automation, StringComparison.Ordinal);
        Assert.Contains("_counts.AddLiveRegionCopy();", automation, StringComparison.Ordinal);
        Assert.True(
            automation.IndexOf("_writer.Write(", StringComparison.Ordinal) < automation.IndexOf("_counts.AddLiveRegionCopy();", StringComparison.Ordinal),
            "the copy is counted after its line is queued, as every event is");
    }

    /// <summary>
    /// Replays the details of UIA-RECV lines through a new <see cref="ElementValues"/> as the
    /// recorder's handlers pass them, and counts as they count: a PropertyChanged line as its
    /// property, old and new value and element, a LiveRegionChanged line as its element; any other
    /// line is written as it is, since its handler does not use ElementValues. A value is rebuilt
    /// from the line: quoted text as that text, (none) as null, "(12 characters)" as twelve
    /// characters, True and False as booleans.
    /// </summary>
    private static Replayed Replay(IEnumerable<string> lines)
    {
        ElementValues values = new();
        EventCounts counts = new();
        List<string> written = new();
        foreach (string line in lines)
        {
            Match property = PropertyLine.Match(line);
            if (property.Success)
            {
                string name = property.Groups["property"].Value;
                written.Add(values.PropertyChanged(name, ValueOf(property.Groups["old"]), ValueOf(property.Groups["new"]), ElementOf(property.Groups["element"].Value)));
                counts.AddPropertyChange(name);
                continue;
            }

            Match live = LiveLine.Match(line);
            if (live.Success)
            {
                written.Add(values.LiveRegionChanged(ElementOf(live.Groups["element"].Value), out bool copy));
                counts.Add(UiaEventNames.LiveRegionChanged);
                if (copy)
                {
                    counts.AddLiveRegionCopy();
                }

                continue;
            }

            written.Add(line);
        }

        return new Replayed(written, counts);
    }

    private static object? ValueOf(Group group)
    {
        if (!group.Success || group.Value == "(none)")
        {
            return null;
        }

        string text = group.Value;
        if (text.StartsWith('"'))
        {
            return text[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
        }

        Match length = LengthText.Match(text);
        if (length.Success)
        {
            return new string('x', int.Parse(length.Groups["length"].Value, CultureInfo.InvariantCulture));
        }

        return text switch
        {
            "True" => true,
            "False" => false,
            _ => text,
        };
    }

    private static ElementFacts ElementOf(string text)
    {
        Match element = ElementText.Match(text);
        Assert.True(element.Success, "not an element: " + text);
        string id = element.Groups["id"].Value;
        return new ElementFacts(element.Groups["type"].Value, id == "-" ? null : id, element.Groups["name"].Value.Replace("\"\"", "\"", StringComparison.Ordinal), AppProcess, true);
    }

    private static ElementFacts Status(string shown) => new("Edit", UiaEventText.StatusBoxId, "Status: " + shown, AppProcess, true);

    private static string[] LiveLines(IEnumerable<string> lines) =>
        lines.Where(line => line.StartsWith(UiaEventNames.LiveRegionChanged + " ", StringComparison.Ordinal)).ToArray();

    /// <summary>The sentences of the LiveRegionChanged lines, the copies or the others, in order.</summary>
    private static string[] Sentences(IEnumerable<string> lines, bool copies) =>
        LiveLines(lines)
            .Where(line => line.Contains(" copy=yes ", StringComparison.Ordinal) == copies)
            .Select(line => Regex.Match(line, @"^LiveRegionChanged sentence=""(?<sentence>(?:[^""]|"""")*)""").Groups["sentence"].Value.Replace("\"\"", "\"", StringComparison.Ordinal))
            .ToArray();

    /// <summary>Every line but the LiveRegionChanged ones is written exactly as the recorder wrote it before.</summary>
    private static void AssertTheOtherLinesAreWrittenAsBefore(IReadOnlyList<string> fixture, IReadOnlyList<string> written)
    {
        Assert.Equal(fixture.Count, written.Count);
        for (int i = 0; i < fixture.Count; i++)
        {
            if (LiveLine.IsMatch(fixture[i]))
            {
                Assert.EndsWith(fixture[i][(UiaEventNames.LiveRegionChanged.Length + 1)..], written[i], StringComparison.Ordinal);
                continue;
            }

            Assert.Equal(fixture[i], written[i]);
        }
    }

    /// <summary>The body of the first method whose declaration starts with <paramref name="signature"/>, from its "{" to the matching "}".</summary>
    private static string MethodBody(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, "method not found: " + signature);
        int open = source.IndexOf('{', start);
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}' && --depth == 0)
            {
                return source[open..(i + 1)];
            }
        }

        throw new InvalidOperationException("unbalanced braces after " + signature);
    }

    private sealed record Replayed(IReadOnlyList<string> Lines, EventCounts Counts);
}
