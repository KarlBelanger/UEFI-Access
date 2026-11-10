// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Buffers.Binary;
using System.CommandLine;
using System.Globalization;
using UefiAccess.Cli.Output;
using UefiAccess.Cli.Services;
using UefiAccess.Core.Decoders;
using UefiAccess.Core.Variables;

namespace UefiAccess.Cli.Commands;

/// <summary>A single boot option reference (BootCurrent or BootNext) resolved against the Boot#### entries.</summary>
/// <param name="Present">True when the variable exists, false when it does not, null when an incomplete enumeration did not list it and a direct read failed (<paramref name="Error"/> then says why).</param>
/// <param name="Index">The option number, or null when absent, unreadable or malformed.</param>
/// <param name="Name">"Boot####", or null when absent, unreadable or malformed.</param>
/// <param name="Description">Description of the referenced load option, or null when the entry is missing or unreadable.</param>
/// <param name="EntryExists">True when the referenced Boot#### variable exists.</param>
/// <param name="Error">Error text when the variable has an unexpected size; "could not be read: &lt;status&gt;" when the variable itself (<paramref name="Present"/> null) or the Boot#### it references (<paramref name="Note"/> "unreadable") could not be read; otherwise null.</param>
/// <param name="Note">"missing" when the referenced Boot#### does not exist, "unreadable" when the enumeration was incomplete and a direct read of it failed, otherwise null.</param>
internal sealed record BootReference(
    bool? Present,
    int? Index,
    string? Name,
    string? Description,
    bool EntryExists,
    string? Error,
    string? Note);

/// <summary>One position in BootOrder.</summary>
/// <param name="Position">1-based position.</param>
/// <param name="Index">The option number.</param>
/// <param name="Name">"Boot####".</param>
/// <param name="EntryExists">True when the Boot#### variable exists.</param>
/// <param name="Description">Description of the load option, or null when the entry is missing.</param>
/// <param name="Note">"missing" when the entry does not exist, "unreadable" when the enumeration was incomplete and a direct read of the entry failed, otherwise null.</param>
internal sealed record BootOrderEntry(
    int Position,
    int Index,
    string Name,
    bool EntryExists,
    string? Description,
    string? Note);

/// <summary>One decoded Boot#### variable.</summary>
/// <param name="Name">"Boot####".</param>
/// <param name="Index">The option number.</param>
/// <param name="Key">Variable key.</param>
/// <param name="Size">Variable size in bytes.</param>
/// <param name="VariableAttributes">Variable attribute flags.</param>
/// <param name="Attributes">Load option attributes (raw UINT32).</param>
/// <param name="Active">LOAD_OPTION_ACTIVE.</param>
/// <param name="ForceReconnect">LOAD_OPTION_FORCE_RECONNECT.</param>
/// <param name="Hidden">LOAD_OPTION_HIDDEN.</param>
/// <param name="Category">"Boot" or "App".</param>
/// <param name="Description">The description shown in the firmware boot menu.</param>
/// <param name="DevicePath">Device path text.</param>
/// <param name="DevicePathNodes">Device path node by node.</param>
/// <param name="OptionalDataLength">Optional data size in bytes.</param>
/// <param name="OptionalDataText">Optional data as text when printable, or null.</param>
/// <param name="InBootOrder">True when BootOrder references this entry.</param>
/// <param name="Error">Parse error, or null.</param>
internal sealed record BootEntry(
    string Name,
    int Index,
    string Key,
    int Size,
    string VariableAttributes,
    uint Attributes,
    bool Active,
    bool ForceReconnect,
    bool Hidden,
    string Category,
    string Description,
    string DevicePath,
    IReadOnlyList<string> DevicePathNodes,
    int OptionalDataLength,
    string? OptionalDataText,
    bool InBootOrder,
    string? Error);

/// <summary>The JSON document of the boot command.</summary>
/// <param name="Command">"boot".</param>
/// <param name="Ok">True when variables were enumerated.</param>
/// <param name="Enumeration">Enumeration summary.</param>
/// <param name="BootCurrent">BootCurrent resolved.</param>
/// <param name="BootNext">BootNext resolved.</param>
/// <param name="TimeoutPresent">True when Timeout exists, false when it does not, null when an incomplete enumeration did not list it and a direct read failed.</param>
/// <param name="TimeoutSeconds">Timeout in seconds, or null when absent, unreadable or malformed.</param>
/// <param name="TimeoutError">Error text when Timeout is malformed, "could not be read: &lt;status&gt;" when it could not be read, or null.</param>
/// <param name="BootOrderPresent">True when BootOrder exists, false when it does not, null when an incomplete enumeration did not list it and a direct read failed.</param>
/// <param name="BootOrderError">Error text when BootOrder is malformed, "could not be read: &lt;status&gt;" when it could not be read, or null.</param>
/// <param name="BootOrder">BootOrder positions resolved to descriptions.</param>
/// <param name="Entries">Every Boot#### entry, by index.</param>
/// <param name="Warnings">Observations such as missing entries or malformed options.</param>
internal sealed record BootDocument(
    string Command,
    bool Ok,
    EnumerationSummary Enumeration,
    BootReference BootCurrent,
    BootReference BootNext,
    bool? TimeoutPresent,
    int? TimeoutSeconds,
    string? TimeoutError,
    bool? BootOrderPresent,
    string? BootOrderError,
    IReadOnlyList<BootOrderEntry> BootOrder,
    IReadOnlyList<BootEntry> Entries,
    IReadOnlyList<string> Warnings);

/// <summary>
/// "uefiaccess boot": BootOrder resolved to descriptions, BootNext, BootCurrent, Timeout, then
/// every Boot#### load option with attributes, description, device path and optional data.
/// </summary>
internal static class BootCommand
{
    /// <summary>Builds the command over the production services.</summary>
    /// <param name="verbose">The global --verbose option.</param>
    /// <returns>The command.</returns>
    public static Command Create(Option<bool> verbose)
    {
        return Create(verbose, CliServices.Create);
    }

    /// <summary>Builds the command over the services from <paramref name="servicesFactory"/>.</summary>
    /// <param name="verbose">The global --verbose option.</param>
    /// <param name="servicesFactory">Creates the services for an invocation; receives the --verbose flag.</param>
    /// <returns>The command.</returns>
    public static Command Create(Option<bool> verbose, Func<bool, CliServices> servicesFactory)
    {
        Option<bool> json = CommonOptions.Json();
        Command command = new("boot", "Show BootOrder resolved to descriptions, BootNext, BootCurrent, Timeout, and every Boot#### load option decoded.");
        command.Add(json);
        command.SetAction(parseResult => CommandRunner.Run(parseResult, verbose, json, "boot", servicesFactory, Execute));
        return command;
    }

    private static int Execute(CommandContext context)
    {
        VariableSnapshot snapshot = VariableSnapshot.Load(context.Services);
        if (snapshot.Failed)
        {
            return context.Fail(ExitCodes.NativeFailure, snapshot.FailureMessage);
        }

        snapshot.ReportPartial(context);
        BootDocument document = Build(
            snapshot,
            snapshot.Complete ? null : name => VariableSnapshot.ReadDirect(context.Services, name, KnownGuids.EfiGlobalVariable));
        foreach (string warning in document.Warnings)
        {
            context.Warn(warning);
        }

        if (context.Json)
        {
            context.WriteJson(document);
        }
        else
        {
            WriteText(context.Out, document);
        }

        return snapshot.ExitCode;
    }

    /// <summary>Builds the boot report from a snapshot. Pure; never throws on malformed data.</summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <returns>The report.</returns>
    internal static BootDocument Build(VariableSnapshot snapshot)
    {
        return Build(snapshot, null);
    }

    /// <summary>
    /// Builds the boot report from a snapshot. When the snapshot is incomplete (the known-names
    /// fallback stops probing Boot#### after three consecutive misses, and an early stop loses
    /// everything after it), <paramref name="readDirect"/> reads BootOrder, BootCurrent, BootNext
    /// and Timeout when the snapshot does not list them, and then each Boot#### that BootOrder,
    /// BootCurrent or BootNext references but the snapshot does not list: found variables are
    /// used and decoded like the others, ERROR_ENVVAR_NOT_FOUND keeps a variable "not present" and
    /// a reference "missing", and any other failure reports the variable or the entry as "could
    /// not be read" with the status instead of claiming it does not exist. Never throws on
    /// malformed data.
    /// </summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <param name="readDirect">Reads one EFI global variable by name, read-only; null when the snapshot is complete.</param>
    /// <returns>The report.</returns>
    internal static BootDocument Build(VariableSnapshot snapshot, Func<string, ReadResult>? readDirect)
    {
        VariableSet set = snapshot.Set;
        Guid global = KnownGuids.EfiGlobalVariable;
        List<string> warnings = new();

        SortedDictionary<int, (FirmwareVariable Variable, LoadOption Option)> options = new();
        foreach (FirmwareVariable variable in set.All)
        {
            if (variable.VendorGuid == global && KnownVariableNames.IsBootEntryName(variable.Name, out int index))
            {
                options[index] = (variable, SafeInspect.LoadOption(variable.Data));
            }
        }

        FirmwareVariable? bootOrderVariable = Lookup(set, "BootOrder", readDirect, out string? bootOrderReadError);
        FirmwareVariable? bootCurrentVariable = Lookup(set, "BootCurrent", readDirect, out string? bootCurrentReadError);
        FirmwareVariable? bootNextVariable = Lookup(set, "BootNext", readDirect, out string? bootNextReadError);
        FirmwareVariable? timeout = Lookup(set, "Timeout", readDirect, out string? timeoutReadError);
        string? bootOrderError = null;
        IReadOnlyList<int> indices = bootOrderVariable is null ? Array.Empty<int>() : ReadUInt16Array(bootOrderVariable.Data, out bootOrderError);
        Dictionary<int, string> unreadable = readDirect is null
            ? new Dictionary<int, string>()
            : ReadUnlistedEntries(indices.Concat(ReferencedIndex(bootCurrentVariable)).Concat(ReferencedIndex(bootNextVariable)), options, readDirect);

        List<BootOrderEntry> order = new();
        HashSet<int> inOrder = new();
        if (bootOrderVariable is not null)
        {
            for (int i = 0; i < indices.Count; i++)
            {
                int index = indices[i];
                string name = KnownVariableNames.BootName(index);
                string position = (i + 1).ToString(CultureInfo.InvariantCulture);
                inOrder.Add(index);
                if (options.TryGetValue(index, out (FirmwareVariable Variable, LoadOption Option) entry))
                {
                    order.Add(new BootOrderEntry(i + 1, index, name, true, entry.Option.Description, null));
                }
                else if (unreadable.TryGetValue(index, out string? reason))
                {
                    order.Add(new BootOrderEntry(i + 1, index, name, false, null, "unreadable"));
                    warnings.Add("BootOrder position " + position + " references " + name + ", which could not be read: " + reason + ".");
                }
                else
                {
                    order.Add(new BootOrderEntry(i + 1, index, name, false, null, "missing"));
                    warnings.Add("BootOrder position " + position + " references " + name + ", which does not exist.");
                }
            }

            if (bootOrderError is not null)
            {
                warnings.Add("BootOrder: " + bootOrderError);
            }
        }
        else if (bootOrderReadError is not null)
        {
            bootOrderError = bootOrderReadError;
            warnings.Add("BootOrder " + bootOrderReadError + ".");
        }
        else
        {
            warnings.Add("BootOrder is not present.");
        }

        BootReference bootCurrent = Resolve(bootCurrentVariable, bootCurrentReadError, "BootCurrent", options, unreadable, warnings);
        BootReference bootNext = Resolve(bootNextVariable, bootNextReadError, "BootNext", options, unreadable, warnings);

        int? timeoutSeconds = null;
        string? timeoutError = null;
        if (timeout is not null)
        {
            byte[] data = timeout.Data ?? Array.Empty<byte>();
            if (data.Length == 2 && BootOrderDecoder.ReadTimeoutSeconds(data) is ushort seconds)
            {
                timeoutSeconds = seconds;
            }
            else
            {
                timeoutError = "Timeout has " + Format.Bytes(data.Length) + "; expected 2.";
                warnings.Add(timeoutError);
            }
        }
        else if (timeoutReadError is not null)
        {
            timeoutError = timeoutReadError;
            warnings.Add("Timeout " + timeoutReadError + ".");
        }

        List<BootEntry> entries = new();
        foreach (KeyValuePair<int, (FirmwareVariable Variable, LoadOption Option)> pair in options)
        {
            FirmwareVariable variable = pair.Value.Variable;
            LoadOption option = pair.Value.Option;
            if (option.Error is not null)
            {
                warnings.Add(variable.Name + ": " + option.Error);
            }

            entries.Add(new BootEntry(
                variable.Name,
                pair.Key,
                variable.Key,
                variable.Size,
                Format.Attributes(variable.Attributes),
                option.Attributes,
                option.Active,
                option.ForceReconnect,
                option.Hidden,
                option.Category ?? string.Empty,
                option.Description ?? string.Empty,
                option.DevicePath ?? string.Empty,
                option.DevicePathNodes ?? Array.Empty<string>(),
                option.OptionalDataLength,
                option.OptionalDataText,
                inOrder.Contains(pair.Key),
                option.Error));
        }

        return new BootDocument(
            "boot",
            true,
            snapshot.ToSummary(),
            bootCurrent,
            bootNext,
            PresenceOf(timeout, timeoutReadError),
            timeoutSeconds,
            timeoutError,
            PresenceOf(bootOrderVariable, bootOrderReadError),
            bootOrderError,
            order,
            entries,
            warnings);
    }

    /// <summary>
    /// Reads every referenced Boot#### the snapshot does not list: a found entry is added to
    /// <paramref name="options"/>; a failure other than "not found" is returned with its reason.
    /// </summary>
    private static Dictionary<int, string> ReadUnlistedEntries(
        IEnumerable<int> referenced,
        SortedDictionary<int, (FirmwareVariable Variable, LoadOption Option)> options,
        Func<string, ReadResult> readDirect)
    {
        Dictionary<int, string> unreadable = new();
        foreach (int index in referenced.Distinct().ToArray())
        {
            if (options.ContainsKey(index))
            {
                continue;
            }

            ReadResult read = readDirect(KnownVariableNames.BootName(index));
            if (read.Status == ReadStatus.Success && read.Variable is not null)
            {
                options[index] = (read.Variable, SafeInspect.LoadOption(read.Variable.Data));
            }
            else if (read.Status != ReadStatus.NotFound)
            {
                unreadable[index] = ReadFailure(read);
            }
        }

        return unreadable;
    }

    /// <summary>
    /// One EFI global variable from the snapshot or, when the snapshot is incomplete and does not
    /// list it, from a direct read. <paramref name="readError"/> is "could not be read: &lt;status&gt;"
    /// when that read failed for another reason than ERROR_ENVVAR_NOT_FOUND, otherwise null.
    /// </summary>
    private static FirmwareVariable? Lookup(VariableSet set, string name, Func<string, ReadResult>? readDirect, out string? readError)
    {
        readError = null;
        FirmwareVariable? variable = set.Find(name, KnownGuids.EfiGlobalVariable);
        if (variable is not null || readDirect is null)
        {
            return variable;
        }

        ReadResult read = readDirect(name);
        if (read.Status == ReadStatus.Success && read.Variable is not null)
        {
            return read.Variable;
        }

        if (read.Status != ReadStatus.NotFound)
        {
            readError = "could not be read: " + ReadFailure(read);
        }

        return null;
    }

    /// <summary>A failed read as "&lt;Status&gt;: &lt;error&gt;", for example "AccessDenied: ERROR_ACCESS_DENIED (5): access denied".</summary>
    private static string ReadFailure(ReadResult read)
    {
        return read.Status + (string.IsNullOrEmpty(read.Error) ? string.Empty : ": " + read.Error);
    }

    /// <summary>True when the variable exists, null when it could not be read, false when it does not exist.</summary>
    private static bool? PresenceOf(FirmwareVariable? variable, string? readError)
    {
        return variable is not null ? true : readError is null ? false : null;
    }

    /// <summary>The Boot#### index a two-byte BootCurrent or BootNext holds; nothing when absent or malformed.</summary>
    private static IEnumerable<int> ReferencedIndex(FirmwareVariable? variable)
    {
        byte[]? data = variable?.Data;
        return data is { Length: 2 } ? new[] { (int)BinaryPrimitives.ReadUInt16LittleEndian(data) } : Array.Empty<int>();
    }

    private static BootReference Resolve(
        FirmwareVariable? variable,
        string? readError,
        string name,
        SortedDictionary<int, (FirmwareVariable Variable, LoadOption Option)> options,
        Dictionary<int, string> unreadable,
        List<string> warnings)
    {
        if (variable is null)
        {
            if (readError is null)
            {
                return new BootReference(false, null, null, null, false, null, null);
            }

            warnings.Add(name + " " + readError + ".");
            return new BootReference(null, null, null, null, false, readError, null);
        }

        byte[] data = variable.Data ?? Array.Empty<byte>();
        if (data.Length != 2)
        {
            string error = name + " has " + Format.Bytes(data.Length) + "; expected 2.";
            warnings.Add(error);
            return new BootReference(true, null, null, null, false, error, null);
        }

        int index = BinaryPrimitives.ReadUInt16LittleEndian(data);
        string entryName = KnownVariableNames.BootName(index);
        if (options.TryGetValue(index, out (FirmwareVariable Variable, LoadOption Option) entry))
        {
            return new BootReference(true, index, entryName, entry.Option.Description, true, null, null);
        }

        if (unreadable.TryGetValue(index, out string? reason))
        {
            warnings.Add(name + " references " + entryName + ", which could not be read: " + reason + ".");
            return new BootReference(true, index, entryName, null, false, "could not be read: " + reason, "unreadable");
        }

        warnings.Add(name + " references " + entryName + ", which does not exist.");
        return new BootReference(true, index, entryName, null, false, null, "missing");
    }

    private static IReadOnlyList<int> ReadUInt16Array(byte[]? data, out string? error)
    {
        error = null;
        byte[] bytes = data ?? Array.Empty<byte>();
        List<int> values = new(bytes.Length / 2);
        for (int i = 0; i + 1 < bytes.Length; i += 2)
        {
            values.Add(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i, 2)));
        }

        if (bytes.Length % 2 != 0)
        {
            error = "length " + Format.Bytes(bytes.Length) + " is not a multiple of 2; the trailing byte was ignored.";
        }

        return values;
    }

    /// <summary>
    /// The summary value of BootCurrent or BootNext: the entry and its description, "(missing
    /// entry)" only when the entry does not exist, "(could not be read: &lt;status&gt;)" when the
    /// variable or its entry could not be read, "malformed: ..." for an unexpected size, or
    /// <paramref name="absentText"/> when the variable does not exist.
    /// </summary>
    private static string Describe(BootReference reference, string absentText)
    {
        if (reference.Present is null)
        {
            return "(" + Format.Text(reference.Error, "could not be read") + ")";
        }

        if (reference.Present == false)
        {
            return absentText;
        }

        string? name = reference.Name;
        if (name is null)
        {
            return "malformed: " + Format.Text(reference.Error, "unexpected size");
        }

        if (reference.EntryExists)
        {
            return name + " (" + Format.Text(reference.Description, "no description") + ")";
        }

        return reference.Note == "unreadable"
            ? name + " (" + Format.Text(reference.Error, "could not be read") + ")"
            : name + " (missing entry)";
    }

    /// <summary>The summary value of a variable that is absent, unreadable, malformed or holds <paramref name="value"/>.</summary>
    private static string DescribeVariable(bool? present, string? error, string? value)
    {
        return present switch
        {
            null => "(" + Format.Text(error, "could not be read") + ")",
            false => "(not present)",
            true => value ?? "malformed: " + Format.Text(error, "unexpected size"),
        };
    }

    private static void WriteText(TextWriter output, BootDocument document)
    {
        TableWriter summary = new("Field", "Value");
        summary.AddRow("BootCurrent", Describe(document.BootCurrent, "(not present)"));
        summary.AddRow("BootNext", Describe(document.BootNext, "(not set)"));
        summary.AddRow("Timeout", DescribeVariable(
            document.TimeoutPresent,
            document.TimeoutError,
            document.TimeoutSeconds is null ? null : document.TimeoutSeconds.Value.ToString(CultureInfo.InvariantCulture) + " seconds"));
        summary.AddRow("BootOrder", DescribeVariable(
            document.BootOrderPresent,
            document.BootOrderError,
            Format.Join(document.BootOrder.Select(e => e.EntryExists ? e.Name : e.Name + " (" + (e.Note ?? "missing") + ")"), "(empty)")));
        summary.AddRow("Boot entries", document.Entries.Count);
        summary.Write(output);

        output.WriteLine();
        output.WriteLine("Boot order:");
        if (document.BootOrder.Count == 0)
        {
            output.WriteLine(document.BootOrderPresent switch
            {
                true => "(empty)",
                false => "(BootOrder not present)",
                null => "(BootOrder could not be read)",
            });
        }
        else
        {
            TableWriter order = new("Position", "Entry", "Description");
            foreach (BootOrderEntry entry in document.BootOrder)
            {
                order.AddRow(
                    entry.Position,
                    entry.Name,
                    entry.EntryExists
                        ? Format.Text(entry.Description, "(no description)")
                        : entry.Note == "unreadable" ? "(unreadable: " + entry.Name + " could not be read)" : "(missing: no " + entry.Name + " variable)");
            }

            order.Write(output);
        }

        output.WriteLine();
        output.WriteLine("Boot entries:");
        if (document.Entries.Count == 0)
        {
            output.WriteLine("(none)");
        }

        foreach (BootEntry entry in document.Entries)
        {
            output.WriteLine();
            output.WriteLine(entry.Name + "  " + Format.Text(entry.Description, "(no description)"));
            List<string> flags = new();
            if (entry.Active)
            {
                flags.Add("Active");
            }

            if (entry.ForceReconnect)
            {
                flags.Add("ForceReconnect");
            }

            if (entry.Hidden)
            {
                flags.Add("Hidden");
            }

            output.WriteLine("  Key:                 " + entry.Key);
            output.WriteLine("  Variable attributes: " + entry.VariableAttributes + ", " + Format.Bytes(entry.Size));
            output.WriteLine("  Option attributes:   0x" + entry.Attributes.ToString("X8", CultureInfo.InvariantCulture) + " " + Format.Join(flags, "(none)") + ", category " + Format.Text(entry.Category, "(unknown)"));
            output.WriteLine("  In BootOrder:        " + (entry.InBootOrder ? "yes" : "no"));
            output.WriteLine("  Device path:         " + Format.Text(entry.DevicePath, "(none)"));
            string optional = entry.OptionalDataLength == 0
                ? "none"
                : Format.Bytes(entry.OptionalDataLength) + (entry.OptionalDataText is null ? " (binary)" : ", text \"" + entry.OptionalDataText + "\"");
            output.WriteLine("  Optional data:       " + optional);
            if (entry.Error is not null)
            {
                output.WriteLine("  Error:               " + entry.Error);
            }
        }
    }
}
