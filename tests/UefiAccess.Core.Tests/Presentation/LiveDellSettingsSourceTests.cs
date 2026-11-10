// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using UefiAccess.Core.Settings.Dell;
using UefiAccess.Presentation.Services;
using Xunit;

namespace UefiAccess.Core.Tests.Presentation;

/// <summary>
/// <see cref="LiveDellSettingsSource"/> over its test seam with a gated fake reader: never the real reader, so no test on this PC (the unit) reads Dell
/// Command Monitor or starts Dell Command Configure. A read runs off the caller's thread, with the
/// default options (Automatic, the agreed settings only), and a read still in flight is handed to
/// a second caller instead of starting another.
/// </summary>
public sealed class LiveDellSettingsSourceTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task ReadAsync_JoinsAReadInFlight()
    {
        using GatedDellSettingsReader reader = new();
        LiveDellSettingsSource source = new(reader);

        Task<DellSettingsResult> first = source.ReadAsync();
        Assert.True(reader.Entered.Wait(Bound), "the read never reached the reader");
        Task<DellSettingsResult> second = source.ReadAsync();

        Assert.Same(first, second);
        Assert.False(first.IsCompleted);
        reader.Release.Set();
        Assert.Same(reader.Answer, await first);
        Assert.Equal(1, reader.Reads);
        Assert.Equal(new[] { DellReadOptions.Default }, reader.Options);

        // Once it has answered, the next call is a read of its own.
        Task<DellSettingsResult> third = source.ReadAsync();
        Assert.NotSame(first, third);
        Assert.Same(reader.Answer, await third);
        Assert.Equal(2, reader.Reads);
    }

    [Fact]
    public async Task ReadAsync_RunsOffTheCallersThread()
    {
        using GatedDellSettingsReader reader = new();
        LiveDellSettingsSource source = new(reader);
        int caller = Environment.CurrentManagedThreadId;

        Task<DellSettingsResult> read = source.ReadAsync();

        // The call returned while the reader still waits: the read runs on a pool thread.
        Assert.True(reader.Entered.Wait(Bound), "the read never reached the reader");
        Assert.False(read.IsCompleted);
        Assert.NotEqual(caller, reader.ThreadId);
        Assert.True(reader.OnThreadPool);
        reader.Release.Set();
        Assert.Same(reader.Answer, await read);
    }

    /// <summary>A reader that waits, on the thread it is called on, until the test releases it; it records each read.</summary>
    private sealed class GatedDellSettingsReader : IDellSettingsReader, IDisposable
    {
        private readonly object _gate = new();
        private readonly List<DellReadOptions> _options = new();

        public ManualResetEventSlim Entered { get; } = new(false);

        public ManualResetEventSlim Release { get; } = new(false);

        public DellSettingsResult Answer { get; } = DellResults.CompleteA18();

        public int ThreadId { get; private set; }

        public bool OnThreadPool { get; private set; }

        public int Reads
        {
            get
            {
                lock (_gate)
                {
                    return _options.Count;
                }
            }
        }

        public IReadOnlyList<DellReadOptions> Options
        {
            get
            {
                lock (_gate)
                {
                    return _options.ToArray();
                }
            }
        }

        public DellSettingsResult Read(DellReadOptions options)
        {
            lock (_gate)
            {
                _options.Add(options);
            }

            ThreadId = Environment.CurrentManagedThreadId;
            OnThreadPool = Thread.CurrentThread.IsThreadPoolThread;
            Entered.Set();
            Release.Wait(Bound);
            return Answer;
        }

        public void Dispose()
        {
            Release.Set();
            Entered.Dispose();
            Release.Dispose();
        }
    }
}
