// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Runtime.CompilerServices;

// The platform inspectors keep their pure logic (regular-expression checks, text parsing, PCR
// profile text) in internal static methods so the unit tests can exercise them without WMI,
// the registry, or child processes.
[assembly: InternalsVisibleTo("UefiAccess.Core.Tests")]
