// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// Source of setting values — the unit of registration (SPEC-012 §10.6). A level
/// (<see cref="ConfigLevel"/>) is only an order of precedence; the values are physically supplied by
/// a source, and one source serves SEVERAL levels.
/// <para>
/// A source declares exactly two things: which levels it serves in this contour and whether it is
/// live. It performs no reads and sees no record types — the input/output belongs to
/// <see cref="IConfigRecordReader{TRecord}"/> and the extraction to the binding. Key names are
/// therefore unmentionable here by construction.
/// </para>
/// </summary>
public interface IConfigSource
{
    /// <summary>
    /// Name of the source — for the startup map and for registration error messages.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Levels the source serves in this contour: the set is stated by the contour's REGISTRATION,
    /// not hardcoded by the class. Registration is idempotent and UNIONS the sets, so a host without
    /// the auth server gets <c>{ Core }</c> from the Options source and a host with it gets
    /// <c>{ Core, Application, UiConfig }</c>, regardless of the order of the DI calls.
    /// On one level in one contour there is exactly one source (checked at startup).
    /// </summary>
    IReadOnlySet<ConfigLevel> Levels { get; }

    /// <summary>
    /// <c>true</c> — the source is live (its freshness is the configuration provider's job): the
    /// resolver does NOT cache it. <c>false</c> — the source is external (a database or a store) and
    /// is cached by the key's TTL. The kind is known to the owner of the source and is therefore
    /// declared by its class rather than inferred by the resolver from the type of a store.
    /// </summary>
    bool IsLive { get; }
}
