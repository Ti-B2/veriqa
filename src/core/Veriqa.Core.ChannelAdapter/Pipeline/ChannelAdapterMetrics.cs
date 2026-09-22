// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.Metrics;

using Veriqa.Core.ChannelAdapter.Constants;

namespace Veriqa.Core.ChannelAdapter.Pipeline;

/// <summary>
/// Metrics of the channel adapter component (SPEC-003 §6.5), built on the BCL
/// <c>System.Diagnostics.Metrics</c>. The component owns the instruments but subscribes to nothing:
/// exporting them is the host's duty, which is why the names live in the contracts package and the
/// host adds the meter to its OpenTelemetry provider by the very same constant.
/// </summary>
/// <remarks>
/// Public, yet with nothing public on it. The type is a token that travels: the pipeline entry point
/// <see cref="ChannelWebhookPipeline.ProcessInboundResultAsync"/> is public — a polling service or
/// an inbound endpoint outside this assembly drives it — and it hands the instrument down to the
/// outbound seam the same way it hands down the logger. A caller therefore has to be able to name
/// the type and take it out of DI. Recording, on the other hand, is the component's own business:
/// the constructor and the recording method stay internal, so nobody outside writes into a series
/// that integrators build dashboards on.
/// <para>
/// Instance, not static, and created through <see cref="IMeterFactory"/>: a static meter cannot be
/// substituted in a test, while <c>MetricCollector</c> subscribes to the meter a factory produced.
/// </para>
/// </remarks>
public sealed class ChannelAdapterMetrics
{
    /// <summary>
    /// Counter of user notifications the channel failed to deliver.
    /// </summary>
    private readonly Counter<long> _failedNotices;

    /// <summary>
    /// Counter of sign-in start events that were not served.
    /// </summary>
    private readonly Counter<long> _unservedStarts;

    /// <summary>
    /// Creates the instruments on the component's own meter.
    /// </summary>
    /// <param name="meterFactory">Meter factory of the host.</param>
    internal ChannelAdapterMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(ChannelTelemetry.MeterName);

        _failedNotices = meter.CreateCounter<long>(ChannelTelemetry.FailedNoticesCounterName);
        _unservedStarts = meter.CreateCounter<long>(ChannelTelemetry.UnservedStartsCounterName);
    }

    /// <summary>
    /// Records one failed user notification.
    /// </summary>
    /// <remarks>
    /// Tags carry no PII (CA-121): neither the recipient, nor the message text, nor the inbound
    /// token contents. The error code always comes from an existing code registry, so the tag
    /// cardinality stays bounded.
    /// </remarks>
    /// <param name="channelType">Channel type the notification belonged to.</param>
    /// <param name="errorCode">Error code returned by the adapter.</param>
    /// <param name="noticeKind">Notification kind (see <see cref="ChannelNoticeKinds"/>).</param>
    internal void RecordFailedNotice(string channelType, string errorCode, string noticeKind)
    {
        _failedNotices.Add(
            1,
            new KeyValuePair<string, object?>(ChannelTelemetry.ChannelTypeTag, channelType),
            new KeyValuePair<string, object?>(ChannelTelemetry.ErrorCodeTag, errorCode),
            new KeyValuePair<string, object?>(ChannelTelemetry.NoticeKindTag, noticeKind));
    }

    /// <summary>
    /// Records one sign-in start event the core could not serve.
    /// </summary>
    /// <remarks>
    /// The branch is what is counted, not the reply: the event is recorded whether the registration
    /// declared a reply text, whether the user's budget allowed one, and whichever way the transaction
    /// refused. Only the channel type is tagged — the reason of the refusal belongs to the journal,
    /// which is keyed by the transaction, while an unbounded reason tag here would be written by
    /// whoever sends the events (CA-121: nothing of the sender is tagged either).
    /// </remarks>
    /// <param name="channelType">Channel type the event arrived through.</param>
    internal void RecordUnservedStart(string channelType)
    {
        _unservedStarts.Add(
            1,
            new KeyValuePair<string, object?>(ChannelTelemetry.ChannelTypeTag, channelType));
    }
}
