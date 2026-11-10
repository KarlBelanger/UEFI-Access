// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Settings.Dell;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The Dell settings reader's launcher of Dell Command | Configure.
/// Only off Windows can a test construct the real reader: there the two Program Files known
/// folders hold no cctk.exe, so nothing can start. On this PC, the OptiPlex 7020 itself, the
/// reader is never constructed (DellSettingsGuardTests.TestsNeverRunTheRealDellReader), so the
/// clamp of the bound and the fixed program path are pinned statically instead
/// (NoWriteGuardTests.CctkReader_RunsCctkOnlyThroughArgumentFor).
/// </summary>
public sealed class CctkReaderTests
{
    [NonWindowsFact]
    public void RunCctk_WithoutCctk_StartsNothing()
    {
        CctkReader reader = new(null);

        CctkRunOutcome outcome = reader.RunCctk(CctkReadOption.NumLock, TimeSpan.FromMinutes(5));

        Assert.False(reader.Found);
        Assert.Equal(CctkReadOption.NumLock, outcome.Option);
        Assert.Equal(CctkReader.NumLockArgument, outcome.Argument);
        Assert.False(outcome.Started);
        Assert.Null(outcome.ExitCode);
        Assert.False(outcome.TimedOut);
        Assert.Empty(outcome.StandardOutput);
        Assert.Empty(outcome.StandardError);
        Assert.False(string.IsNullOrEmpty(outcome.Error));
    }
}
