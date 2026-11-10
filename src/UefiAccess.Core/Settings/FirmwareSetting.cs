// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Core.Settings;

/// <summary>The state of one setting after a read.</summary>
public enum SettingState
{
    /// <summary>The interface reported a value and a name for it; <see cref="FirmwareSetting.Current"/> holds it.</summary>
    Read,

    /// <summary>
    /// A value was read, but the BIOS setup hides the setting while the setting it depends on reads
    /// otherwise (TPM activation while TPM security is not Enabled); the value is kept.
    /// </summary>
    NotApplicable,

    /// <summary>The interface reported the setting without a current value (Dell Command Monitor's Secure Boot while it is off).</summary>
    NoValueReported,

    /// <summary>The interface reported a value it gives no name for; the code is kept for the JSON and the log and is never spoken.</summary>
    Unnamed,

    /// <summary>The interface answered, but not for this setting.</summary>
    NotReported,

    /// <summary>Nothing was read for this setting: no interface answered, or the read stopped before it.</summary>
    NotRead,
}

/// <summary>Where a setting's possible values come from: the interface, or the catalog (the attribute map).</summary>
public enum PossibleValuesOrigin
{
    /// <summary>The interface listed them (Dell Command Monitor's PossibleValues and their names).</summary>
    Interface,

    /// <summary>The interface lists none, so they are the catalog's, as the attribute map records them for the verified BIOS.</summary>
    Catalog,
}

/// <summary>One value of a setting, in the interface's terms and in the words shown and spoken.</summary>
/// <param name="Token">The interface's own code or word: Dell Command Monitor's code "2", Dell Command Configure's word "Enabled".</param>
/// <param name="VendorName">The interface's name for it (Dell Command Monitor's PossibleValuesDescription); null when the interface gives none, as Dell Command Configure.</param>
/// <param name="Text">The words shown and spoken, in the vocabulary of the BIOS setup screen; never a code.</param>
public sealed record SettingValue(string Token, string? VendorName, string Text);

/// <summary>
/// One setting of the common model (plan task 4.2): name, display name, current value, possible
/// values, the vendor's read-only flag, category; plus where it lives in the BIOS setup and how it
/// was read. Nothing in this model can change a setting.
/// </summary>
/// <param name="Id">The catalog id, for example "NumLock".</param>
/// <param name="Name">The attribute name, for example "Num Lock".</param>
/// <param name="DisplayName">The name shown and spoken, for example "Num Lock at boot".</param>
/// <param name="Category">The BIOS setup page group, for example "POST Behavior".</param>
/// <param name="SetupLocation">Where the setting is in the BIOS setup, for example "POST Behavior, Numlock LED" (commas, never "&gt;").</param>
/// <param name="State">What the read found.</param>
/// <param name="Current">The current value; null for <see cref="SettingState.NoValueReported"/>, <see cref="SettingState.NotReported"/> and <see cref="SettingState.NotRead"/>.</param>
/// <param name="PossibleValues">The values the setting can take, in the interface's order; empty when none were read.</param>
/// <param name="PossibleValuesOrigin">Where <paramref name="PossibleValues"/> come from.</param>
/// <param name="VendorMarksReadOnly">The vendor's read-only flag as reported (Dell's IsReadOnly); null when the interface reports none. Never spoken (deviation D8).</param>
/// <param name="DisplayFromVendor">True when the current value's text is the vendor's own word because the catalog has no words for it.</param>
/// <param name="Note">One sentence said after the row, or null.</param>
public sealed record FirmwareSetting(
    string Id,
    string Name,
    string DisplayName,
    string Category,
    string SetupLocation,
    SettingState State,
    SettingValue? Current,
    IReadOnlyList<SettingValue> PossibleValues,
    PossibleValuesOrigin PossibleValuesOrigin,
    bool? VendorMarksReadOnly,
    bool DisplayFromVendor,
    string? Note);
