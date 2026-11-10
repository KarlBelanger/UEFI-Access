// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The tests that start Windows PowerShell (<see cref="RunGateOutputFolderTests"/>). They run on
/// their own, after the parallel tests, so that starting PowerShell does not take processor time
/// from the tests that measure short waits.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WindowsPowerShellCollection
{
    /// <summary>The collection name used in [Collection(...)].</summary>
    public const string Name = "Windows PowerShell";
}
