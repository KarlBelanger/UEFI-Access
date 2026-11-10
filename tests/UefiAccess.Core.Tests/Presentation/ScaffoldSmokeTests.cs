// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Cli.Services;
using UefiAccess.Core.Text;
using UefiAccess.Presentation.Accessibility;
using UefiAccess.Presentation.Infrastructure;
using UefiAccess.Presentation.Resources;
using UefiAccess.Presentation.Services;
using UefiAccess.Presentation.ViewModels;
using Xunit;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// Smoke tests for the M2 scaffold: the test host starts with the Presentation reference (which
/// must never drag WPF in), the refusal sentences match the CLI verbatim, the F6 region order
/// wraps, the recording announcer and focus sink record, the immediate and recording detail
/// schedulers run or hold a refresh as documented, the commands execute and report CanExecute,
/// and the shared hex dump renders the documented line format.
/// </summary>
public sealed class ScaffoldSmokeTests
{
    [Fact]
    public void TestHostRunsWithThePresentationReference()
    {
        // A test project that referenced the WPF App would build but fail to start the host on a
        // non-Windows machine; the mere execution of this fact proves the wiring is right.
        Assert.Equal("UefiAccess", Strings.WindowTitle);
        Assert.NotNull(typeof(ShellViewModel).Assembly.GetName().Name);
        Assert.Equal("UefiAccess.Presentation", typeof(ShellViewModel).Assembly.GetName().Name);
    }

    [Fact]
    public void PresentationDoesNotReferenceWpfAssemblies()
    {
        string[] referenced = typeof(ShellViewModel).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain("PresentationFramework", referenced);
        Assert.DoesNotContain("PresentationCore", referenced);
        Assert.DoesNotContain("WindowsBase", referenced);
        Assert.DoesNotContain("System.Xaml", referenced);
        Assert.Contains("UefiAccess.Core", referenced);
    }

    [Fact]
    public void RefusalSentencesMatchTheCliVerbatim()
    {
        Assert.Equal(Preconditions.NotUefiSentence, Strings.NotUefiSentence);
        Assert.Equal(Preconditions.NotElevatedSentence, Strings.NotElevatedSentence);
        Assert.Contains("SeSystemEnvironmentPrivilege", Strings.PrivilegeAbsentSentence, StringComparison.Ordinal);
        Assert.Contains("elevated", Strings.PrivilegeAbsentSentence, StringComparison.Ordinal);
    }

    [Fact]
    public void ButtonLabelsCarryUniqueAccessKeysAndNoWriteImplyingWords()
    {
        string[] labels =
        {
            Strings.RefreshLabel, Strings.OpenBaselineLabel, Strings.ClearFilterLabel,
            Strings.CopyValueLabel, Strings.ExportVisibleListLabel, Strings.CopyLogLabel,
            Strings.ReadDellSettingsLabel,
        };
        Assert.Equal("_Refresh", Strings.RefreshLabel);
        Assert.Equal("_Open baseline folder", Strings.OpenBaselineLabel);
        Assert.Equal("_Clear filter", Strings.ClearFilterLabel);
        Assert.Equal("Cop_y value", Strings.CopyValueLabel);
        Assert.Equal("E_xport visible list", Strings.ExportVisibleListLabel);
        Assert.Equal("Copy _log", Strings.CopyLogLabel);
        Assert.Equal("Read Dell _settings", Strings.ReadDellSettingsLabel);
        Assert.Equal("Read Dell settings", Strings.ReadDellSettingsName);
        Assert.Equal("Alt+S", Strings.ReadDellSettingsAccessKey);

        List<char> accessKeys = labels
            .Select(label => char.ToUpperInvariant(label[label.IndexOf('_', StringComparison.Ordinal) + 1]))
            .ToList();
        Assert.Equal(accessKeys.Count, accessKeys.Distinct().Count());
        Assert.Equal(new[] { 'R', 'O', 'C', 'Y', 'X', 'L', 'S' }, accessKeys);

        string[] forbidden = { "Apply", "Save", "Toggle", "Edit", "Write", "Set" };
        foreach (string label in labels)
        {
            foreach (string word in forbidden)
            {
                Assert.DoesNotContain(word, label, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void RegionOrder_NextAndPreviousWrap()
    {
        // Since M4 seven regions: Dell settings between Log and Status.
        Assert.Equal(new[] { Region.Toolbar, Region.Filter, Region.List, Region.Detail, Region.Log, Region.Dell, Region.Status }, RegionOrder.All);

        Assert.Equal(Region.Filter, RegionOrder.Next(Region.Toolbar));
        Assert.Equal(Region.List, RegionOrder.Next(Region.Filter));
        Assert.Equal(Region.Detail, RegionOrder.Next(Region.List));
        Assert.Equal(Region.Log, RegionOrder.Next(Region.Detail));
        Assert.Equal(Region.Dell, RegionOrder.Next(Region.Log));
        Assert.Equal(Region.Status, RegionOrder.Next(Region.Dell));
        Assert.Equal(Region.Toolbar, RegionOrder.Next(Region.Status));

        Assert.Equal(Region.Status, RegionOrder.Previous(Region.Toolbar));
        Assert.Equal(Region.Toolbar, RegionOrder.Previous(Region.Filter));
        Assert.Equal(Region.Dell, RegionOrder.Previous(Region.Status));
        Assert.Equal(Region.Log, RegionOrder.Previous(Region.Dell));

        foreach (Region region in RegionOrder.All)
        {
            Assert.Equal(region, RegionOrder.Previous(RegionOrder.Next(region)));
            Assert.False(string.IsNullOrWhiteSpace(RegionOrder.Name(region)));
        }

        Assert.Equal("Toolbar", RegionOrder.Name(Region.Toolbar));
        Assert.Equal("Status", RegionOrder.Name(Region.Status));
        Assert.Equal("Dell settings", RegionOrder.Name(Region.Dell));
        Assert.Equal("List region", string.Format(System.Globalization.CultureInfo.InvariantCulture, Strings.RegionArrivedFormat, RegionOrder.Name(Region.List)));
    }

    [Fact]
    public void RecordingAnnouncer_RecordsEveryAnnouncementInOrder()
    {
        RecordingAnnouncer announcer = new();
        Assert.Equal(string.Empty, announcer.Current);
        Assert.Empty(announcer.History);

        announcer.Announce("first");
        announcer.Announce("second");

        Assert.Equal("second", announcer.Current);
        Assert.Equal(new[] { "first", "second" }, announcer.History);

        announcer.Clear();
        Assert.Equal(string.Empty, announcer.Current);
        Assert.Empty(announcer.History);
    }

    [Fact]
    public void RecordingFocusSink_RecordsEveryRequestInOrder()
    {
        RecordingFocusSink sink = new();
        Assert.Empty(sink.Requests);
        Assert.Equal(FocusTarget.None, sink.Last);

        sink.RequestFocus(FocusTarget.ListFirstRow);
        sink.RequestFocus(FocusTarget.Status);

        Assert.Equal(new[] { FocusTarget.ListFirstRow, FocusTarget.Status }, sink.Requests);
        Assert.Equal(FocusTarget.Status, sink.Last);
    }

    [Fact]
    public void ImmediateDetailScheduler_RunsEveryRefreshAtOnce_AndFlushHasNothingToDo()
    {
        ImmediateDetailScheduler scheduler = new();
        List<int> ran = new();

        scheduler.Schedule(() => ran.Add(1));
        scheduler.Schedule(() => ran.Add(2));
        scheduler.Flush();

        Assert.Equal(new[] { 1, 2 }, ran);
        Assert.Throws<ArgumentNullException>(() => scheduler.Schedule(null!));
    }

    [Fact]
    public void RecordingDetailScheduler_KeepsTheNewestRefresh_UntilItIsRunOrFlushed()
    {
        RecordingDetailScheduler scheduler = new();
        List<int> ran = new();
        Assert.False(scheduler.IsPending);
        Assert.False(scheduler.RunPending());

        scheduler.Schedule(() => ran.Add(1));
        scheduler.Schedule(() => ran.Add(2));
        scheduler.Schedule(() => ran.Add(3));

        Assert.True(scheduler.IsPending);
        Assert.Equal(3, scheduler.ScheduleCalls);
        Assert.Empty(ran);

        Assert.True(scheduler.RunPending());
        Assert.Equal(new[] { 3 }, ran);
        Assert.False(scheduler.IsPending);
        Assert.Equal(1, scheduler.RefreshesRun);
        Assert.False(scheduler.RunPending());

        scheduler.Schedule(() => ran.Add(4));
        scheduler.Flush();
        Assert.Equal(new[] { 3, 4 }, ran);
        Assert.Equal(1, scheduler.FlushCalls);
        Assert.Equal(2, scheduler.RefreshesRun);

        scheduler.Flush();
        Assert.Equal(2, scheduler.FlushCalls);
        Assert.Equal(2, scheduler.RefreshesRun);

        scheduler.Schedule(() => ran.Add(5));
        scheduler.Clear();
        Assert.False(scheduler.IsPending);
        Assert.Equal(0, scheduler.ScheduleCalls);
        Assert.Equal(0, scheduler.FlushCalls);
        Assert.Equal(0, scheduler.RefreshesRun);
        Assert.False(scheduler.RunPending());
        Assert.Equal(new[] { 3, 4 }, ran);
        Assert.Throws<ArgumentNullException>(() => scheduler.Schedule(null!));
    }

    [Fact]
    public void RelayCommand_ExecutesAndReportsCanExecute()
    {
        int calls = 0;
        bool enabled = false;
        RelayCommand command = new(() => calls++, () => enabled);
        int raised = 0;
        command.CanExecuteChanged += (_, _) => raised++;

        Assert.False(command.CanExecute(null));
        enabled = true;
        Assert.True(command.CanExecute(null));
        command.Execute(null);
        Assert.Equal(1, calls);

        command.RaiseCanExecuteChanged();
        Assert.Equal(1, raised);

        object? received = null;
        RelayCommand parameterised = new(parameter => received = parameter, parameter => parameter is string);
        Assert.False(parameterised.CanExecute(42));
        Assert.True(parameterised.CanExecute("key"));
        parameterised.Execute("key");
        Assert.Equal("key", received);

        RelayCommand always = new(() => { });
        Assert.True(always.CanExecute(null));
    }

    [Fact]
    public async Task AsyncRelayCommand_RunsTheTaskAndIsDisabledMeanwhile()
    {
        TaskCompletionSource gate = new();
        AsyncRelayCommand command = new(() => gate.Task);
        List<bool> canExecuteAtRaise = new();
        command.CanExecuteChanged += (_, _) => canExecuteAtRaise.Add(command.CanExecute(null));

        Assert.False(command.IsRunning);
        Assert.True(command.CanExecute(null));

        Task running = command.ExecuteAsync();
        Assert.True(command.IsRunning);
        Assert.False(command.CanExecute(null));

        gate.SetResult();
        await running;

        Assert.False(command.IsRunning);
        Assert.True(command.CanExecute(null));
        Assert.Equal(new[] { false, true }, canExecuteAtRaise);
    }

    [Fact]
    public async Task AsyncRelayCommand_RoutesExceptionsToTheHandlerOrRethrows()
    {
        Exception? seen = null;
        AsyncRelayCommand handled = new(() => throw new InvalidOperationException("boom"), null, ex => seen = ex);
        await handled.ExecuteAsync();
        Assert.IsType<InvalidOperationException>(seen);
        Assert.False(handled.IsRunning);

        AsyncRelayCommand unhandled = new(() => Task.FromException(new InvalidOperationException("boom")));
        await Assert.ThrowsAsync<InvalidOperationException>(unhandled.ExecuteAsync);
        Assert.False(unhandled.IsRunning);

        AsyncRelayCommand disabled = new(() => Task.FromException(new InvalidOperationException("never")), () => false);
        await disabled.ExecuteAsync();
        Assert.False(disabled.IsRunning);
    }

    [Fact]
    public void HexDump_TwentyBytes_GiveTwoLinesInTheDocumentedFormat()
    {
        byte[] sample = new byte[20];
        for (int i = 0; i < sample.Length; i++)
        {
            sample[i] = (byte)(0x41 + i); // "ABCDEFGHIJKLMNOPQRST"
        }

        sample[0] = 0x00;
        sample[19] = 0xFF;

        IReadOnlyList<string> lines = HexDump.Lines(sample);

        Assert.Equal(2, lines.Count);
        Assert.Equal("00000000  00 42 43 44 45 46 47 48  49 4A 4B 4C 4D 4E 4F 50  .BCDEFGHIJKLMNOP", lines[0]);
        Assert.Equal("00000010  51 52 53 FF                                       QRS.", lines[1]);
        Assert.Equal(lines[0].IndexOf('.', StringComparison.Ordinal), lines[1].IndexOf('Q', StringComparison.Ordinal));

        string text = HexDump.Format(sample);
        Assert.Equal(lines[0] + Environment.NewLine + lines[1] + Environment.NewLine, text);
        Assert.Equal(string.Empty, HexDump.Format(ReadOnlySpan<byte>.Empty));
        Assert.Empty(HexDump.Lines(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void HexDump_MatchesTheCliRegexFromCliCommandTests()
    {
        byte[] bootOrder = { 0x00, 0x00, 0x03, 0x00, 0x01, 0x00, 0x02, 0x00, 0x05, 0x00 };

        // CliCommandTests captures the CLI's output through Normalize, which rewrites every line
        // ending to a bare line feed, and the regex below is copied from it character for
        // character. HexDump.Format ends each line with Environment.NewLine instead, so on Windows
        // a carriage return sits between the last ASCII dot and the line feed, and "$" in multiline
        // mode anchors only at the line feed. Normalising the text the same way CliCommandTests
        // does keeps the two assertions running the identical pattern, which is what this test is
        // for; loosening the pattern here would stop it proving that.
        string text = HexDump.Format(bootOrder).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Matches(@"(?m)^00000000  00 00 03 00 01 00 02 00  05 00 +\.{10}$", text);

        IReadOnlyList<string> eightPerLine = HexDump.Lines(bootOrder, 8);
        Assert.Equal(2, eightPerLine.Count);
        Assert.Equal("00000000  00 00 03 00  01 00 02 00  ........", eightPerLine[0]);
        Assert.Equal("00000008  05 00                     ..", eightPerLine[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => HexDump.Lines(bootOrder, 0));
    }

    [Fact]
    public void NullPlatformInteractions_CancelsDialogsAndRecordsTheClipboard()
    {
        NullPlatformInteractions platform = new();
        Assert.Null(platform.PickFolder("title"));
        Assert.Null(platform.PickSaveFile("title", "visible.txt", "Text|*.txt"));
        Assert.Null(platform.LastClipboardText);
        platform.SetClipboardText("copied");
        Assert.Equal("copied", platform.LastClipboardText);
    }

    [Fact]
    public void DetailFieldAndLogLine_ExposeTheirAccessibleNames()
    {
        DetailFieldViewModel field = new("GUID", "8be4df61-93ca-11d2-aa0d-00e098032b8c");
        Assert.Equal("GUID", field.AccessibleName);
        Assert.Equal("8be4df61-93ca-11d2-aa0d-00e098032b8c", field.Value);
        Assert.False(field.IsMultiline);

        DetailFieldViewModel longField = new("Raw", new string('x', 1000), isMultiline: true);
        Assert.Equal("Raw", longField.AccessibleName);
        Assert.True(longField.IsMultiline);

        DetailFieldViewModel longSingleLine = new("Raw", new string('x', 1000));
        Assert.Equal("Raw", longSingleLine.AccessibleName);
        Assert.Equal(1000, longSingleLine.Value.Length);
        Assert.False(longSingleLine.IsMultiline);

        DetailSectionViewModel section = new("Identity", new[] { field });
        Assert.Equal("Identity", section.Title);
        Assert.Single(section.Fields);

        // Serilog level names arrive from the application's log mirror and are spoken in the panel's vocabulary.
        LogLineViewModel line = new(new DateTimeOffset(2026, 9, 15, 13, 4, 5, TimeSpan.Zero), "Information", "Loaded 63 variables");
        Assert.Equal("13:04:05", line.Time);
        Assert.Equal("INFO 13:04:05: Loaded 63 variables", line.AccessibleName);

        LogViewModel log = new(capacity: 2);
        log.Add(line);
        log.Add("Warning", "second");
        log.Add("Error", "third");
        Assert.Equal(2, log.Lines.Count);
        Assert.Equal("second", log.Lines[0].Message);
        Assert.Equal("third", log.Lines[1].Message);
        Assert.Contains("WARN second", log.CopyText(), StringComparison.Ordinal);
        Assert.Equal(new[] { "WARN", "ERROR" }, log.Lines.Select(entry => entry.Level));
    }

    [Fact]
    public void ShellViewModelStub_ComposesWithoutThrowing()
    {
        RecordingAnnouncer announcer = new();
        RecordingFocusSink focus = new();
        NullPlatformInteractions platform = new();
        FakeSource live = new();

        ShellViewModel shell = new(live, folder => new FakeSource(), announcer, focus, platform);

        Assert.Equal(ShellState.Idle, shell.State);
        Assert.Equal("Idle", shell.StateText);
        Assert.Equal("UefiAccess", shell.WindowTitle);
        Assert.Equal("Live firmware", shell.SourceDescription);
        Assert.False(shell.IsOffline);
        Assert.False(shell.IsBusy);
        Assert.NotNull(shell.List);
        Assert.NotNull(shell.Detail);
        Assert.NotNull(shell.Log);
        Assert.True(shell.RefreshCommand.CanExecute(null));
        Assert.True(shell.OpenBaselineCommand.CanExecute(null));
        Assert.True(shell.ClearFilterCommand.CanExecute(null));
        Assert.True(shell.CopyValueCommand.CanExecute(null));
        Assert.True(shell.ExportVisibleListCommand.CanExecute(null));
        Assert.True(shell.CopyLogCommand.CanExecute(null));
        Assert.Equal("No variable selected", shell.Detail.Heading);
        Assert.Equal(Strings.NoSelectionHeading, shell.Detail.ReadOnlyStatus);
        Assert.StartsWith("Manufacturer: ", shell.Manufacturer, StringComparison.Ordinal);
        Assert.Equal("Showing 0 of 0 variables", shell.List.ShowingText);
    }

    private sealed class FakeSource : IVariableSource
    {
        public string Description => Strings.LiveSourceName;

        public bool IsOffline => false;

        public Task<SourceResult> LoadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new SourceResult(
                SourceStatus.Loaded,
                Description,
                IsOffline,
                Core.Platform.FirmwareType.Uefi,
                Core.Privileges.PrivilegeState.Enabled,
                Array.Empty<Core.Variables.FirmwareVariable>(),
                Core.Variables.EnumerationMethod.InMemory,
                Array.Empty<string>(),
                null,
                null));
        }
    }
}
