// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Serilog.Events;
using UefiAccess.Presentation.Accessibility;
using UefiAccess.Presentation.ViewModels;
using Xunit;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// Alpha 2 finding A2-8 (the tester's A2-6, "Raw values as the size or position changes"; expected "Some
/// usable value, size percentage, pixel coordinates") and the developer's A2-5 (Narrator silent
/// after a press), the shell's half; the splitter's half is
/// WpfShellAccessibilityTests.SplittersSayTheirPositionAsAReadOnlyValueSentence. After every arrow
/// press that moved a splitter, once at the end of a drag that changed its share, and after a press
/// at a limit, <see cref="ShellViewModel.OnSplitterMoved"/> says the position as a sentence that
/// names both panes through the one live region (the Status box, which NVDA and Narrator both
/// speak), once, with no focus request, and writes no INFO line to the log panel (only a Debug line
/// to the log file), so a resize of twenty presses does not fill the panel.
/// </summary>
public sealed class ShellSplitterAnnouncementTests
{
    [Fact]
    public void APressThatMovedTheListDetailSplitter_IsSaidOnce_ThroughStatus_AsASentence()
    {
        ShellTestRig rig = new();
        int before = rig.Announcer.History.Count;
        (string Level, string Message)[] lines = rig.Lines;
        List<string?> changed = new();
        rig.Shell.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        rig.Shell.OnSplitterMoved(SplitterKind.ListAndDetails, 64, atLimit: false);

        Assert.Equal(new[] { "List 64 percent of the width, details 36 percent." }, rig.Announcer.History.Skip(before));
        Assert.Equal("List 64 percent of the width, details 36 percent.", rig.Shell.StatusText);
        Assert.Equal(new[] { nameof(ShellViewModel.StatusText) }, changed);
        Assert.Empty(rig.Focus.Requests);
        Assert.Equal(lines, rig.Lines);
    }

    [Fact]
    public void APressThatMovedTheLogSplitter_NamesTheLogPanelAndTheListAndDetails()
    {
        ShellTestRig rig = new();

        rig.Shell.OnSplitterMoved(SplitterKind.LogPanel, 31, atLimit: false);

        Assert.Equal("Log panel 31 percent of the height, list and details 69 percent.", rig.Shell.StatusText);
    }

    [Fact]
    public void APressAtALimit_SaysLimitReached_BeforeThePosition()
    {
        ShellTestRig rig = new();
        int before = rig.Announcer.History.Count;

        rig.Shell.OnSplitterMoved(SplitterKind.ListAndDetails, 24, atLimit: true);
        rig.Shell.OnSplitterMoved(SplitterKind.ListAndDetails, 24, atLimit: true);
        rig.Shell.OnSplitterMoved(SplitterKind.LogPanel, 56, atLimit: true);

        Assert.Equal(
            new[]
            {
                "Limit reached: List 24 percent of the width, details 76 percent.",
                "Limit reached: List 24 percent of the width, details 76 percent.",
                "Limit reached: Log panel 56 percent of the height, list and details 44 percent.",
            },
            rig.Announcer.History.Skip(before));
    }

    [Fact]
    public void TwentyPresses_AreSaidOnceEach_WriteNoLineToTheLogPanel_AndAreLoggedAtDebugOnly()
    {
        ShellTestRig rig = new();
        int before = rig.Announcer.History.Count;
        (string Level, string Message)[] lines = rig.Lines;
        int events = rig.Events.Snapshot().Count;
        int mirrored = rig.Mirror.Mirrored;

        for (int share = 31; share <= 50; share++)
        {
            rig.Shell.OnSplitterMoved(SplitterKind.LogPanel, share, atLimit: false);
        }

        Assert.Equal(20, rig.Announcer.History.Count - before);
        Assert.Equal("Log panel 50 percent of the height, list and details 50 percent.", rig.Shell.StatusText);
        Assert.Equal(lines, rig.Lines);
        Assert.Equal(mirrored, rig.Mirror.Mirrored);
        LogEvent[] logged = rig.Events.Snapshot().Skip(events).ToArray();
        Assert.Equal(20, logged.Length);
        Assert.All(logged, logEvent => Assert.Equal(LogEventLevel.Debug, logEvent.Level));
        Assert.DoesNotContain(rig.Shell.Log.Lines, line => line.Message.Contains("percent of the height", StringComparison.Ordinal));
    }

    [Fact]
    public void TheSentence_IsNeverABareNumber()
    {
        ShellTestRig rig = new();

        foreach (SplitterKind kind in Enum.GetValues<SplitterKind>())
        {
            foreach (int share in new[] { 0, 1, 63, 99, 100 })
            {
                rig.Shell.OnSplitterMoved(kind, share, atLimit: false);
                Assert.DoesNotMatch(@"^\d+$", rig.Shell.StatusText);
                Assert.Contains(share + " percent", rig.Shell.StatusText, StringComparison.Ordinal);
                Assert.Contains(100 - share + " percent", rig.Shell.StatusText, StringComparison.Ordinal);
            }
        }
    }
}
