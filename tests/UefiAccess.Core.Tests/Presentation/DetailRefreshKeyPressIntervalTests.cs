// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Presentation.Accessibility;
using Xunit;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// Alpha 1 finding A1-6 checked against the tester's repro (context finding F019): separate quick arrow
/// presses, not only a held key (the developer took "quickly" as four to six presses a second,
/// 150 to 250 ms apart; neither of the tester's reports gives a pace). <see cref="CoalescingDetailScheduler"/>
/// has no delay of its own: a refresh runs when the host serves the post made as the key comes up.
/// A held, repeating key (no key-up, so no post, between the rows) costs one refresh after it is
/// released; separate presses whose posts the host serves between them cost one refresh each. That
/// is why alpha1.2, whose host served a post at the dispatcher's first idle turn, still rebuilt the
/// pane after every one of the tester's presses (Alpha 2 finding A2-7, the tester's A2-5): the WPF host now
/// serves a post only once a quiet period has passed with no key down in the list, so a burst is
/// served once, after its last key (QuietPeriodPosterTests). Enter still opens the details at once:
/// <see cref="IDetailScheduler.Flush"/> runs the pending refresh, held or not. These tests pin the
/// policy under both hosts: "RunPending" stands for the host serving the post.
/// </summary>
public sealed class DetailRefreshKeyPressIntervalTests
{
    [Fact]
    public void AHeldKey_CostsOneRefresh_AfterItIsReleased()
    {
        RecordingDetailScheduler scheduler = new();
        int shown = 0;

        scheduler.Policy.Hold();
        for (int row = 1; row <= 10; row++)
        {
            int current = row;
            scheduler.Schedule(() => shown = current);
        }

        Assert.False(scheduler.RunPending());
        scheduler.Policy.Release();
        Assert.True(scheduler.RunPending());

        Assert.Equal(1, scheduler.RefreshesRun);
        Assert.Equal(10, shown);
    }

    [Fact]
    public void SeparatePresses_WithAnIdleTurnBetweenThem_CostOneRefreshEach()
    {
        RecordingDetailScheduler scheduler = new();
        List<int> shown = new();

        for (int row = 1; row <= 3; row++)
        {
            int current = row;
            scheduler.Policy.Hold();
            scheduler.Schedule(() => shown.Add(current));
            scheduler.Policy.Release();

            // The idle gap between two presses: the dispatcher serves the post.
            scheduler.RunPending();
        }

        Assert.Equal(new[] { 1, 2, 3 }, shown);
        Assert.Equal(3, scheduler.RefreshesRun);
    }

    [Fact]
    public void SeparatePresses_WithoutAnIdleTurnBetweenThem_Coalesce()
    {
        RecordingDetailScheduler scheduler = new();
        List<int> shown = new();

        for (int row = 1; row <= 3; row++)
        {
            int current = row;
            scheduler.Policy.Hold();
            scheduler.Schedule(() => shown.Add(current));
            scheduler.Policy.Release();
        }

        scheduler.RunPending();

        Assert.Equal(new[] { 3 }, shown);
    }

    [Fact]
    public void Enter_RunsThePendingRefreshAtOnce_EvenWhileTheKeyIsDown()
    {
        RecordingDetailScheduler scheduler = new();
        int shown = 0;

        scheduler.Policy.Hold();
        scheduler.Schedule(() => shown = 7);
        scheduler.Flush();

        Assert.Equal(7, shown);
        Assert.False(scheduler.IsPending);
    }
}
