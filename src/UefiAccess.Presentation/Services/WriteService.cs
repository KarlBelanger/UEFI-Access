// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Serilog;
using UefiAccess.Core.Safety;

namespace UefiAccess.Presentation.Services;

/// <summary>
/// The application's <see cref="IWriteService"/> over one <see cref="IWritePipeline"/>, composed
/// lazily: the factory runs on the first dry run, commit or cancel, never when the service is
/// built, so building the live source (which tests do) never builds the real writer. Every
/// pipeline call runs on a thread-pool thread (<c>Task.Run</c>), so the UI
/// thread stays responsive through the WMI queries of the checks and through the write. A dry run
/// the caller cancels returns at once and goes on in the background to its next stage boundary,
/// where the pipeline records the cancel; nothing here waits on a clock. The commit is handed no
/// cancellation token at all. The service logs only at Debug: the shell writes the one panel line.
/// </summary>
public sealed class WriteService : IWriteService
{
    /// <summary>The message of the exception a factory that returned no pipeline gives.</summary>
    internal const string NoPipelineMessage = "The write pipeline factory returned no pipeline.";

    private readonly Lazy<IWritePipeline> _pipeline;
    private readonly SnapshotStore _snapshots;
    private readonly ILogger _logger;

    /// <summary>Creates the service.</summary>
    /// <param name="pipelineFactory">Composes the pipeline; called once, on the first dry run, commit or cancel.</param>
    /// <param name="snapshots">The snapshot store the pipeline writes to, read by <see cref="Newest"/> without composing the pipeline.</param>
    /// <param name="logger">Optional logger; null disables logging.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pipelineFactory"/> or <paramref name="snapshots"/> is null.</exception>
    public WriteService(Func<IWritePipeline> pipelineFactory, SnapshotStore snapshots, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(pipelineFactory);
        ArgumentNullException.ThrowIfNull(snapshots);
        _pipeline = new Lazy<IWritePipeline>(() => pipelineFactory() ?? throw new InvalidOperationException(NoPipelineMessage), LazyThreadSafetyMode.ExecutionAndPublication);
        _snapshots = snapshots;
        _logger = logger ?? Serilog.Core.Logger.None;
    }

    /// <summary>True once the pipeline has been composed (the first dry run, commit or cancel called the factory).</summary>
    public bool IsComposed => _pipeline.IsValueCreated;

    /// <inheritdoc />
    public string SnapshotRoot => _snapshots.RootDirectory;

    /// <inheritdoc />
    public async Task<PrepareResult?> PrepareAsync(ChangeRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (token.IsCancellationRequested)
        {
            _logger.Debug("The checks of a {Request} were cancelled before they started", request.GetType().Name);
            return null;
        }

        Task<PrepareResult> checks = Task.Run(() => _pipeline.Value.PrepareAsync(request, token), CancellationToken.None);
        TaskCompletionSource cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using (token.Register(() => cancelled.TrySetResult()))
        {
            Task first = await Task.WhenAny(checks, cancelled.Task).ConfigureAwait(false);
            if (ReferenceEquals(first, checks))
            {
                return await checks.ConfigureAwait(false);
            }
        }

        _logger.Debug("The checks of a {Request} were cancelled; they stop at their next stage boundary", request.GetType().Name);
        Abandon(checks);
        return null;
    }

    /// <inheritdoc />
    public Task<TransactionResult> CommitAsync(ChangePlan plan, ConfirmationToken confirmation)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(confirmation);
        _logger.Debug("Committing {Operation} of {Key} as {TransactionId}", plan.Operation, plan.Variable.Key, plan.SnapshotFolderName);
        return Task.Run(() => _pipeline.Value.CommitAsync(plan, confirmation), CancellationToken.None);
    }

    /// <inheritdoc />
    public Task<TransactionResult> CancelAsync(ChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _logger.Debug("Recording the cancel of {Operation} of {Key}", plan.Operation, plan.Variable.Key);
        return Task.Run(() => _pipeline.Value.CancelAsync(plan), CancellationToken.None);
    }

    /// <inheritdoc />
    public SnapshotSummary? Newest(string key)
    {
        try
        {
            return _snapshots.Newest(key);
        }
        catch (Exception ex)
        {
            // SnapshotStore.Newest does not throw; a root that cannot be listed offers nothing to restore.
            _logger.Debug(ex, "Listing the snapshots of {Key} failed", key);
            return null;
        }
    }

    /// <summary>
    /// Leaves checks the caller cancelled to finish on their own: a fault is observed and logged,
    /// and a plan they still made (the cancel came after their last stage boundary) is cancelled
    /// in the pipeline, so it is recorded as cancelled and can never be committed.
    /// </summary>
    private void Abandon(Task<PrepareResult> checks)
    {
        _ = checks.ContinueWith(
            finished =>
            {
                if (finished.IsFaulted)
                {
                    _logger.Debug(finished.Exception, "Cancelled checks ended in an exception");
                    return;
                }

                if (finished.IsCompletedSuccessfully && finished.Result.Plan is ChangePlan plan)
                {
                    _ = CancelAsync(plan).ContinueWith(
                        recorded => _logger.Debug(recorded.Exception, "The cancel of a plan made after its checks were cancelled could not be recorded"),
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
