// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Options;

namespace Veriqa.Core.AuthServer.Configuration;

/// <summary>
/// Validator of the QR group of the GLOBAL sign-in page section (SPEC-012 §8.1/§8.2).
/// Performs the critical range check at application startup via ValidateOnStart.
/// <para>
/// It stands over <see cref="QrCodeOptions"/> on its own section rather than over the root options
/// class: the section the scale is written in is read by the canonical resolver, level by level, and
/// the root class no longer carries a field for it (see <see cref="VeriqaOptions"/>). The check itself
/// did not move an inch — it is the critical check of the global section SPEC-012 §8.2 keeps apart
/// from the snapshot-boundary check that walks every level.
/// </para>
/// <para>
/// The type is internal: it is consumed only through the public IValidateOptions&lt;QrCodeOptions&gt;
/// contract it implements, and it is registered by AddVeriqaConfiguration, which is internal to this
/// assembly. The constructor stays public so the DI container can activate the type as usual.
/// </para>
/// </summary>
internal sealed class QrCodeOptionsValidator : IValidateOptions<QrCodeOptions>
{
    /// <summary>
    /// Validates the QR settings of the global sign-in page section.
    /// </summary>
    /// <param name="name">Options instance name.</param>
    /// <param name="options">Options instance to validate.</param>
    /// <returns>Validation result.</returns>
    public ValidateOptionsResult Validate(string? name, QrCodeOptions options)
    {
        var failures = new List<string>();

        ValidateQrCode(options, failures);

        if (failures.Count > 0)
        {
            return ValidateOptionsResult.Fail(failures);
        }

        return ValidateOptionsResult.Success;
    }

    /// <summary>
    /// Fails the start when a QR pixel scale — the section default or any per-channel override — falls
    /// outside the allowed range (SPEC-015 §4.2, fail-fast SPEC-012 §8.1). The two bounds guard against
    /// different mistakes and are derived from different boxes: below the floor the sparsest QR the core
    /// ships is upscaled into the SHIPPED display box and its module grid blurs, while the ceiling is
    /// the highest scale the documented rule "scale ≥ box ÷ the channel QR's module count" can ask for
    /// on the largest box the setting is sized for — a value above it serves no box a page can show and
    /// is a typo. Weight of the inlined PNG is not what the ceiling protects (see
    /// <see cref="QrCodeOptions.MaxPixelsPerModule"/>).
    /// A silent clamp is deliberately not used: an operator who asked for a scale the core cannot honor
    /// must learn it at startup, not from a QR that scans worse in the field.
    /// <para>
    /// This is a RANGE check and nothing more. Whether a scale actually fills the box of a given
    /// channel's QR cannot be decided here: the box may be redefined by a stylesheet the core does not
    /// read, and a third-party channel's module count is unknown to the core, so a passing configuration
    /// can still upscale such a channel — even in the shipped box, at the shipped default. That relation
    /// is documented instead (<see cref="QrCodeOptions.PixelsPerModule"/> and the custom-channel guide).
    /// </para>
    /// </summary>
    /// <param name="qrCode">QR generation settings.</param>
    /// <param name="failures">Failure list to append to.</param>
    private static void ValidateQrCode(QrCodeOptions qrCode, List<string> failures)
    {
        // Normalization (PixelsPerModuleByChannel ??= empty) is done in QrCodeOptionsPostConfigure

        if (qrCode.PixelsPerModule is < QrCodeOptions.MinPixelsPerModule
            or > QrCodeOptions.MaxPixelsPerModule)
        {
            failures.Add(
                $"{QrCodeOptions.SectionName}:PixelsPerModule is {qrCode.PixelsPerModule}, outside the "
                + $"allowed range {QrCodeOptions.MinPixelsPerModule}..{QrCodeOptions.MaxPixelsPerModule} "
                + "px per module.");
        }

        foreach (var (channelType, pixelsPerModule) in qrCode.PixelsPerModuleByChannel)
        {
            if (pixelsPerModule is < QrCodeOptions.MinPixelsPerModule
                or > QrCodeOptions.MaxPixelsPerModule)
            {
                failures.Add(
                    $"{QrCodeOptions.SectionName}:PixelsPerModuleByChannel:{channelType} is "
                    + $"{pixelsPerModule}, outside the allowed range {QrCodeOptions.MinPixelsPerModule}.."
                    + $"{QrCodeOptions.MaxPixelsPerModule} px per module.");
            }
        }
    }
}
