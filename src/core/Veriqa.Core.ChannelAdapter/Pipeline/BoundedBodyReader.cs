// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Buffers;

namespace Veriqa.Core.ChannelAdapter.Pipeline;

/// <summary>
/// The component's only bounded reader of an inbound HTTP body: copies the request stream while
/// refusing to hold more than the caller's limit.
/// </summary>
/// <remarks>
/// The routes that read a body differ in what the limit is and in how an over-limit body must be
/// answered (a webhook route drops the event and still answers 200, an email push endpoint answers
/// 400), but not in how the body is read. Those two differences are the parameters of this method,
/// so the reading loop itself — the part that guards the ceiling as the bytes arrive — exists once
/// and cannot drift between the routes.
/// </remarks>
internal static class BoundedBodyReader
{
    /// <summary>
    /// Minimum chunk size of the bounded copy: the body arrives piece by piece, so the limit can be
    /// enforced while it is being read rather than after it has been accepted in full. The pool may
    /// hand out a larger array and the whole of it is used — the guard counts bytes, not chunks, so
    /// the size only sets how often the limit is re-checked, never where the limit falls.
    /// </summary>
    private const int CopyChunkSizeBytes = 8192;

    /// <summary>
    /// Copies <paramref name="source"/> into <paramref name="destination"/>, refusing to copy more
    /// than <paramref name="limitBytes"/>.
    /// </summary>
    /// <remarks>
    /// The copied bytes are counted here instead of being read off the destination, so the limit
    /// stays a limit on this body regardless of what the destination already held.
    /// </remarks>
    /// <param name="source">Source stream (the request body).</param>
    /// <param name="destination">Destination stream the body is copied into.</param>
    /// <param name="limitBytes">Maximum number of bytes to copy.</param>
    /// <param name="overLimitExceptionFactory">
    /// Builds the exception thrown when the body crosses the limit: the answer an over-limit body
    /// gets belongs to the route, not to the reader.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static async Task CopyAsync(
        Stream source,
        Stream destination,
        long limitBytes,
        Func<Exception> overLimitExceptionFactory,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(CopyChunkSizeBytes);
        try
        {
            long copied = 0;

            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (copied + read > limitBytes)
                {
                    throw overLimitExceptionFactory();
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                copied += read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
