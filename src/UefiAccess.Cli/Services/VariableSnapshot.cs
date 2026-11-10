// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Variables;

namespace UefiAccess.Cli.Services;

/// <summary>Enumeration summary included in JSON documents.</summary>
/// <param name="Method">How the variables were obtained.</param>
/// <param name="EntriesParsed">Entries parsed from the native buffer (or read in the fallback).</param>
/// <param name="Count">Number of variables in the snapshot.</param>
/// <param name="Error">Enumeration error, or null.</param>
/// <param name="Warnings">Non-fatal enumeration warnings.</param>
internal sealed record EnumerationSummary(
    string Method,
    int EntriesParsed,
    int Count,
    string? Error,
    IReadOnlyList<string> Warnings);

/// <summary>
/// The variable store enumerated once for a command, as a sorted <see cref="VariableSet"/>, with
/// the exit-code interpretation of the enumeration result: failed (no variables and an error,
/// exit 5), partial (variables plus an error or warnings, exit 6) or complete (exit 0).
/// </summary>
internal sealed class VariableSnapshot
{
    private VariableSnapshot(EnumerationResult enumeration, VariableSet set)
    {
        Enumeration = enumeration;
        Set = set;
    }

    /// <summary>The raw enumeration result.</summary>
    public EnumerationResult Enumeration { get; }

    /// <summary>The variables, sorted by key.</summary>
    public VariableSet Set { get; }

    /// <summary>True when enumeration produced nothing and reported an error.</summary>
    public bool Failed => Set.Count == 0 && !string.IsNullOrEmpty(Enumeration.Error);

    /// <summary>True when variables were obtained but enumeration stopped early or some reads failed.</summary>
    public bool Partial => !Failed && (!string.IsNullOrEmpty(Enumeration.Error) || Enumeration.Warnings.Count > 0);

    /// <summary>The exit code the enumeration alone would justify: 5 failed, 6 partial, 0 complete.</summary>
    public int ExitCode => Failed ? ExitCodes.NativeFailure : Partial ? ExitCodes.Partial : ExitCodes.Success;

    /// <summary>
    /// True when the snapshot lists every runtime-visible variable: the native enumeration (or an
    /// in-memory store) ran to the end without an error or a warning. Only then does "not in the
    /// snapshot" mean "not visible at runtime"; otherwise a missing name has to be read directly.
    /// </summary>
    public bool Complete => !Failed && !Partial && Enumeration.Method is EnumerationMethod.NtEnumerate or EnumerationMethod.InMemory;

    /// <summary>Why the snapshot may miss variables, in words; null when <see cref="Complete"/>.</summary>
    public string? IncompleteReason
    {
        get
        {
            if (Complete)
            {
                return null;
            }

            if (Failed)
            {
                return "no variables could be enumerated";
            }

            if (Enumeration.Method == EnumerationMethod.KnownNamesFallback)
            {
                return "the known-names fallback lists only the curated names and Boot#### entries";
            }

            if (!string.IsNullOrEmpty(Enumeration.Error) || Enumeration.Warnings.Count > 0)
            {
                return "the enumeration stopped early or reported warnings";
            }

            return "no enumeration method listed the variables";
        }
    }

    /// <summary>
    /// Reads one variable directly through the store (GetFirmwareEnvironmentVariableExW, read-only),
    /// for a name an incomplete snapshot did not list. Never throws.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="name">Variable name.</param>
    /// <param name="vendorGuid">Vendor GUID.</param>
    /// <returns>The read result; a store that throws or returns nothing yields status Failed.</returns>
    public static ReadResult ReadDirect(CliServices services, string name, Guid vendorGuid)
    {
        ReadResult result;
        try
        {
            result = services.Store.Read(name, vendorGuid) ?? new ReadResult(null, ReadStatus.Failed, "Read returned no result", 0);
        }
        catch (Exception ex)
        {
            result = new ReadResult(null, ReadStatus.Failed, "Read threw " + ex.GetType().Name + ": " + ex.Message, 0);
        }

        services.Logger.Information("direct read of {Name} ({Guid}) for an incomplete enumeration: {Status} {Error}", name, vendorGuid.ToString("D"), result.Status, result.Error ?? string.Empty);
        return result;
    }

    /// <summary>Enumerates the store through <paramref name="services"/>. Never throws.</summary>
    /// <param name="services">The services.</param>
    /// <returns>The snapshot.</returns>
    public static VariableSnapshot Load(CliServices services)
    {
        EnumerationResult? result = null;
        try
        {
            result = services.Store.Enumerate();
        }
        catch (Exception ex)
        {
            services.Logger.Error(ex, "Enumerate threw");
            result = new EnumerationResult(Array.Empty<FirmwareVariable>(), EnumerationMethod.None, 0, "Enumerate threw " + ex.GetType().Name + ": " + ex.Message, Array.Empty<string>());
        }

        result ??= new EnumerationResult(Array.Empty<FirmwareVariable>(), EnumerationMethod.None, 0, "Enumerate returned no result", Array.Empty<string>());
        EnumerationResult normalized = result with
        {
            Variables = result.Variables ?? Array.Empty<FirmwareVariable>(),
            Warnings = result.Warnings ?? Array.Empty<string>(),
        };
        VariableSet set = new(normalized.Variables);
        services.Logger.Information(
            "Enumerated {Count} variables via {Method} ({EntriesParsed} entries parsed, {WarningCount} warnings){Error}",
            set.Count,
            normalized.Method,
            normalized.EntriesParsed,
            normalized.Warnings.Count,
            string.IsNullOrEmpty(normalized.Error) ? string.Empty : "; error: " + normalized.Error);
        return new VariableSnapshot(normalized, set);
    }

    /// <summary>Builds the summary for JSON documents.</summary>
    /// <returns>The summary.</returns>
    public EnumerationSummary ToSummary()
    {
        return new EnumerationSummary(Enumeration.Method.ToString(), Enumeration.EntriesParsed, Set.Count, Enumeration.Error, Enumeration.Warnings);
    }

    /// <summary>The message for a failed enumeration (no variables), for <see cref="CommandContext.Fail"/>.</summary>
    public string FailureMessage => "No variables could be enumerated: " + Format.Text(Enumeration.Error, "unknown error");

    /// <summary>
    /// Writes the partial-enumeration error and every warning to standard error. A failed
    /// enumeration is not reported here; the command reports it through <see cref="CommandContext.Fail"/>.
    /// </summary>
    /// <param name="context">The command context.</param>
    public void ReportPartial(CommandContext context)
    {
        if (Failed)
        {
            return;
        }

        if (!string.IsNullOrEmpty(Enumeration.Error))
        {
            context.Warn("enumeration incomplete: " + Enumeration.Error);
        }

        foreach (string warning in Enumeration.Warnings)
        {
            context.Warn(warning);
        }
    }
}
