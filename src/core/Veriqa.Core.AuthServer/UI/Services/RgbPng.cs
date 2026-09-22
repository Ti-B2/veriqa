// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Buffers.Binary;
using System.IO.Compression;

namespace Veriqa.Core.AuthServer.UI.Services;

/// <summary>
/// Encoder and decoder of 8-bit truecolor PNG (W3C PNG Specification) — the one image format the QR
/// image of the confirmation answer needs: the code is black and white, and the attribution mark under
/// it keeps its brand color and its antialiased edges. Built on the platform alone: deflate is
/// <see cref="ZLibStream"/>, and the CRC-32 of the chunks is a local table function, so no package is
/// added for either.
/// </summary>
internal static class RgbPng
{
    /// <summary>
    /// Size of the length field and of the checksum field of a chunk, and of its type.
    /// </summary>
    private const int ChunkFieldSize = 4;

    /// <summary>
    /// Bytes in front of the data of a chunk: its length and its type.
    /// </summary>
    private const int ChunkPrefixSize = 2 * ChunkFieldSize;

    /// <summary>
    /// Length of the data of the header chunk.
    /// </summary>
    private const int HeaderLength = 13;

    /// <summary>
    /// Offset of the width in the header chunk.
    /// </summary>
    private const int HeaderWidthOffset = 0;

    /// <summary>
    /// Offset of the height in the header chunk.
    /// </summary>
    private const int HeaderHeightOffset = 4;

    /// <summary>
    /// Offset of the bit depth in the header chunk.
    /// </summary>
    private const int HeaderBitDepthOffset = 8;

    /// <summary>
    /// Offset of the color type in the header chunk.
    /// </summary>
    private const int HeaderColorTypeOffset = 9;

    /// <summary>
    /// Offset of the compression method in the header chunk.
    /// </summary>
    private const int HeaderCompressionOffset = 10;

    /// <summary>
    /// Offset of the filter method in the header chunk.
    /// </summary>
    private const int HeaderFilterOffset = 11;

    /// <summary>
    /// Offset of the interlace method in the header chunk.
    /// </summary>
    private const int HeaderInterlaceOffset = 12;

    /// <summary>
    /// Bit depth of the images this type reads and writes.
    /// </summary>
    private const byte BitDepth = 8;

    /// <summary>
    /// Color type "truecolor": red, green and blue per pixel, no alpha.
    /// </summary>
    private const byte ColorTypeTruecolor = 2;

    /// <summary>
    /// Compression method "deflate" — the only one PNG defines.
    /// </summary>
    private const byte CompressionMethodDeflate = 0;

    /// <summary>
    /// Filter method "adaptive" — the only one PNG defines.
    /// </summary>
    private const byte FilterMethodAdaptive = 0;

    /// <summary>
    /// Interlace method "none".
    /// </summary>
    private const byte InterlaceMethodNone = 0;

    /// <summary>
    /// Row filter "None": the row is stored as it is.
    /// </summary>
    private const byte FilterTypeNone = 0;

    /// <summary>
    /// Row filter "Sub": difference from the previous byte of the row.
    /// </summary>
    private const byte FilterTypeSub = 1;

    /// <summary>
    /// Row filter "Up": difference from the byte above.
    /// </summary>
    private const byte FilterTypeUp = 2;

    /// <summary>
    /// Row filter "Average": difference from the mean of the previous byte and the byte above.
    /// </summary>
    private const byte FilterTypeAverage = 3;

    /// <summary>
    /// Row filter "Paeth": difference from the Paeth predictor.
    /// </summary>
    private const byte FilterTypePaeth = 4;

    /// <summary>
    /// Writes one row of the image being encoded.
    /// </summary>
    /// <param name="y">Row, from the top.</param>
    /// <param name="row">Bytes of the row, red, green and blue per pixel. The buffer is reused between
    /// rows, so the writer sets every byte of it.</param>
    internal delegate void RowWriter(int y, Span<byte> row);

    /// <summary>
    /// Signature every PNG starts with.
    /// </summary>
    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Type of the header chunk.
    /// </summary>
    private static ReadOnlySpan<byte> HeaderChunkType => "IHDR"u8;

    /// <summary>
    /// Type of the image data chunk.
    /// </summary>
    private static ReadOnlySpan<byte> DataChunkType => "IDAT"u8;

    /// <summary>
    /// Type of the end chunk.
    /// </summary>
    private static ReadOnlySpan<byte> EndChunkType => "IEND"u8;

    /// <summary>
    /// Encodes an image as an 8-bit truecolor PNG, asking for its rows one at a time.
    /// </summary>
    /// <remarks>
    /// The image is never held whole: the encoder keeps the row it writes and the row above, so the
    /// memory of an encode follows the width of the image and its compressed size, not its area — a
    /// truecolor canvas takes three bytes a pixel, and the pixel scale of the code is the operator's.
    /// </remarks>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="writeRow">Writer of each row, called top to bottom.</param>
    /// <returns>Bytes of the PNG file.</returns>
    internal static byte[] Encode(int width, int height, RowWriter writeRow)
    {
        // The method writes the signature, the header, ONE data chunk holding every row, and the end
        // chunk. The row filter is fixed rather than searched for per row — see Compress.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(writeRow);

        Span<byte> header = stackalloc byte[HeaderLength];
        BinaryPrimitives.WriteInt32BigEndian(header[HeaderWidthOffset..], width);
        BinaryPrimitives.WriteInt32BigEndian(header[HeaderHeightOffset..], height);
        header[HeaderBitDepthOffset] = BitDepth;
        header[HeaderColorTypeOffset] = ColorTypeTruecolor;
        header[HeaderCompressionOffset] = CompressionMethodDeflate;
        header[HeaderFilterOffset] = FilterMethodAdaptive;
        header[HeaderInterlaceOffset] = InterlaceMethodNone;

        var data = Compress(width, height, writeRow);

        using var output = new MemoryStream();
        output.Write(Signature);
        WriteChunk(output, HeaderChunkType, header);
        WriteChunk(output, DataChunkType, data);
        WriteChunk(output, EndChunkType, ReadOnlySpan<byte>.Empty);

        return output.ToArray();
    }

    /// <summary>
    /// Decodes an 8-bit truecolor PNG.
    /// </summary>
    /// <param name="png">Bytes of the PNG file.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="InvalidDataException">The bytes are not an 8-bit truecolor PNG without interlacing.</exception>
    internal static RgbBitmap Decode(ReadOnlySpan<byte> png)
    {
        // The method reads what Encode writes and what a PNG optimizer may turn it into: the header has
        // to declare an 8-bit truecolor image without interlacing, while the image data may be split
        // across several data chunks and filtered with any of the five filter types. Ancillary chunks
        // are skipped and checksums are not verified — the input is a resource of this assembly, not
        // data from outside.
        if (!png.StartsWith(Signature))
        {
            throw Unreadable("the signature is missing");
        }

        var position = Signature.Length;
        var width = 0;
        var height = 0;
        using var imageData = new MemoryStream();

        while (true)
        {
            if (png.Length - position < ChunkPrefixSize)
            {
                throw Unreadable("a chunk is truncated");
            }

            var length = BinaryPrimitives.ReadInt32BigEndian(png[position..]);
            var type = png.Slice(position + ChunkFieldSize, ChunkFieldSize);
            var dataStart = position + ChunkPrefixSize;

            if (length < 0 || png.Length - dataStart - ChunkFieldSize < length)
            {
                throw Unreadable("a chunk is truncated");
            }

            var data = png.Slice(dataStart, length);
            position = dataStart + length + ChunkFieldSize;

            if (type.SequenceEqual(HeaderChunkType))
            {
                (width, height) = ReadHeader(data);
            }
            else if (type.SequenceEqual(DataChunkType))
            {
                imageData.Write(data);
            }
            else if (type.SequenceEqual(EndChunkType))
            {
                break;
            }
        }

        if (width is 0)
        {
            throw Unreadable("the header is missing");
        }

        var stride = RgbBitmap.StrideOf(width);
        var scanlines = Inflate(imageData, (stride + 1) * height);

        return new RgbBitmap(width, height, Unfilter(scanlines, stride, height));
    }

    /// <summary>
    /// Deflates the rows of an image into a zlib stream: a row that repeats the row above behind the
    /// filter type Up, any other row behind the filter type None.
    /// </summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="writeRow">Writer of each row.</param>
    /// <returns>The zlib stream — the data of the image data chunk.</returns>
    private static byte[] Compress(int width, int height, RowWriter writeRow)
    {
        // A QR is drawn in squares of whole modules, so most of its rows repeat the row above byte for
        // byte, and Up turns such a row into zeros. A row that differs — a module boundary, the band of
        // the mark — deflates better as it is than as a difference, so it stays behind None.
        ReadOnlySpan<byte> noneFilter = [FilterTypeNone];
        ReadOnlySpan<byte> upFilter = [FilterTypeUp];
        var stride = RgbBitmap.StrideOf(width);
        var unchangedRow = new byte[stride];
        var row = new byte[stride];
        var above = new byte[stride];

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            for (var y = 0; y < height; y++)
            {
                writeRow(y, row);

                if (y > 0 && row.AsSpan().SequenceEqual(above.AsSpan()))
                {
                    zlib.Write(upFilter);
                    zlib.Write(unchangedRow);
                }
                else
                {
                    zlib.Write(noneFilter);
                    zlib.Write(row);
                }

                (row, above) = (above, row);
            }
        }

        return compressed.ToArray();
    }

    /// <summary>
    /// Writes one chunk: length, type, data and the CRC-32 of the type and the data.
    /// </summary>
    /// <param name="output">Stream to write to.</param>
    /// <param name="type">Chunk type.</param>
    /// <param name="data">Chunk data.</param>
    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> field = stackalloc byte[ChunkFieldSize];

        BinaryPrimitives.WriteInt32BigEndian(field, data.Length);
        output.Write(field);
        output.Write(type);
        output.Write(data);

        var crc = Crc32.Append(Crc32.Seed, type);
        crc = Crc32.Append(crc, data);
        BinaryPrimitives.WriteUInt32BigEndian(field, Crc32.Complete(crc));
        output.Write(field);
    }

    /// <summary>
    /// Reads the size of the image out of the header chunk and checks it declares the format this type
    /// decodes.
    /// </summary>
    /// <param name="header">Data of the header chunk.</param>
    /// <returns>Width and height of the image.</returns>
    private static (int Width, int Height) ReadHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length != HeaderLength)
        {
            throw Unreadable("the header has a wrong length");
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(header[HeaderWidthOffset..]);
        var height = BinaryPrimitives.ReadInt32BigEndian(header[HeaderHeightOffset..]);

        if (width <= 0 || height <= 0)
        {
            throw Unreadable("the image is empty");
        }

        if (header[HeaderBitDepthOffset] != BitDepth
            || header[HeaderColorTypeOffset] != ColorTypeTruecolor
            || header[HeaderCompressionOffset] != CompressionMethodDeflate
            || header[HeaderFilterOffset] != FilterMethodAdaptive
            || header[HeaderInterlaceOffset] != InterlaceMethodNone)
        {
            throw Unreadable("it is not an 8-bit truecolor image without interlacing");
        }

        return (width, height);
    }

    /// <summary>
    /// Inflates the concatenated image data into the filtered scanlines.
    /// </summary>
    /// <param name="compressed">Concatenated data of the image data chunks.</param>
    /// <param name="length">Length of the scanlines: a filter byte and a row per row.</param>
    /// <returns>The filtered scanlines.</returns>
    private static byte[] Inflate(MemoryStream compressed, int length)
    {
        compressed.Position = 0;

        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true);
        var scanlines = new byte[length];
        zlib.ReadExactly(scanlines);

        return scanlines;
    }

    /// <summary>
    /// Reverses the row filters of the scanlines.
    /// </summary>
    /// <param name="scanlines">Filtered scanlines: a filter byte and a row per row.</param>
    /// <param name="stride">Bytes in one row.</param>
    /// <param name="height">Rows in the image.</param>
    /// <returns>Pixel bytes, row by row.</returns>
    private static byte[] Unfilter(byte[] scanlines, int stride, int height)
    {
        // Each byte is restored by adding its predictor. At a bit depth of 8 the "previous pixel" of the
        // filters is the byte of the same channel one pixel to the left, and the row above the first one
        // reads as zeros.
        var pixels = new byte[stride * height];
        var bytesPerPixel = RgbBitmap.BytesPerPixel;

        for (var y = 0; y < height; y++)
        {
            var sourceStart = y * (stride + 1);
            var filterType = scanlines[sourceStart];
            var source = scanlines.AsSpan(sourceStart + 1, stride);
            var row = pixels.AsSpan(y * stride, stride);
            ReadOnlySpan<byte> above = y is 0 ? default : pixels.AsSpan((y - 1) * stride, stride);

            for (var i = 0; i < stride; i++)
            {
                var left = i < bytesPerPixel ? 0 : row[i - bytesPerPixel];
                var up = y is 0 ? 0 : above[i];
                var upLeft = y is 0 || i < bytesPerPixel ? 0 : above[i - bytesPerPixel];

                var predictor = filterType switch
                {
                    FilterTypeNone => 0,
                    FilterTypeSub => left,
                    FilterTypeUp => up,
                    FilterTypeAverage => (left + up) / 2,
                    FilterTypePaeth => PaethPredictor(left, up, upLeft),
                    _ => throw Unreadable("a row has an unknown filter type")
                };

                row[i] = unchecked((byte)(source[i] + predictor));
            }
        }

        return pixels;
    }

    /// <summary>
    /// The Paeth predictor: of the left, upper and upper-left bytes, the one closest to their linear
    /// estimate, ties resolved in that order.
    /// </summary>
    /// <param name="left">Byte to the left.</param>
    /// <param name="up">Byte above.</param>
    /// <param name="upLeft">Byte above and to the left.</param>
    /// <returns>The predictor.</returns>
    private static int PaethPredictor(int left, int up, int upLeft)
    {
        var estimate = left + up - upLeft;
        var distanceLeft = Math.Abs(estimate - left);
        var distanceUp = Math.Abs(estimate - up);
        var distanceUpLeft = Math.Abs(estimate - upLeft);

        if (distanceLeft <= distanceUp && distanceLeft <= distanceUpLeft)
        {
            return left;
        }

        return distanceUp <= distanceUpLeft ? up : upLeft;
    }

    /// <summary>
    /// Builds the failure of a decode.
    /// </summary>
    /// <param name="reason">What is wrong with the bytes.</param>
    /// <returns>The exception to throw.</returns>
    private static InvalidDataException Unreadable(string reason) =>
        new($"The bytes are not a PNG this decoder reads: {reason}.");

    /// <summary>
    /// CRC-32 of PNG chunks — the reflected polynomial 0xEDB88320 with a precomputed byte table, as the
    /// PNG specification gives it. Local on purpose: the platform's own implementation ships as a
    /// separate package, and the delivery adds no package for a few lines.
    /// </summary>
    private static class Crc32
    {
        /// <summary>
        /// Starting value of a checksum.
        /// </summary>
        internal const uint Seed = 0xFFFFFFFF;

        /// <summary>
        /// The reflected CRC-32 polynomial.
        /// </summary>
        private const uint Polynomial = 0xEDB88320;

        /// <summary>
        /// Entries of the table — one per byte value.
        /// </summary>
        private const int TableSize = 256;

        /// <summary>
        /// Bits in one byte — the shifts one table entry takes.
        /// </summary>
        private const int BitsPerByte = 8;

        /// <summary>
        /// Mask of the low byte of the running checksum.
        /// </summary>
        private const uint LowByteMask = 0xFF;

        /// <summary>
        /// Checksum of every byte value, precomputed once.
        /// </summary>
        private static readonly uint[] Table = BuildTable();

        /// <summary>
        /// Folds bytes into a running checksum.
        /// </summary>
        /// <param name="crc">Running checksum; <see cref="Seed"/> for a new one.</param>
        /// <param name="data">Bytes to fold in.</param>
        /// <returns>The running checksum.</returns>
        internal static uint Append(uint crc, ReadOnlySpan<byte> data)
        {
            foreach (var value in data)
            {
                crc = Table[(crc ^ value) & LowByteMask] ^ (crc >> BitsPerByte);
            }

            return crc;
        }

        /// <summary>
        /// Completes a running checksum into the value a chunk stores.
        /// </summary>
        /// <param name="crc">Running checksum.</param>
        /// <returns>The checksum.</returns>
        internal static uint Complete(uint crc) => crc ^ Seed;

        /// <summary>
        /// Computes the table of the checksums of every byte value.
        /// </summary>
        /// <returns>The table.</returns>
        private static uint[] BuildTable()
        {
            var table = new uint[TableSize];

            for (var index = 0; index < TableSize; index++)
            {
                var entry = (uint)index;

                for (var bit = 0; bit < BitsPerByte; bit++)
                {
                    entry = (entry & 1) is 0 ? entry >> 1 : Polynomial ^ (entry >> 1);
                }

                table[index] = entry;
            }

            return table;
        }
    }
}
