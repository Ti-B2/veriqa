// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// Enumeration of EVERY record of one configuration level, as nodes (SPEC-012 §10.6). It is the half
/// of a level's reading that <see cref="IConfigRecordReader{TRecord}"/> deliberately does not have: a
/// reader answers a resolution context and returns ONE record, while the walk of a snapshot has to see
/// them all — and see them as records, because above the core level the unit a deployment accepts or
/// rejects is the record (CFG-246) and a value without one could not be named.
/// <para>
/// It is implemented by the OWNER of the level's records, next to the reader of that level, and it
/// names no record type: what it hands out is a <see cref="ConfigNode"/>, which already carries the
/// level, the label of the record and the address the record lives at. That is what lets the catalog
/// of a setting be built from the DECLARATION of its key alone, with no class written per pair
/// "setting + level".
/// </para>
/// <para>
/// The core level has no walker and needs none: its record IS the application configuration of the
/// host, read by the mechanism itself at the absolute address the declaration names
/// (self-hosted ≡ core, SPEC-012 §10.1).
/// </para>
/// </summary>
public interface IConfigNodeWalker
{
    /// <summary>
    /// Configuration level whose records this walker enumerates.
    /// </summary>
    ConfigLevel Level { get; }

    /// <summary>
    /// The walker is the source of its level in THIS deployment. It is the walker's OWN answer and
    /// never a guess made above it: a level whose store a contour substitutes after the auth server
    /// registered this walker is still declared, still bound and still read — just not from the source
    /// this walker stands over (<see cref="IConfigSnapshotCatalog.CanWalk"/> carries the answer on).
    /// <para>
    /// The answer is a property of the composed deployment and does not change over its lifetime.
    /// </para>
    /// </summary>
    bool CanWalk { get; }

    /// <summary>
    /// Configuration section the records of this level live in — the section named in the report when
    /// the level is not walked or could not be read. It is never a section WIDER than what is walked.
    /// </summary>
    string SectionKey { get; }

    /// <summary>
    /// Subscribes to the snapshot boundary of the level's source: the action is invoked once per reload
    /// of that source. Only the owner of the level knows what a reload of it looks like, which is why
    /// the walker is asked to subscribe rather than told what to listen to.
    /// </summary>
    /// <param name="onSnapshot">Action to invoke on a new snapshot.</param>
    /// <returns>Subscription to dispose; null when the source announces no reload.</returns>
    IDisposable? Subscribe(Action onSnapshot);

    /// <summary>
    /// Enumerates the records of the level as nodes. Only the records the RESOLUTION of this level
    /// reads are enumerated: a record the source dropped from its effective set states nothing to a
    /// resolution (CFG-210), so a finding about it would name a value no consumer can ever be given.
    /// </summary>
    /// <returns>Nodes over the records of the level.</returns>
    IEnumerable<ConfigNode> Walk();
}
