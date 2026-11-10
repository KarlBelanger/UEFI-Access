// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace UefiAccess.Core.Settings.Dell;

/// <summary>The Dell interface a result's settings were read through.</summary>
public enum DellSettingsSource
{
    /// <summary>Dell Command | Monitor's WMI classes in root\dcim\sysman.</summary>
    DellCommandMonitor,

    /// <summary>Dell Command | Configure's command line, the fallback.</summary>
    DellCommandConfigure,
}

/// <summary>Which Dell interface a read may use.</summary>
public enum DellSourcePreference
{
    /// <summary>Dell Command | Monitor, and Dell Command | Configure only when Dell Command | Monitor gives no answer.</summary>
    Automatic,

    /// <summary>Dell Command | Monitor only; never a Dell Command | Configure process.</summary>
    DellCommandMonitor,

    /// <summary>Dell Command | Configure only.</summary>
    DellCommandConfigure,
}

/// <summary>How much of the agreed list a read produced.</summary>
public enum DellReadOutcome
{
    /// <summary>Every agreed setting was read or is not applicable.</summary>
    Complete,

    /// <summary>An interface answered, but at least one agreed setting was not read.</summary>
    Partial,

    /// <summary>No interface answered.</summary>
    Unavailable,
}

/// <summary>Where a problem of a Dell read lies.</summary>
public enum DellInterface
{
    /// <summary>Dell Command | Monitor.</summary>
    DellCommandMonitor,

    /// <summary>Dell Command | Configure.</summary>
    DellCommandConfigure,

    /// <summary>The built-in Dell BIOS interface of newer models (root\dcim\sysman\biosattributes), which this version detects and never reads.</summary>
    NativeBiosInterface,

    /// <summary>The machine itself (not Dell).</summary>
    Machine,

    /// <summary>The reader (not Windows, the read budget, an unexpected error).</summary>
    Reader,
}

/// <summary>Why a Dell interface gave no answer, or no answer for a setting.</summary>
public enum DellProblemKind
{
    /// <summary>The process does not run on Windows (DF1).</summary>
    NotWindows,

    /// <summary>The manufacturer is known and is not Dell (DF2); the problem's detail holds the manufacturer.</summary>
    NotDell,

    /// <summary>Dell Command | Monitor's namespace is absent (DF3).</summary>
    NotInstalled,

    /// <summary>The namespace is present, the enumeration class is not (DF4).</summary>
    ClassMissing,

    /// <summary>Dell Command | Monitor's WMI provider did not load, the sign of a missing Visual C++ runtime (DF5).</summary>
    ProviderLoadFailure,

    /// <summary>Windows refused access (DF6).</summary>
    AccessDenied,

    /// <summary>The query answered with no rows, the sign of a stopped data service (DF7).</summary>
    NoInstances,

    /// <summary>The interface did not answer in time, or an earlier call of this process has still not finished (DF8, DF18).</summary>
    TimedOut,

    /// <summary>Any other WMI error (DF9).</summary>
    QueryFailed,

    /// <summary>Dell Command | Configure was not found (DF14).</summary>
    ProgramNotFound,

    /// <summary>Dell Command | Configure needs administrator rights (DF15).</summary>
    NeedsAdministrator,

    /// <summary>Dell Command | Configure found no Dell BIOS interface (DF16).</summary>
    NoBiosInterface,

    /// <summary>Dell Command | Configure does not offer an option on this computer (DF17).</summary>
    OptionNotSupported,

    /// <summary>Dell Command | Configure stopped with an unexpected exit code, or could not be started (DF20).</summary>
    RunFailed,

    /// <summary>Dell Command | Configure answered in a form this version does not recognise (DF19).</summary>
    UnrecognisedOutput,

    /// <summary>The read budget was used up before the step (DF21).</summary>
    BudgetExhausted,

    /// <summary>Only the built-in Dell BIOS interface of newer models is present, which this version does not read (DF22).</summary>
    NativeInterfaceOnly,

    /// <summary>An unexpected error, caught (DF24).</summary>
    Unexpected,
}

/// <summary>The machine a Dell read ran on: three fields, never a serial number or service tag.</summary>
/// <param name="Manufacturer">Win32_ComputerSystem.Manufacturer, or null when it could not be read.</param>
/// <param name="Model">Win32_ComputerSystem.Model, or null when it could not be read.</param>
/// <param name="BiosVersion">Win32_BIOS.SMBIOSBIOSVersion, or null when it could not be read.</param>
public sealed record DellMachine(string? Manufacturer, string? Model, string? BiosVersion);

/// <summary>One problem of a Dell read.</summary>
/// <param name="Interface">Where the problem lies.</param>
/// <param name="Kind">What went wrong.</param>
/// <param name="Detail">A one-line technical detail for the log and the JSON (the WMI error, the exit code); for <see cref="DellProblemKind.NotDell"/>, the manufacturer.</param>
public sealed record DellInterfaceProblem(DellInterface Interface, DellProblemKind Kind, string Detail);

/// <summary>
/// One attribute exactly as Dell Command | Monitor reports it, for the full enumeration
/// (<c>uefiaccess dell --all</c>, deviation D11): codes and Dell's names, no display text.
/// </summary>
/// <param name="Name">Dell's AttributeName.</param>
/// <param name="CurrentValues">The current value codes, blank elements kept as empty strings.</param>
/// <param name="CurrentNames">The current codes' names from PossibleValuesDescription, joined with ", "; null when a code has no name.</param>
/// <param name="PossibleValues">The possible value codes, blank elements kept as empty strings.</param>
/// <param name="PossibleNames">PossibleValuesDescription, index for index with <paramref name="PossibleValues"/>.</param>
/// <param name="IsReadOnly">Dell's IsReadOnly as reported; null when it is missing.</param>
public sealed record DellVendorAttribute(
    string Name,
    IReadOnlyList<string> CurrentValues,
    string? CurrentNames,
    IReadOnlyList<string> PossibleValues,
    IReadOnlyList<string> PossibleNames,
    bool? IsReadOnly);

/// <summary>What a Dell read may use and return.</summary>
/// <param name="Preference">Which interface the read may use.</param>
/// <param name="IncludeAllAttributes">True to keep every attribute Dell Command | Monitor reports as <see cref="DellSettingsResult.AllAttributes"/>.</param>
public sealed record DellReadOptions(DellSourcePreference Preference, bool IncludeAllAttributes)
{
    /// <summary>Automatic, without the full enumeration.</summary>
    public static DellReadOptions Default { get; } = new(DellSourcePreference.Automatic, false);
}

/// <summary>The result of one Dell read.</summary>
/// <param name="Outcome">Complete, Partial or Unavailable.</param>
/// <param name="Source">The interface that answered; null when the outcome is Unavailable.</param>
/// <param name="ReadAt">When the read started.</param>
/// <param name="ElapsedMs">How long the read took, in milliseconds.</param>
/// <param name="Machine">The machine's manufacturer, model and BIOS version.</param>
/// <param name="Settings">Always one setting per catalog entry, in catalog order, all from the one interface that answered.</param>
/// <param name="Problems">Every problem met, in the order met.</param>
/// <param name="Warnings">Sentences that qualify a usable result (another BIOS version than the verified one); never the fallback sentence.</param>
public sealed record DellSettingsResult(
    DellReadOutcome Outcome,
    DellSettingsSource? Source,
    DateTimeOffset ReadAt,
    long ElapsedMs,
    DellMachine Machine,
    IReadOnlyList<FirmwareSetting> Settings,
    IReadOnlyList<DellInterfaceProblem> Problems,
    IReadOnlyList<string> Warnings)
{
    /// <summary>When Dell Command | Configure answered after Dell Command | Monitor did not: Dell Command | Monitor's cause followed by the fallback clause; otherwise null.</summary>
    public string? FallbackSentence { get; init; }

    /// <summary>True when nothing answered, Dell Command | Monitor is not installed, and the built-in Dell BIOS interface of newer models is present (DF22).</summary>
    public bool NativeInterfacePresent { get; init; }

    /// <summary>Every attribute Dell Command | Monitor reported, when the options asked for them and it answered; otherwise null.</summary>
    public IReadOnlyList<DellVendorAttribute>? AllAttributes { get; init; }
}

/// <summary>Reads the agreed Dell BIOS settings. Reads only; never changes a setting.</summary>
public interface IDellSettingsReader
{
    /// <summary>Reads the agreed settings once, under the read budget. Never throws: every failure is a problem of the result.</summary>
    /// <param name="options">Which interface to use and whether to keep the full enumeration.</param>
    /// <returns>The result; its settings are always one per catalog entry.</returns>
    DellSettingsResult Read(DellReadOptions options);
}
