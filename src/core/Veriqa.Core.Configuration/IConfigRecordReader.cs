// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// Identity of a record reader, without its record type — what the resolution scope keys its single
/// read by, and what diagnostics name. It carries no reading member on purpose: reading is typed, and
/// the type belongs to the generic contract below.
/// </summary>
public interface IConfigRecordReader
{
    /// <summary>
    /// Name of the reader — for the startup map and diagnostics. The VALUE of a record never goes
    /// into diagnostics.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Whether what stands behind this reader is LIVE — that is, whether its freshness is somebody
    /// else's job (a configuration provider that reloads itself) rather than the resolver's.
    /// A live reader is never cached; a reader that is not live is cached by the lifetime its key
    /// declares (SPEC-012 §10.6).
    /// <para>
    /// <c>null</c> — the reader has NO ANSWER OF ITS OWN, and the level's source answers instead. That
    /// is the default, so a reader that does not override this member leaves the question to the
    /// level's source.
    /// </para>
    /// <para>
    /// A reader answers here when it knows something the source cannot: ONE reader class may stand
    /// over stores that differ by contour — an options monitor in one deployment, a database in
    /// another — and the level's source is the same in both. The authority is whoever HOLDS the
    /// record, so such a reader translates its store's answer rather than inventing a second one.
    /// </para>
    /// </summary>
    bool? IsLive => null;
}

/// <summary>
/// Reader of a source record — the TYPED port of input/output (SPEC-012 §10.6). It is implemented by
/// the assembly that owns the record type: the auth server owns the OIDC client entry and the
/// <c>ui_config</c> record, the cloud contour owns the tenant row. The mechanism never sees the
/// record type — it is present only as the <typeparamref name="TRecord"/> parameter, so neither an
/// untyped member nor a type cast appears on any public boundary.
/// <para>
/// An instance of a reader ends up inside the singleton binding registry, so it must be safe as a
/// singleton: a dependency with a narrower lifetime is taken through a factory
/// (<c>IServiceScopeFactory</c>, <c>IDbContextFactory&lt;T&gt;</c>), never injected directly.
/// </para>
/// </summary>
/// <typeparam name="TRecord">Type of the source record.</typeparam>
public interface IConfigRecordReader<TRecord> : IConfigRecordReader
    where TRecord : class
{
    /// <summary>
    /// Reads the record addressed by the resolution context; <c>null</c> — the context addresses no
    /// record of this reader (the level does not apply, or the record does not exist).
    /// <para>
    /// This is where an external store is actually touched, and it is asynchronous: a reader of a
    /// database row no longer reads it synchronously on the request thread.
    /// </para>
    /// </summary>
    /// <param name="context">Resolution context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Record, or null.</returns>
    ValueTask<TRecord?> ReadAsync(ResolutionContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reader whose record is DERIVED from the record of ANOTHER reader — a node over a record already
/// read, a projection of one source into the shape a second consumer needs. Such a reader states the
/// derivation here so the resolution scope reads the SOURCE through itself as well.
/// <para>
/// Without it the scope would remember the derived reader alone: the source behind it would be
/// touched once per reader standing over it — twice for one sign-in page whose text settings are read
/// off the typed record and whose design settings are read off the node over it — which is the very
/// "a source is read once per resolution" rule the scope exists for
/// (<see cref="ConfigResolutionScope"/>).
/// </para>
/// </summary>
/// <typeparam name="TRecord">Type of the derived record.</typeparam>
public interface IDerivedConfigRecordReader<TRecord> : IConfigRecordReader<TRecord>
    where TRecord : class
{
    /// <summary>
    /// Reads the record INSIDE a scope of resolution, taking the record it derives from out of that
    /// scope. The context of the read is the scope's own
    /// (<see cref="ConfigResolutionScope.Context"/>), so the two readings address one owner.
    /// </summary>
    /// <param name="scope">Scope of the current resolution.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Record, or null.</returns>
    ValueTask<TRecord?> ReadAsync(ConfigResolutionScope scope, CancellationToken cancellationToken = default);
}
