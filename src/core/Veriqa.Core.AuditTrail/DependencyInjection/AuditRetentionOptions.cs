// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.AuditTrail.DependencyInjection;

/// <summary>
/// Parameters of the retention sweep. Host-owned: they are supplied through the registration
/// delegate rather than a configuration section, because the audit axis introduces no
/// configuration keys of its own beyond the ones SPEC-012 already declares.
/// How long a record lives is NOT configured here — that is the levelled logging retention setting,
/// resolved through the canonical resolver; these two only say how often and in what batches the
/// sweep runs.
/// </summary>
public sealed class AuditRetentionOptions
{
    /// <summary>
    /// Default number of records deleted per sweep iteration.
    /// </summary>
    public const int DefaultBatchSize = 500;

    /// <summary>
    /// Smallest batch a sweep iteration may delete. Not a policy of ours but the termination
    /// condition of the sweep: it repeats until a batch comes back short of the requested size,
    /// which a batch of zero never does — the pass would spin forever deleting nothing. There is
    /// deliberately no upper bound: the batch is a limit over the expired records, and a value
    /// above the backlog simply clears it in a single pass.
    /// </summary>
    public const int MinBatchSize = 1;

    /// <summary>
    /// Default pause between retention sweeps.
    /// </summary>
    public static readonly TimeSpan DefaultSweepInterval = TimeSpan.FromHours(1);

    /// <summary>
    /// Lower bound of the pause between sweeps. Same origin as <see cref="MaxSweepInterval"/> —
    /// the contract of the timer the sweep runs on: <see cref="PeriodicTimer"/> truncates the
    /// period to whole milliseconds and rejects anything that leaves less than one of them.
    /// </summary>
    public static readonly TimeSpan MinSweepInterval = TimeSpan.FromMilliseconds(1);

    /// <summary>
    /// Upper bound of the pause between sweeps (about 49.7 days). It is not a policy of ours but
    /// the contract of the timer the sweep runs on: <see cref="PeriodicTimer"/> accepts a period of
    /// at most <see cref="uint.MaxValue"/> - 1 milliseconds and throws on anything larger.
    /// </summary>
    public static readonly TimeSpan MaxSweepInterval = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    /// <summary>
    /// Pause between retention sweeps. Must be between <see cref="MinSweepInterval"/> and
    /// <see cref="MaxSweepInterval"/> inclusive.
    /// </summary>
    public TimeSpan SweepInterval { get; set; } = DefaultSweepInterval;

    /// <summary>
    /// Number of records deleted per iteration of a sweep. Must be at least
    /// <see cref="MinBatchSize"/>; it has no upper bound.
    /// </summary>
    public int BatchSize { get; set; } = DefaultBatchSize;
}
