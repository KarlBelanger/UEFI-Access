// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Core.Settings.Dell;

/// <summary>
/// One Dell interface the settings reader can read the agreed list through: Dell Command | Monitor, Dell Command | Configure, and later, without a change to the view,
/// the command line or the catalog, a third. A provider only reads.
/// </summary>
internal interface IDellSettingsProvider
{
    /// <summary>The interface this provider reads.</summary>
    DellSettingsSource Source { get; }

    /// <summary>Reads the catalog's settings once, within the budget. Never throws: every failure is a problem of the read.</summary>
    /// <param name="catalog">The agreed settings, in the order they are shown.</param>
    /// <param name="budget">The time left of the whole read.</param>
    /// <param name="includeAllAttributes">True to keep every attribute the interface reports; a provider that cannot list them ignores it.</param>
    /// <returns>The read; its settings are always one per catalog entry, in catalog order.</returns>
    DellProviderRead Read(IReadOnlyList<DellCatalogEntry> catalog, DellReadBudget budget, bool includeAllAttributes);
}

/// <summary>What one provider read.</summary>
/// <param name="Answered">True when the interface gave a usable answer; the reader then uses this provider's settings and no other's.</param>
/// <param name="Settings">One setting per catalog entry, in catalog order; when the interface did not answer, none is <see cref="SettingState.Read"/>.</param>
/// <param name="Problems">The problems met, in the order met.</param>
/// <param name="AllAttributes">Every attribute the interface reported, when asked for and answered; otherwise null.</param>
internal sealed record DellProviderRead(
    bool Answered,
    IReadOnlyList<FirmwareSetting> Settings,
    IReadOnlyList<DellInterfaceProblem> Problems,
    IReadOnlyList<DellVendorAttribute>? AllAttributes);
