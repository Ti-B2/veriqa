// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// Scope of resolution: it holds EXACTLY ONE read per reader. This is where the rule "a source is
/// read once, not once per level and not once per step of the fallback chain" lives — every level
/// served by the same source and every step of a composite key's chain then works over an already
/// read record, in memory.
/// <para>
/// <b>How far "once" reaches is the CALLER's decision.</b> A resolution asked without a scope creates
/// one of its own and the rule holds within that single resolution — the behaviour every existing
/// caller has. A caller that creates the scope itself extends the rule over everything it resolves
/// inside it: assembling one sign-in page is fifteen resolutions of keys served by the same
/// <c>ui_config</c> record, and a record read fifteen times is fifteen round-trips to the store
/// behind it. Caching does not answer this — a level served by a LIVE source is deliberately not
/// cached at all (SPEC-012 §10.6, CFG-233).
/// </para>
/// <para>
/// The scope is NOT thread-safe: it exists to remember reads, and two resolutions racing inside one
/// scope would race over that memory. Its intended use — assembling one page, serving one request —
/// is sequential; parallel work resolves in scopes of its own.
/// </para>
/// <para>
/// A binding receives the already read record from the scope and does not see the scope in its own
/// signature.
/// </para>
/// </summary>
public sealed class ConfigResolutionScope : IDisposable
{
    /// <summary>
    /// Records already read in this resolution, by reader instance. A reader that returned null is
    /// remembered as well — "there is no record" is an answer, and asking twice would be a second
    /// round-trip to the store.
    /// </summary>
    private readonly Dictionary<IConfigRecordReader, ReadRecord> _records = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// The scope has been released and remembers nothing further.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Creates a scope of resolution over one ownership context. The caller owns its lifetime and
    /// releases it deterministically (<c>using</c>): what the scope holds is the records it has read,
    /// and holding them past the work they were read for is exactly what a scope must not do.
    /// </summary>
    /// <param name="context">Resolution context — who owns the values being resolved.</param>
    public ConfigResolutionScope(ResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Context = context;
    }

    /// <summary>
    /// Context of the resolution this scope belongs to.
    /// </summary>
    public ResolutionContext Context { get; }

    /// <summary>
    /// Returns the record of the reader, reading it on the first request within this resolution and
    /// reusing the result afterwards.
    /// </summary>
    /// <typeparam name="TRecord">Type of the source record.</typeparam>
    /// <param name="reader">Reader of the record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Record, or null when the context addresses none.</returns>
    /// <exception cref="ObjectDisposedException">The scope has been released.</exception>
    /// <remarks>
    /// A read that is CANCELLED is not remembered: the record is put into the scope only after the
    /// reader has returned it, so the next request reads afresh rather than treating a half-finished
    /// read as an answer.
    /// </remarks>
    public async ValueTask<TRecord?> ReadAsync<TRecord>(
        IConfigRecordReader<TRecord> reader,
        CancellationToken cancellationToken = default)
        where TRecord : class
    {
        ArgumentNullException.ThrowIfNull(reader);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_records.TryGetValue(reader, out var cached))
        {
            return ((ReadRecord<TRecord>)cached).Record;
        }

        // A reader that DERIVES its record from another one is read through the scope as well, so the
        // record it derives from is taken from here rather than read a second time behind the scope's
        // back (IDerivedConfigRecordReader{TRecord}).
        var record = reader is IDerivedConfigRecordReader<TRecord> derived
            ? await derived.ReadAsync(this, cancellationToken)
            : await reader.ReadAsync(Context, cancellationToken);

        _records[reader] = new ReadRecord<TRecord>(record);

        return record;
    }

    /// <summary>
    /// Releases the records the scope has read. It holds nothing unmanaged — what it holds is memory
    /// whose lifetime the caller declared by opening the scope, and letting go of it is the point.
    /// </summary>
    public void Dispose()
    {
        _records.Clear();
        _disposed = true;
    }

    /// <summary>
    /// A read already performed in this scope. The base is typeless only in the sense that it names no
    /// record type — it declares no member either, so nothing untyped is ever read through it: the
    /// value is taken back through the typed <see cref="ReadRecord{TRecord}"/>, whose type parameter is
    /// the one the caller asked with.
    /// </summary>
    private abstract class ReadRecord;


    /// <summary>
    /// A read of one record type.
    /// </summary>
    /// <typeparam name="TRecord">Type of the source record.</typeparam>
    /// <param name="record">Record read, or null when the context addressed none.</param>
    private sealed class ReadRecord<TRecord>(TRecord? record) : ReadRecord
        where TRecord : class
    {
        /// <summary>
        /// Record read, or null when the context addressed none.
        /// </summary>
        public TRecord? Record { get; } = record;
    }
}
