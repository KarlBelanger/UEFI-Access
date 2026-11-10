// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using UefiAccess.Cli.Services;
using UefiAccess.Core.Platform;
using UefiAccess.Presentation.Services;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The built product assemblies the test project can load: UefiAccess.Core, the CLI (uefiaccess)
/// and UefiAccess.Presentation. The WPF App is not referenced (a test host that loads it needs the
/// Windows Desktop runtime); the text scans cover its sources instead. Used by the guard tests that
/// check what the compiler actually emitted: P/Invoke entry points and allow-listed constants.
/// </summary>
internal static class ProductAssemblies
{
    /// <summary>The three assemblies, Core first.</summary>
    public static IReadOnlyList<Assembly> All { get; } = new[]
    {
        typeof(BitLockerInspector).Assembly,
        typeof(CliPlatform).Assembly,
        typeof(LiveFirmwareSource).Assembly,
    };

    /// <summary>Every type of every product assembly, nested and compiler-generated types included.</summary>
    /// <returns>The types.</returns>
    public static IEnumerable<Type> Types() => All.SelectMany(assembly => assembly.GetTypes());
}
