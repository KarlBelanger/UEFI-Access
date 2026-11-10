// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.CommandLine;
using UefiAccess.Cli.Output;
using UefiAccess.Cli.Services;
using UefiAccess.Core.Variables;

namespace UefiAccess.Cli.Commands;

/// <summary>The JSON document of the read command.</summary>
/// <param name="Command">"read".</param>
/// <param name="Ok">True when the variable was read.</param>
/// <param name="Name">Requested name.</param>
/// <param name="Guid">Vendor GUID used (lowercase).</param>
/// <param name="Key">Variable key.</param>
/// <param name="Status">Read status name.</param>
/// <param name="Error">Error text when the read failed, or null.</param>
/// <param name="LastBufferSize">Last buffer size tried by the store.</param>
/// <param name="Attributes">Raw attribute value, or null when not read.</param>
/// <param name="Flags">Short flags, or null when not read.</param>
/// <param name="AttributeNames">Full attribute names, or null when not read.</param>
/// <param name="Size">Data size in bytes, or null when not read.</param>
/// <param name="DataBase64">The data as base64, or null when not read.</param>
/// <param name="Hex">Hex dump lines when --hex was given, or null.</param>
/// <param name="OutFile">Path written when --out was given, or null.</param>
/// <param name="BytesWritten">Bytes written to --out, or null.</param>
internal sealed record ReadDocument(
    string Command,
    bool Ok,
    string Name,
    string Guid,
    string Key,
    string Status,
    string? Error,
    int LastBufferSize,
    uint? Attributes,
    string? Flags,
    IReadOnlyList<string>? AttributeNames,
    int? Size,
    string? DataBase64,
    IReadOnlyList<string>? Hex,
    string? OutFile,
    int? BytesWritten);

/// <summary>"uefiaccess read": one variable with its attributes and size, as a hex dump or a raw file.</summary>
internal static class ReadCommand
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
        Argument<string> name = new("name")
        {
            Description = "Variable name, case-sensitive, for example BootOrder, Boot0001, SecureBoot or dbx.",
        };
        Option<string?> guid = CommonOptions.Guid("Vendor GUID. Default: the EFI global variable GUID, or the image security database GUID for db, dbx, dbt and dbr.");
        Option<bool> hex = new("--hex", "-x")
        {
            Description = "Print the data as a hex dump: 16 bytes per line with offsets and an ASCII column.",
        };
        Option<string?> outFile = new("--out", "-o")
        {
            Description = "Write the raw data bytes to this file.",
            HelpName = "file",
        };
        Command command = new("read", "Read one variable: attributes, size, and the data as a hex dump or a raw file.");
        command.Add(name);
        command.Add(json);
        command.Add(guid);
        command.Add(hex);
        command.Add(outFile);
        command.SetAction(parseResult => CommandRunner.Run(
            parseResult,
            verbose,
            json,
            "read",
            servicesFactory,
            context => Execute(
                context,
                parseResult.GetValue(name) ?? string.Empty,
                CommandRunner.GetString(parseResult, guid),
                CommandRunner.GetFlag(parseResult, hex),
                CommandRunner.GetString(parseResult, outFile))));
        return command;
    }

    private static int Execute(CommandContext context, string name, string? guidText, bool hex, string? outFile)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return context.Fail(ExitCodes.Usage, "A variable name is required.");
        }

        Guid vendorGuid;
        if (guidText is null)
        {
            vendorGuid = KnownGuids.DefaultGuidFor(name);
        }
        else if (!Format.TryParseGuid(guidText, out vendorGuid))
        {
            return context.Fail(ExitCodes.Usage, "The value '" + guidText + "' given to --guid is not a GUID.");
        }

        string key = FirmwareVariable.MakeKey(vendorGuid, name);
        ReadResult result;
        try
        {
            result = context.Services.Store.Read(name, vendorGuid) ?? new ReadResult(null, ReadStatus.Failed, "Read returned no result", 0);
        }
        catch (Exception ex)
        {
            result = new ReadResult(null, ReadStatus.Failed, "Read threw " + ex.GetType().Name + ": " + ex.Message, 0);
        }

        context.Logger.Information("read {Key}: {Status} {Error}", key, result.Status, result.Error ?? string.Empty);

        if (result.Status != ReadStatus.Success || result.Variable is null)
        {
            int code = result.Status switch
            {
                ReadStatus.PrivilegeNotHeld => ExitCodes.NotElevated,
                ReadStatus.NotUefi => ExitCodes.NotUefi,
                ReadStatus.NotSupportedOnThisOs => ExitCodes.NotUefi,
                _ => ExitCodes.NativeFailure,
            };
            string message = "Reading " + name + " (" + vendorGuid.ToString("D") + ") failed: " + result.Status + (string.IsNullOrEmpty(result.Error) ? string.Empty : ": " + result.Error);
            context.Error.WriteLine(message);
            if (context.Json)
            {
                context.WriteJson(new ReadDocument("read", false, name, vendorGuid.ToString("D"), key, result.Status.ToString(), result.Error, result.LastBufferSize, null, null, null, null, null, null, null, null));
            }

            return code;
        }

        FirmwareVariable variable = result.Variable;
        byte[] data = variable.Data ?? Array.Empty<byte>();
        int exitCode = ExitCodes.Success;
        int? bytesWritten = null;
        string? outPath = null;
        string? outError = null;
        if (!string.IsNullOrWhiteSpace(outFile))
        {
            try
            {
                outPath = Path.GetFullPath(outFile);
                File.WriteAllBytes(outPath, data);
                bytesWritten = data.Length;
                context.Logger.Information("wrote {Bytes} bytes of {Key} to {Path}", data.Length, key, outPath);
            }
            catch (Exception ex)
            {
                outError = "Writing '" + outFile + "' failed: " + ex.GetType().Name + ": " + ex.Message;
                context.Logger.Error(ex, "writing {Path} failed", outFile);
                exitCode = ExitCodes.NativeFailure;
            }
        }

        string? hexText = hex ? HexDump.Render(data) : null;

        if (context.Json)
        {
            IReadOnlyList<string>? hexLines = hexText is null ? null : hexText.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
            context.WriteJson(new ReadDocument(
                "read",
                outError is null,
                variable.Name,
                variable.VendorGuid.ToString("D"),
                variable.Key,
                result.Status.ToString(),
                outError,
                result.LastBufferSize,
                (uint)variable.Attributes,
                Format.ShortFlags(variable.Attributes),
                Format.AttributeNames(variable.Attributes),
                variable.Size,
                Convert.ToBase64String(data),
                hexLines,
                outPath,
                bytesWritten));
        }
        else
        {
            TableWriter table = new("Field", "Value");
            table.AddRow("Name", variable.Name);
            table.AddRow("GUID", variable.VendorGuid.ToString("D"));
            table.AddRow("Key", variable.Key);
            table.AddRow("Status", result.Status.ToString());
            table.AddRow("Attributes", Format.Attributes(variable.Attributes));
            table.AddRow("Attribute names", Format.Join(Format.AttributeNames(variable.Attributes)));
            table.AddRow("Size", Format.Bytes(variable.Size));
            table.Write(context.Out);

            if (bytesWritten is not null)
            {
                context.Out.WriteLine("Wrote " + Format.Bytes(bytesWritten.Value) + " to " + outPath + ".");
            }
            else if (outError is not null)
            {
                context.Out.WriteLine("The data could not be written to " + (outPath ?? outFile) + ".");
            }

            if (hexText is not null)
            {
                context.Out.WriteLine();
                context.Out.Write(data.Length == 0 ? "(no data)" + Environment.NewLine : hexText);
            }
            else if (string.IsNullOrWhiteSpace(outFile) && data.Length > 0)
            {
                context.Out.WriteLine("Use --hex to show the data or --out <file> to save it.");
            }
        }

        if (outError is not null)
        {
            context.Error.WriteLine(outError);
        }

        return exitCode;
    }
}
