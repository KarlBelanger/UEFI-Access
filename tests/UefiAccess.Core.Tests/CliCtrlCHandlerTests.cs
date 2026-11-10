// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Reflection;
using System.Text;
using UefiAccess.Cli.Commands;
using Xunit;

namespace UefiAccess.Core.Tests;

/// <summary>
/// The CLI's Ctrl+C handler of a commit (the CLI ignores Ctrl+C from
/// S12 to S17), raised directly with the arguments the runtime passes for Ctrl+C. The handler
/// writes to <see cref="Console.Error"/>, which is process-wide, so these tests run in
/// <see cref="CliConsoleErrorCollection"/>, alone and after the parallel tests, and put standard
/// error back in every case. That the commit subscribes this handler to Console.CancelKeyPress
/// before the write and removes it in its finally block is pinned by
/// <see cref="CliWriteCommandTests.CommitIgnoringCtrlC_SubscribesTheHandlerBeforeTheCommit_AndRemovesItInFinally"/>.
/// </summary>
[Collection(CliConsoleErrorCollection.Name)]
public sealed class CliCtrlCHandlerTests
{
    [Fact]
    public void IgnoreCancelKey_CancelsCtrlC_AndSaysWhyOnStandardError()
    {
        ConsoleCancelEventArgs args = CtrlC();
        using StringWriter error = new(CultureInfo.InvariantCulture);

        WithStandardError(error, () => WriteCommand.IgnoreCancelKey(null, args));

        Assert.True(args.Cancel);
        Assert.Equal(WriteCommand.CtrlCIgnored + Environment.NewLine, error.ToString());
    }

    [Fact]
    public void IgnoreCancelKey_StillCancelsCtrlC_WhenStandardErrorCannotBeWritten()
    {
        ConsoleCancelEventArgs args = CtrlC();
        using FailingWriter error = new();

        // An exception out of the handler would end the process on the runtime's signal thread,
        // between the suspend and the write or between the write and S17.
        WithStandardError(error, () => WriteCommand.IgnoreCancelKey(null, args));

        Assert.True(error.WriteAttempted);
        Assert.True(args.Cancel);
    }

    [Fact]
    public void IgnoreCancelKey_KeepsCtrlCIgnored_WhenPressedAgain()
    {
        ConsoleCancelEventArgs first = CtrlC();
        ConsoleCancelEventArgs second = CtrlC();
        using StringWriter error = new(CultureInfo.InvariantCulture);

        WithStandardError(error, () =>
        {
            WriteCommand.IgnoreCancelKey(null, first);
            WriteCommand.IgnoreCancelKey(null, second);
        });

        Assert.True(first.Cancel);
        Assert.True(second.Cancel);
        Assert.Equal(WriteCommand.CtrlCIgnored + Environment.NewLine + WriteCommand.CtrlCIgnored + Environment.NewLine, error.ToString());
    }

    /// <summary>
    /// The event arguments of one Ctrl+C, not yet cancelled. Their constructor is internal (only
    /// the runtime's signal handling builds them), so it is reached by reflection.
    /// </summary>
    private static ConsoleCancelEventArgs CtrlC()
    {
        ConstructorInfo? constructor = typeof(ConsoleCancelEventArgs).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            types: new[] { typeof(ConsoleSpecialKey) },
            modifiers: null);
        Assert.NotNull(constructor);
        ConsoleCancelEventArgs args = Assert.IsType<ConsoleCancelEventArgs>(constructor.Invoke(new object[] { ConsoleSpecialKey.ControlC }));
        Assert.Equal(ConsoleSpecialKey.ControlC, args.SpecialKey);
        Assert.False(args.Cancel);
        return args;
    }

    /// <summary>Runs <paramref name="action"/> with standard error replaced by <paramref name="writer"/>, then puts it back.</summary>
    private static void WithStandardError(TextWriter writer, Action action)
    {
        TextWriter original = Console.Error;
        Console.SetError(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetError(original);
        }
    }

    /// <summary>A standard error that fails every write, as a closed or broken stream does.</summary>
    private sealed class FailingWriter : TextWriter
    {
        /// <summary>True once anything was written.</summary>
        public bool WriteAttempted { get; private set; }

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            WriteAttempted = true;
            throw new IOException("The standard error stream cannot be written.");
        }

        public override void Write(string? value)
        {
            WriteAttempted = true;
            throw new IOException("The standard error stream cannot be written.");
        }

        public override void WriteLine(string? value)
        {
            WriteAttempted = true;
            throw new IOException("The standard error stream cannot be written.");
        }
    }
}
