// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.AuthServer.UI.Services;

/// <summary>
/// Module matrix of one QR code together with the pixel scale it is drawn at — what
/// <see cref="IQrModuleMatrixSource"/> hands a caller that composes its own image around the code.
/// The matrix includes the quiet zone of the code: <see cref="QuietZoneModules"/> light modules on
/// every side, exactly as the PNG of <see cref="IQrCodeService"/> draws it.
/// </summary>
internal sealed class QrModuleMatrix
{
    /// <summary>
    /// Light modules on each side of the code — the quiet zone the QR standard requires around it.
    /// </summary>
    internal const int QuietZoneModules = 4;

    /// <summary>
    /// Dark flags of the modules, row by row.
    /// </summary>
    private readonly bool[] _darkModules;

    /// <summary>
    /// Creates the matrix.
    /// </summary>
    /// <param name="darkModules">Dark flags of the modules, row by row, quiet zone included.</param>
    /// <param name="moduleCount">Modules on each side of the matrix, quiet zone included.</param>
    /// <param name="pixelsPerModule">Pixel scale the code is drawn at.</param>
    internal QrModuleMatrix(bool[] darkModules, int moduleCount, int pixelsPerModule)
    {
        ArgumentNullException.ThrowIfNull(darkModules);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(moduleCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelsPerModule);

        if (darkModules.Length != moduleCount * moduleCount)
        {
            throw new ArgumentException("The module flags do not form a square of the stated side.", nameof(darkModules));
        }

        _darkModules = darkModules;
        ModuleCount = moduleCount;
        PixelsPerModule = pixelsPerModule;
    }

    /// <summary>
    /// Modules on each side of the matrix, quiet zone included.
    /// </summary>
    internal int ModuleCount { get; }

    /// <summary>
    /// Pixel scale the code is drawn at — resolved for the channel of the code by the level model.
    /// </summary>
    internal int PixelsPerModule { get; }

    /// <summary>
    /// Side of the drawn code in pixels, quiet zone included.
    /// </summary>
    internal int SizePx => ModuleCount * PixelsPerModule;

    /// <summary>
    /// Tells whether a module is dark.
    /// </summary>
    /// <param name="row">Row of the module, counted from the top edge of the quiet zone.</param>
    /// <param name="column">Column of the module, counted from the left edge of the quiet zone.</param>
    /// <returns><c>true</c> for a dark module.</returns>
    internal bool IsDark(int row, int column) => _darkModules[(row * ModuleCount) + column];
}
