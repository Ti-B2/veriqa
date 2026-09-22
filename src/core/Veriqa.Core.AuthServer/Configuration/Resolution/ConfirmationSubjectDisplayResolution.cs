// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.AuthServer.Configuration.Enums;
using Veriqa.Core.Configuration;

namespace Veriqa.Core.AuthServer.Configuration.Resolution;

/// <summary>
/// Reading of the subject-display axis (SPEC-012 §4.11): whether a surface is one the deployment
/// lets the subject of a confirmation be shown on.
/// <para>
/// A helper and deliberately not a seam, for the same reason the channel capability's is: the
/// narrowing this axis is about is stated by the KEY's own declaration and applied by the canonical
/// resolver, so a substitutable interface here would add nothing and would let an answer bypass it
/// (anti-fork CFG-235).
/// </para>
/// </summary>
internal static class ConfirmationSubjectDisplayResolution
{
    /// <summary>
    /// Checks whether the subject may be shown on the given surface.
    /// </summary>
    /// <param name="resolver">Canonical layer resolver.</param>
    /// <param name="context">Ownership context the surface belongs to (the transaction's).</param>
    /// <param name="surface">Surface asking whether it shows the subject.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> when the effective set contains the surface.</returns>
    internal static async ValueTask<bool> ShowsSubjectOnAsync(
        this IConfigurationResolver resolver,
        ResolutionContext context,
        ConfirmationSubjectSurface surface,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(context);

        var surfaces = await resolver.ResolveAsync(
            ConfirmationSubjectDisplayConfigKeys.Surfaces,
            context,
            ConfigDimensionValues.None,
            cancellationToken);

        // No level stated a set — the subject is shown where it is asked about and nowhere else
        // (the empty set of CFG-105, not "everywhere").
        return surfaces.Value?.Contains(surface) == true;
    }
}
