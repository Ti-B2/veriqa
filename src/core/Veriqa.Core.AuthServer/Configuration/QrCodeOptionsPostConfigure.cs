// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Options;

namespace Veriqa.Core.AuthServer.Configuration;

/// <summary>
/// PostConfigure that normalizes the QR group of the global sign-in page section.
/// Fills the null per-channel map with an empty one before validation runs — normalization is the job
/// of IPostConfigureOptions, not IValidateOptions.
/// <para>
/// The type is internal, like the validator it runs before: it is consumed only through the public
/// IPostConfigureOptions&lt;QrCodeOptions&gt; contract it implements, and it is registered by
/// AddVeriqaConfiguration, which is internal to this assembly. The constructor stays public so the DI
/// container can activate the type as usual.
/// </para>
/// </summary>
internal sealed class QrCodeOptionsPostConfigure : IPostConfigureOptions<QrCodeOptions>
{
    /// <summary>
    /// Normalizes the options: replaces the null per-channel map with an empty one.
    /// </summary>
    /// <param name="name">Named options instance name.</param>
    /// <param name="options">Options instance to normalize.</param>
    public void PostConfigure(string? name, QrCodeOptions options)
    {
        // The configuration binder may return null both for an explicit "PixelsPerModuleByChannel": null
        // and for a completely missing map (when the parent object was overridden by the binder).
        options.PixelsPerModuleByChannel ??=
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }
}
