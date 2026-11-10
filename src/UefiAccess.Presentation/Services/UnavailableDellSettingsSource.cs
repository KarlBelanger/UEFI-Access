// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Settings;
using UefiAccess.Core.Settings.Dell;

namespace UefiAccess.Presentation.Services;

/// <summary>
/// The Dell settings source of a shell composed without one (the tests that do not read the Dell
/// settings): every read answers at once as unavailable, with the reader's
/// <see cref="DellProblemKind.Unexpected"/> problem and every agreed setting not read. It reads
/// nothing. The shell gives the same answer when a source's read throws.
/// </summary>
public sealed class UnavailableDellSettingsSource : IDellSettingsSource
{
    /// <summary>The problem's detail when no source was composed.</summary>
    internal const string NotComposedDetail = "no Dell settings source was composed";

    /// <inheritdoc />
    public Task<DellSettingsResult> ReadAsync() => Task.FromResult(Unavailable(NotComposedDetail));

    /// <summary>
    /// An unavailable read: the reader's <see cref="DellProblemKind.Unexpected"/> problem with the
    /// given detail, no machine fields, and one setting per agreed entry, each not read.
    /// </summary>
    /// <param name="detail">The problem's detail, for the log and the JSON; never said.</param>
    /// <returns>The result.</returns>
    internal static DellSettingsResult Unavailable(string detail)
    {
        FirmwareSetting[] settings = DellAttributeCatalog.Default
            .Select(entry => new FirmwareSetting(
                entry.Id,
                entry.DcmAttributeName,
                entry.DisplayName,
                entry.Category,
                entry.SetupLocation,
                SettingState.NotRead,
                null,
                Array.Empty<SettingValue>(),
                PossibleValuesOrigin.Catalog,
                null,
                false,
                null))
            .ToArray();
        return new DellSettingsResult(
            DellReadOutcome.Unavailable,
            null,
            DateTimeOffset.Now,
            0,
            new DellMachine(null, null, null),
            settings,
            new[] { new DellInterfaceProblem(DellInterface.Reader, DellProblemKind.Unexpected, detail ?? string.Empty) },
            Array.Empty<string>());
    }
}
