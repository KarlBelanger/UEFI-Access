// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;
using Serilog.Core;
using UefiAccess.Core.Diagnostics;
using UefiAccess.Core.Variables;
using UefiAccess.Presentation.Accessibility;
using UefiAccess.Presentation.Diagnostics;
using UefiAccess.Presentation.Resources;
using UefiAccess.Presentation.Services;
using UefiAccess.Presentation.ViewModels;
using Xunit;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// The shell's part of the input trace (UefiAccess.App --trace-input; Alpha 2 finding A2-7): off by
/// default and silent when off; when on, a SELECTION line for every change of the selected row,
/// written before the change is scheduled, a DETAIL line for every request to bring the detail pane
/// up to date, naming the method that asked (filled in by the compiler, so no call site changed),
/// and a pair of lines around every update of the pane (still called a rebuild in the trace) with
/// the row's variable name, the time it took, the sections the pane has, how many sections the
/// in-place update kept, added and removed and how many fields it kept, updated, added and removed
/// (fix 1 of the A16 measurement),
/// never a value; and a load's service tag registered for redaction before anything of the load
/// is traced. The shell runs over the recording detail scheduler, as the window's scheduler would
/// hold the update until the test serves it.
/// </summary>
public sealed class ShellInputTraceTests
{
    private const string Tag = "TESTTAG7";

    /// <summary>The counts the finished line ends with since the pane is updated in place (DetailPaneUpdate.ToString), numbers only.</summary>
    private const string UpdateCounts = @"sections \d+ kept, \d+ added, \d+ removed; fields \d+ kept, \d+ updated, \d+ added, \d+ removed";

    private static readonly FirmwareVariable[] Variables = { ShellTestRig.BootOrder, ShellTestRig.Timeout, ShellTestRig.Vendor };

    private static readonly Regex DetailLine = new(
        @"^(?:flush requested by \S.*|(?:direct )?rebuild (?:started: row (?:\S+|\(no row\))|finished in \d+\.\d ms: row (?:\S+|\(no row\)), \d+ sections; " + UpdateCounts + "))$",
        RegexOptions.CultureInvariant);

    private static readonly Regex SelectionLine = new(@"^shell: (?:row \S+|no row)$", RegexOptions.CultureInvariant);

    [Fact]
    public void TheDefaultTrace_IsTheNullOne_AndNullSetsItAgain()
    {
        Rig rig = new();
        Assert.Same(NullInputTrace.Instance, rig.Shell.InputTrace);

        RecordingInputTrace trace = new();
        rig.Shell.InputTrace = trace;
        Assert.Same(trace, rig.Shell.InputTrace);

        rig.Shell.InputTrace = null!;
        Assert.Same(NullInputTrace.Instance, rig.Shell.InputTrace);
    }

    [Fact]
    public async Task ATraceThatIsOff_GetsNoLine()
    {
        RecordingInputTrace trace = new(enabled: false);
        Rig rig = new(trace);

        await rig.LoadAsync(Tag);
        rig.Select(ShellTestRig.Timeout);
        Assert.True(rig.Scheduler.RunPending());
        rig.Select(ShellTestRig.BootOrder);
        rig.Shell.EnsureDetailCurrent();
        rig.Shell.CopyValueCommand.Execute(null);

        Assert.Empty(trace.Lines);
        Assert.Same(rig.Shell.List.SelectedItem, rig.Shell.Detail.Item);
    }

    [Fact]
    public void EnsureDetailCurrent_WhenTracing_NamesItsCaller()
    {
        RecordingInputTrace trace = new();
        Rig rig = new(trace);

        rig.Shell.EnsureDetailCurrent();

        Assert.Equal(new[] { (TraceKind.Detail, "flush requested by " + nameof(EnsureDetailCurrent_WhenTracing_NamesItsCaller)) }, trace.Lines);
    }

    [Fact]
    public async Task TheCallersTheTraceKnows_AreNamedWithWhatTheyStandFor()
    {
        RecordingInputTrace trace = new();
        Rig rig = new(trace);

        await rig.LoadAsync(Tag);
        Assert.Contains((TraceKind.Detail, "flush requested by ShowResult (a load finished)"), trace.Lines);

        trace.Clear();
        rig.Select(ShellTestRig.Timeout);
        rig.Shell.CopyValueCommand.Execute(null);
        Assert.Equal((TraceKind.Selection, "shell: row Timeout"), trace.Lines[0]);
        Assert.Equal((TraceKind.Detail, "flush requested by CopyValue (Copy value)"), trace.Lines[1]);

        // Export visible list (its save dialog cancelled by the null platform) flushes first too.
        trace.Clear();
        rig.Shell.ExportVisibleListCommand.Execute(null);
        Assert.Equal((TraceKind.Detail, "flush requested by ExportVisibleList (Export visible list)"), trace.Lines[0]);
    }

    [Fact]
    public async Task ASelectionChange_IsTracedBeforeItIsScheduled_AndBeforeTheRebuildItCauses()
    {
        RecordingInputTrace trace = new();
        Rig rig = new(trace);
        await rig.LoadAsync(Tag);
        trace.Clear();

        rig.Select(ShellTestRig.Timeout);

        // The scheduler holds the rebuild: the selection's line is the only one so far.
        Assert.Equal(new[] { (TraceKind.Selection, "shell: row Timeout") }, trace.Lines);
        Assert.True(rig.Scheduler.IsPending);

        Assert.True(rig.Scheduler.RunPending());
        Assert.Equal(new[] { TraceKind.Selection, TraceKind.Detail, TraceKind.Detail }, trace.Lines.Select(line => line.Kind));
        Assert.Equal("rebuild started: row Timeout", trace.Lines[1].Details);

        // A scheduler that looks at the trace when it is called finds the selection's line there:
        // the line is written before the scheduling, whose own DETAIL line (the window's
        // scheduler writes one) therefore follows it.
        RecordingInputTrace observed = new();
        ObservingScheduler scheduler = new(observed);
        GatedSource live = new(Strings.LiveSourceName, isOffline: false);
        ShellViewModel shell = new(live, _ => live, new RecordingAnnouncer(), new RecordingFocusSink(), new NullPlatformInteractions(), Logger.None, scheduler) { InputTrace = observed };
        live.Result = ShellTestRig.Loaded(Variables) with { Identity = LiveFirmwareSourceTests.Identity() with { ServiceTag = Tag } };
        await shell.RefreshAsync();
        observed.Clear();
        scheduler.LinesAtSchedule.Clear();

        shell.List.SelectedItem = shell.List.FindLoaded(ShellTestRig.Vendor.Key);

        Assert.Equal(new[] { 1 }, scheduler.LinesAtSchedule);
        Assert.Equal((TraceKind.Selection, "shell: row AcpiGlobalVariable"), observed.Lines[0]);
    }

    [Fact]
    public async Task AScheduledRefresh_IsTracedBeforeAndAfter_WithTheRowsNameOnly()
    {
        RecordingInputTrace trace = new();
        Rig rig = new(trace);
        await rig.LoadAsync(Tag);
        trace.Clear();
        Assert.Null(rig.Shell.Detail.Item);
        Assert.Empty(rig.Shell.Detail.Sections);

        rig.Select(ShellTestRig.Timeout);
        Assert.Equal(new[] { (TraceKind.Selection, "shell: row Timeout") }, trace.Lines);
        trace.Clear();
        Assert.True(rig.Scheduler.RunPending());

        // From the empty pane every section and field is added (the in-place update's counts end
        // the line since fix 1 of the A16 measurement).
        int sections = rig.Shell.Detail.Sections.Count;
        int fields = rig.Shell.Detail.Sections.Sum(section => section.Fields.Count);
        Assert.Equal(5, sections);
        Assert.Equal(2, trace.Lines.Count);
        Assert.Equal((TraceKind.Detail, "rebuild started: row Timeout"), trace.Lines[0]);
        Assert.Equal(TraceKind.Detail, trace.Lines[1].Kind);
        Assert.Matches(
            @"^rebuild finished in \d+\.\d ms: row Timeout, " + sections + " sections; sections 0 kept, " + sections + " added, 0 removed; fields 0 kept, 0 updated, " + fields + " added, 0 removed$",
            trace.Lines[1].Details);
        Assert.Matches(DetailLine, trace.Lines[1].Details);
        Assert.Same(rig.Shell.List.SelectedItem, rig.Shell.Detail.Item);

        // Another row of the same shape keeps every section and field: nothing added or removed.
        rig.Select(ShellTestRig.BootOrder);
        trace.Clear();
        Assert.True(rig.Scheduler.RunPending());
        Assert.Matches(@"^rebuild finished in \d+\.\d ms: row BootOrder, \d+ sections; sections \d+ kept, \d+ added, \d+ removed; fields \d+ kept, \d+ updated, \d+ added, \d+ removed$", trace.Lines[1].Details);
        Assert.Contains("; sections " + sections + " kept, 0 added, 0 removed; ", trace.Lines[1].Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARebuildWithNothingPending_IsTracedAsADirectRebuild()
    {
        RecordingInputTrace trace = new();
        Rig rig = new(trace);
        await rig.LoadAsync(Tag);
        rig.Select(ShellTestRig.Vendor);
        rig.Scheduler.Clear();
        trace.Clear();

        rig.Shell.EnsureDetailCurrent();

        Assert.Equal(3, trace.Lines.Count);
        Assert.Equal("direct rebuild started: row AcpiGlobalVariable", trace.Lines[1].Details);
        Assert.StartsWith("direct rebuild finished in ", trace.Lines[2].Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALoadsServiceTag_IsRegisteredBeforeAnythingOfTheLoadIsTraced()
    {
        RecordingInputTrace trace = new();
        Rig rig = new(trace);

        await rig.LoadAsync(Tag);
        rig.Select(ShellTestRig.BootOrder);
        rig.Scheduler.RunPending();
        rig.Shell.CopyValueCommand.Execute(null);

        Assert.Equal(new[] { Tag }, trace.Redacted);
        Assert.Equal("Redact " + Tag, trace.Calls[0]);
        Assert.All(trace.Lines, line => Assert.DoesNotContain(Tag, line.Details, StringComparison.OrdinalIgnoreCase));

        // A load without an identity (a refusal) registers nothing new.
        rig.Live.Result = ShellTestRig.Refused(Strings.NotElevatedSentence);
        await rig.Shell.RefreshAsync();
        Assert.Equal(new[] { Tag }, trace.Redacted);
    }

    [Fact]
    public async Task EveryDetailLine_HoldsOnlyTheCallerTheRowsNameATimeAndACount()
    {
        RecordingInputTrace trace = new();
        Rig rig = new(trace);
        await rig.LoadAsync(Tag);
        foreach (FirmwareVariable variable in Variables)
        {
            rig.Select(variable);
            rig.Scheduler.RunPending();
            rig.Shell.EnsureDetailCurrent();
        }

        rig.Shell.CopyValueCommand.Execute(null);

        Assert.NotEmpty(trace.Lines);
        Assert.Contains(trace.Lines, line => line.Kind == TraceKind.Selection);
        Assert.All(trace.Lines, line =>
        {
            Assert.Contains(line.Kind, new[] { TraceKind.Detail, TraceKind.Selection });
            Assert.Matches(line.Kind == TraceKind.Detail ? DetailLine : SelectionLine, line.Details);
        });
        Assert.All(trace.Lines, line => Assert.DoesNotContain("0x", line.Details, StringComparison.Ordinal));
    }

    /// <summary>A detail scheduler that notes how many lines the trace held each time it was asked to schedule, and runs the refresh at once.</summary>
    private sealed class ObservingScheduler : IDetailScheduler
    {
        private readonly RecordingInputTrace _trace;

        public ObservingScheduler(RecordingInputTrace trace)
        {
            _trace = trace;
        }

        public List<int> LinesAtSchedule { get; } = new();

        public void Schedule(Action refresh)
        {
            LinesAtSchedule.Add(_trace.Lines.Count);
            refresh();
        }

        public void Flush()
        {
        }
    }

    /// <summary>The shell over recording services and the recording detail scheduler, with a trace when one is given.</summary>
    private sealed class Rig
    {
        public Rig(IInputTrace? trace = null)
        {
            Shell = new ShellViewModel(
                Live,
                _ => Live,
                Announcer,
                Focus,
                new NullPlatformInteractions(),
                Logger.None,
                Scheduler);
            if (trace is not null)
            {
                Shell.InputTrace = trace;
            }
        }

        public RecordingDetailScheduler Scheduler { get; } = new();

        public RecordingAnnouncer Announcer { get; } = new();

        public RecordingFocusSink Focus { get; } = new();

        public GatedSource Live { get; } = new(Strings.LiveSourceName, isOffline: false);

        public ShellViewModel Shell { get; }

        public Task LoadAsync(string serviceTag)
        {
            Live.Result = ShellTestRig.Loaded(Variables) with { Identity = LiveFirmwareSourceTests.Identity() with { ServiceTag = serviceTag } };
            return Shell.RefreshAsync();
        }

        public void Select(FirmwareVariable variable)
        {
            Shell.List.SelectedItem = Shell.List.FindLoaded(variable.Key) ?? throw new InvalidOperationException(variable.Key + " is not loaded");
        }
    }
}
