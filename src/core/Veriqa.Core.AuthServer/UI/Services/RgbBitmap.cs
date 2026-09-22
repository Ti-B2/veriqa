// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.AuthServer.UI.Services;

/// <summary>
/// An 8-bit truecolor image kept in the byte layout of PNG scanlines: rows of <see cref="Stride"/>
/// bytes, red, green and blue per pixel — a decoded attribution mark of the QR image of the confirmation
/// answer. The image the mark is composed into is never held as one: it is encoded row by row
/// (<see cref="RgbPng.Encode"/>).
/// </summary>
internal sealed class RgbBitmap
{
    /// <summary>
    /// Bytes of one pixel: red, green, blue.
    /// </summary>
    internal const int BytesPerPixel = 3;

    /// <summary>
    /// Pixel bytes, row by row.
    /// </summary>
    private readonly byte[] _pixels;

    /// <summary>
    /// Creates an image over decoded pixel bytes.
    /// </summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="pixels">Pixel bytes, row by row, <see cref="Stride"/> bytes per row.</param>
    internal RgbBitmap(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);

        Width = width;
        Height = height;
        Stride = StrideOf(width);

        if (pixels.Length != Stride * height)
        {
            throw new ArgumentException("The pixel bytes do not match the stated size.", nameof(pixels));
        }

        _pixels = pixels;
    }

    /// <summary>
    /// Width in pixels.
    /// </summary>
    internal int Width { get; }

    /// <summary>
    /// Height in pixels.
    /// </summary>
    internal int Height { get; }

    /// <summary>
    /// Bytes in one row.
    /// </summary>
    internal int Stride { get; }

    /// <summary>
    /// Bytes a row of the given width takes.
    /// </summary>
    /// <param name="width">Width in pixels.</param>
    /// <returns>Bytes per row.</returns>
    internal static int StrideOf(int width) => width * BytesPerPixel;

    /// <summary>
    /// Copies one row of this image into a row of a larger one, every pixel as
    /// <paramref name="scale"/> pixels side by side.
    /// </summary>
    /// <param name="y">Row of this image.</param>
    /// <param name="scale">Whole scale factor.</param>
    /// <param name="destination">Bytes the scaled row is written to, <see cref="Stride"/> times
    /// <paramref name="scale"/> long.</param>
    internal void CopyRowScaled(int y, int scale, Span<byte> destination)
    {
        var row = GetRow(y);

        for (var x = 0; x < Width; x++)
        {
            var pixel = row.Slice(x * BytesPerPixel, BytesPerPixel);

            for (var copy = 0; copy < scale; copy++)
            {
                pixel.CopyTo(destination[(((x * scale) + copy) * BytesPerPixel)..]);
            }
        }
    }

    /// <summary>
    /// Bytes of one row.
    /// </summary>
    /// <param name="y">Row.</param>
    /// <returns>The <see cref="Stride"/> bytes of the row.</returns>
    internal ReadOnlySpan<byte> GetRow(int y) => _pixels.AsSpan(y * Stride, Stride);
}
