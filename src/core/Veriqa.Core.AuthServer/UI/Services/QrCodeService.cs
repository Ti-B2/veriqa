// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using QRCoder;

using Veriqa.Core.AuthServer.Configuration.Resolution;
using Veriqa.Core.Configuration;

namespace Veriqa.Core.AuthServer.UI.Services;

/// <summary>
/// QR code generation service interface (SPEC-007 §3.1).
/// </summary>
public interface IQrCodeService
{
    /// <summary>
    /// Generates a QR code as a Base64 string of a PNG image.
    /// </summary>
    /// <param name="content">QR code content (deep link URL).</param>
    /// <param name="channelType">
    /// Channel type the QR is rendered for — the value of the <c>channel</c> dimension the pixel scale
    /// is resolved with (SPEC-012 CFG-237).
    /// </param>
    /// <param name="resolution">
    /// Resolution context of the request, built ONCE at the entry of the sign-in page and passed
    /// explicitly. <see cref="ResolutionContext.Core"/> outside a request (an integrator calling the
    /// service on its own, self-hosted with no levels set) — the degenerate N=1 of the same path,
    /// not a separate branch (CFG-202).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Base64 string of the PNG image.</returns>
    ValueTask<string> GenerateBase64PngAsync(
        string content,
        string channelType,
        ResolutionContext resolution,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Internal seam of the QR generator for a caller that draws the image itself: the module matrix and
/// the pixel scale of the very code <see cref="IQrCodeService"/> renders — one generator, one error
/// correction level, one resolution of the scale, no second copy of any of them.
/// <para>
/// Internal on purpose: its one caller is the confirmation creation answer, which draws the attribution
/// mark under the code, and the public surface of the service stays the PNG it has always returned.
/// </para>
/// </summary>
internal interface IQrModuleMatrixSource
{
    /// <summary>
    /// Generates the module matrix of a QR code with the pixel scale resolved for it.
    /// </summary>
    /// <param name="content">QR code content.</param>
    /// <param name="channelType">Channel type the QR is rendered for — the <c>channel</c> dimension of
    /// the pixel scale (SPEC-012 CFG-237); empty — no channel, the flat value of the level applies.</param>
    /// <param name="resolution">Resolution context of the request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The module matrix, quiet zone included, and its pixel scale.</returns>
    ValueTask<QrModuleMatrix> GenerateModuleMatrixAsync(
        string content,
        string channelType,
        ResolutionContext resolution,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// QR code generation service implementation based on QRCoder (SPEC-007 UI-013).
/// Generates QR codes in PNG format and returns a Base64 string (without the data URI prefix).
/// The data:image/png;base64, prefix is added by the renderer when embedding into HTML.
/// It also serves the module matrix of the same code through <see cref="IQrModuleMatrixSource"/>.
/// </summary>
internal sealed class QrCodeService : IQrCodeService, IQrModuleMatrixSource
{
    /// <summary>
    /// Gathering point of the level-owned sign-in page settings (SPEC-012 §10.6) — the source of the
    /// effective QR pixel scale. The pixel scale is a presentational setting, not a host constant: it
    /// is owned by a level (core/tenant, application, ui_config) and is cut by the <c>channel</c>
    /// dimension inside that level.
    /// </summary>
    private readonly AuthPageSettingsResolver _pageSettings;

    /// <summary>
    /// Creates the QR code generation service.
    /// </summary>
    /// <param name="pageSettings">Resolver of the level-owned sign-in page settings.</param>
    public QrCodeService(AuthPageSettingsResolver pageSettings)
    {
        _pageSettings = pageSettings ?? throw new ArgumentNullException(nameof(pageSettings));
    }

    /// <inheritdoc />
    public async ValueTask<string> GenerateBase64PngAsync(
        string content,
        string channelType,
        ResolutionContext resolution,
        CancellationToken cancellationToken = default)
    {
        // The method creates the QR code and converts it to Base64 for embedding into HTML.
        // The pixel scale decides the resolution of the source PNG: width = total modules (quiet zone
        // included) × scale. It is resolved per request because the owning level is a runtime dimension
        // (self-hosted: the tenant level is empty and the core value applies, CFG-234), and it is
        // asked for THIS channel: the channel is a dimension of the key (CFG-237), so the service picks
        // nothing out of a resolved value itself. The context arrives from the caller — it is built
        // ONCE at the entry of the sign-in page and carries the ownership of the request.
        var pixelsPerModule = await _pageSettings.ResolvePixelsPerModuleAsync(
            resolution,
            channelType,
            cancellationToken);

        using var qrCodeData = CreateQrCodeData(content);

        using var qrCode = new PngByteQRCode(qrCodeData);
        var qrCodeBytes = qrCode.GetGraphic(pixelsPerModule);

        return Convert.ToBase64String(qrCodeBytes);
    }

    /// <inheritdoc />
    public async ValueTask<QrModuleMatrix> GenerateModuleMatrixAsync(
        string content,
        string channelType,
        ResolutionContext resolution,
        CancellationToken cancellationToken = default)
    {
        // The same two steps as the PNG above — the scale resolved for this channel, the code generated
        // at the same error correction level — with the matrix copied out instead of drawn. The matrix
        // QRCoder generates already carries the quiet zone, the one its PNG draws by default.
        var pixelsPerModule = await _pageSettings.ResolvePixelsPerModuleAsync(
            resolution,
            channelType,
            cancellationToken);

        using var qrCodeData = CreateQrCodeData(content);

        var rows = qrCodeData.ModuleMatrix;
        var moduleCount = rows.Count;
        var darkModules = new bool[moduleCount * moduleCount];

        for (var row = 0; row < moduleCount; row++)
        {
            var modules = rows[row];

            for (var column = 0; column < moduleCount; column++)
            {
                darkModules[(row * moduleCount) + column] = modules[column];
            }
        }

        return new QrModuleMatrix(darkModules, moduleCount, pixelsPerModule);
    }

    /// <summary>
    /// Generates the QR data of a content — the one place that picks the error correction level.
    /// </summary>
    /// <param name="content">QR code content.</param>
    /// <returns>The QR data; the caller owns it.</returns>
    private static QRCodeData CreateQrCodeData(string content)
    {
        using var qrGenerator = new QRCodeGenerator();

        return qrGenerator.CreateQrCode(
            content,
            QRCodeGenerator.ECCLevel.M);
    }
}
