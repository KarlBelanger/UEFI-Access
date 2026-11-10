// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The tests that replace <see cref="Console.Error"/>, which is process-wide
/// (<see cref="CliCtrlCHandlerTests"/>). They run on their own, after the parallel tests, so that
/// no other test writes to standard error while it is captured, and each puts it back.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CliConsoleErrorCollection
{
    /// <summary>The collection name used in [Collection(...)].</summary>
    public const string Name = "Console standard error";
}
