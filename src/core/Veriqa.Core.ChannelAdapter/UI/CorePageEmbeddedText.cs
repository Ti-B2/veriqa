// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;
using System.Reflection;

namespace Veriqa.Core.ChannelAdapter.UI;

/// <summary>
/// Reader of the text resources the pages of the "Core" contour are built from — the stylesheets of
/// <see cref="CorePageStyles"/> and the client script of the sign-in window. None of them is served
/// over a URL: their content is inlined into the generated document, so the page stays
/// self-contained (SPEC-015 §1) while the sources themselves live in real .css and .js files that a
/// linter, an editor and a reviewer can read.
/// <para>
/// Inlining has a price for the comments of those files: each of them reaches the end user's
/// browser verbatim and is readable through "view source". A resource comment therefore explains the
/// rule it stands next to and names no internal design document; the norms a resource implements are
/// named in the C# type that loads it (<see cref="CorePageStyles"/>, and in Veriqa.Core.AuthServer
/// the stylesheet and script loaders of the sign-in window).
/// </para>
/// <para>
/// The reader takes the assembly as an argument because the resources of the contour are spread over
/// the assemblies that own them: the shared stylesheets travel in this one, the window's own
/// stylesheet and script in Veriqa.Core.AuthServer. It lives here because this is the lowest project
/// both page families reference, and it is internal for the same reason the stylesheets are — loading
/// resources is an implementation detail of the contour and not a seam for integrators.
/// </para>
/// </summary>
internal static class CorePageEmbeddedText
{
    /// <summary>
    /// Cache of loaded resources: the content is immutable, so it is read from the assembly once.
    /// The key includes the assembly identity — the reader serves resources of several assemblies.
    /// </summary>
    private static readonly ConcurrentDictionary<string, string> LoadedResources = new(StringComparer.Ordinal);

    /// <summary>
    /// Reads an embedded text resource and caches its content.
    /// </summary>
    /// <param name="assembly">Assembly holding the resource.</param>
    /// <param name="resourceName">Full logical name of the resource.</param>
    /// <returns>Content of the resource.</returns>
    /// <exception cref="InvalidOperationException">The resource is missing from the assembly.</exception>
    internal static string Load(Assembly assembly, string resourceName)
    {
        // The method reads the resource once and serves the cached copy afterwards
        return LoadedResources.GetOrAdd(
            $"{assembly.FullName}|{resourceName}",
            _ => ReadResource(assembly, resourceName));
    }

    /// <summary>
    /// Reads the resource stream into a string.
    /// </summary>
    private static string ReadResource(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' was not found in assembly '{assembly.GetName().Name}'.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
