// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.ChannelAdapter.UI;

namespace Veriqa.Core.AuthServer.UI.Services;

/// <summary>
/// Composer of the QR image the confirmation creation answer hands the relying party (SPEC-039 C20):
/// the code with the attribution mark drawn under it (SPEC-015 §4.18). The image leaves for a screen of
/// the integrator, where no footer of the core exists, so the mark travels in its pixels.
/// <para>
/// Nothing is rasterized at run time. The mark ships pre-rasterized as antialiased 8-bit truecolor PNG
/// resources of this assembly at a range of base heights, produced from a vector master outside the
/// delivery, and this type only stitches: it writes each row of the image as the encoder asks for it —
/// the modules of the code, then the mark, at its own size across the usual range of code widths and
/// scaled by a whole factor only past the largest base height — into one PNG. No canvas of the whole
/// image is held. The QR of the core's own pages does not pass through here: their
/// footer already carries the mark, and a second one in the picture would repeat it.
/// </para>
/// <para>
/// The mark in the image is what the delivery ships by default and what the license covers, not a
/// protection: the picture can be cropped, and nothing here tries to prevent that.
/// </para>
/// </summary>
internal static class QrAttributionImage
{
    /// <summary>
    /// Resource names of the pre-rasterized mark, ascending by height. The set is shared with the
    /// resources the project declares and with the script that produces them — change the three together.
    /// </summary>
    private static readonly IReadOnlyList<string> MarkResourceNames =
    [
        "Veriqa.Core.AuthServer.UI.Assets.qr-attribution-mark-16.png",
        "Veriqa.Core.AuthServer.UI.Assets.qr-attribution-mark-24.png",
        "Veriqa.Core.AuthServer.UI.Assets.qr-attribution-mark-32.png",
        "Veriqa.Core.AuthServer.UI.Assets.qr-attribution-mark-48.png",
        "Veriqa.Core.AuthServer.UI.Assets.qr-attribution-mark-64.png",
        "Veriqa.Core.AuthServer.UI.Assets.qr-attribution-mark-96.png",
        "Veriqa.Core.AuthServer.UI.Assets.qr-attribution-mark-128.png"
    ];

    /// <summary>
    /// Pixels of code width per pixel of the height the mark aims at: the mark takes about a tenth of
    /// the width of the code.
    /// </summary>
    private const int CodeWidthPerMarkHeight = 10;

    /// <summary>
    /// Scale of a mark that is not enlarged.
    /// </summary>
    private const int UnscaledMark = 1;

    /// <summary>
    /// Channel value of the light ground.
    /// </summary>
    private const byte LightChannel = 0xFF;

    /// <summary>
    /// Channel value of a dark module of the code.
    /// </summary>
    private const byte DarkChannel = 0x00;

    /// <summary>
    /// Decoded marks, ascending by height — read from the resources once, on first use.
    /// </summary>
    private static readonly Lazy<IReadOnlyList<RgbBitmap>> Marks = new(LoadMarks);

    /// <summary>
    /// Composes the PNG of a QR code for the answer of the API.
    /// </summary>
    /// <param name="matrix">Module matrix of the code and its pixel scale.</param>
    /// <returns>Bytes of the PNG file; the code alone when the build does not show the attribution
    /// (<see cref="CorePageAttribution.IsShown"/> — the same constant the footer of the pages reads).</returns>
    internal static byte[] ComposePng(QrModuleMatrix matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        // A conditional expression rather than an early return: with a constant condition a statement
        // branch would be unreachable code in one of the two builds (CS0162).
        return CorePageAttribution.IsShown ? ComposeWithMark(matrix) : ComposeCodeOnly(matrix);
    }

    /// <summary>
    /// Encodes the code alone, quiet zone included.
    /// </summary>
    /// <param name="matrix">Module matrix of the code and its pixel scale.</param>
    /// <returns>Bytes of the PNG file.</returns>
    private static byte[] ComposeCodeOnly(QrModuleMatrix matrix)
    {
        var codeSize = matrix.SizePx;

        return RgbPng.Encode(codeSize, codeSize, (y, row) =>
        {
            row.Fill(LightChannel);
            WriteModuleRow(matrix, y, left: 0, row);
        });
    }

    /// <summary>
    /// Encodes the code with the attribution mark under it.
    /// </summary>
    /// <param name="matrix">Module matrix of the code and its pixel scale.</param>
    /// <returns>Bytes of the PNG file.</returns>
    private static byte[] ComposeWithMark(QrModuleMatrix matrix)
    {
        // The method lays out one canvas and encodes it row by row:
        //  - the code is drawn as it is, quiet zone included, so the mark never takes a module of it;
        //  - below the quiet zone goes the mark, scaled by a whole factor, and under it a margin as wide
        //    as the quiet zone, so the mark is framed like the code;
        //  - the canvas is as wide as the wider of the code and the mark with a quiet-zone margin on each
        //    side: a narrow code is centered on a wider canvas instead of the mark being shrunk or cut.
        var codeSize = matrix.SizePx;

        var margin = QrModuleMatrix.QuietZoneModules * matrix.PixelsPerModule;
        var (mark, scale) = FitMark(codeSize);
        var markWidth = mark.Width * scale;
        var markHeight = mark.Height * scale;

        var canvasWidth = Math.Max(codeSize, markWidth + (2 * margin));
        var canvasHeight = codeSize + markHeight + margin;
        var codeLeft = (canvasWidth - codeSize) / 2;
        var markLeft = (canvasWidth - markWidth) / 2;

        return RgbPng.Encode(canvasWidth, canvasHeight, (y, row) =>
        {
            row.Fill(LightChannel);

            if (y < codeSize)
            {
                WriteModuleRow(matrix, y, codeLeft, row);
            }
            else if (y - codeSize < markHeight)
            {
                mark.CopyRowScaled(
                    (y - codeSize) / scale,
                    scale,
                    row.Slice(RgbBitmap.StrideOf(markLeft), RgbBitmap.StrideOf(markWidth)));
            }
        });
    }

    /// <summary>
    /// Picks the mark and its whole scale factor for a code of the given width: the largest base height
    /// not above the aimed height, enlarged the whole number of times it fits into it. A code too narrow
    /// for the smallest base height gets that one unscaled — legibility wins over proportion, and the
    /// canvas widens for it.
    /// </summary>
    /// <param name="codeSize">Width of the code in pixels, quiet zone included.</param>
    /// <returns>The mark and its scale.</returns>
    private static (RgbBitmap Mark, int Scale) FitMark(int codeSize)
    {
        var marks = Marks.Value;
        var aimedHeight = codeSize / CodeWidthPerMarkHeight;
        var mark = marks[0];

        foreach (var candidate in marks)
        {
            if (candidate.Height <= aimedHeight)
            {
                mark = candidate;
            }
        }

        return (mark, Math.Max(UnscaledMark, aimedHeight / mark.Height));
    }

    /// <summary>
    /// Writes the dark modules of one pixel row of the code into a row of the canvas.
    /// </summary>
    /// <param name="matrix">Module matrix of the code.</param>
    /// <param name="y">Pixel row of the code, from its top edge.</param>
    /// <param name="left">Left edge of the code on the canvas.</param>
    /// <param name="row">Bytes of the canvas row.</param>
    private static void WriteModuleRow(QrModuleMatrix matrix, int y, int left, Span<byte> row)
    {
        var pixelsPerModule = matrix.PixelsPerModule;
        var moduleRow = y / pixelsPerModule;

        for (var column = 0; column < matrix.ModuleCount; column++)
        {
            if (matrix.IsDark(moduleRow, column))
            {
                row.Slice(
                        RgbBitmap.StrideOf(left + (column * pixelsPerModule)),
                        RgbBitmap.StrideOf(pixelsPerModule))
                    .Fill(DarkChannel);
            }
        }
    }

    /// <summary>
    /// Reads and decodes the marks from the resources of this assembly.
    /// </summary>
    /// <returns>The marks, ascending by height.</returns>
    private static IReadOnlyList<RgbBitmap> LoadMarks()
    {
        var assembly = typeof(QrAttributionImage).Assembly;
        var marks = new List<RgbBitmap>(MarkResourceNames.Count);

        foreach (var resourceName in MarkResourceNames)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded resource '{resourceName}' was not found in assembly '{assembly.GetName().Name}'.");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);

            marks.Add(RgbPng.Decode(buffer.ToArray()));
        }

        return marks.AsReadOnly();
    }
}
