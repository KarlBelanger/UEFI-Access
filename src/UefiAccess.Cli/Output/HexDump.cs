// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Cli.Output;

/// <summary>
/// Formats bytes as a classic hex dump for <c>read --hex</c>. Since M2 the layout lives in
/// <see cref="Core.Text.HexDump"/> so the CLI and the GUI detail pane render raw data identically;
/// this class only keeps the CLI's call site stable.
/// </summary>
internal static class HexDump
{
    /// <summary>Renders the dump; every line ends with a newline. Empty input renders as an empty string.</summary>
    /// <param name="data">The bytes to render.</param>
    /// <returns>The dump text.</returns>
    public static string Render(ReadOnlySpan<byte> data) => global::UefiAccess.Core.Text.HexDump.Format(data);
}
