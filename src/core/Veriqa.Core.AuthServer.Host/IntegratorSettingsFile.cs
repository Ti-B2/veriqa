// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;

namespace Veriqa.Core.AuthServer.Host;

/// <summary>
/// The JSON settings file of the integrator: a file of its own, layered over the settings the host
/// ships with, so that providing clients or any other Veriqa setting never means replacing
/// <c>appsettings.{Environment}.json</c> — the file that carries the production defaults of the image
/// (logging levels, store providers, token lifetimes). The file at the default location is optional;
/// a location named explicitly through <see cref="PathEnvironmentVariable"/> is required.
/// </summary>
internal static class IntegratorSettingsFile
{
    /// <summary>
    /// Environment variable that overrides the location of the file. An absolute path is taken as is,
    /// a relative one is resolved against the content root. When the variable is set, a missing file
    /// stops the host at startup.
    /// </summary>
    public const string PathEnvironmentVariable = "VERIQA_SETTINGS_FILE";

    /// <summary>
    /// Location of the file when the variable is not set, relative to the content root. In the
    /// container image the content root is <c>/app</c>, which makes it <c>/app/config/veriqa.json</c>.
    /// </summary>
    public const string DefaultRelativePath = "config/veriqa.json";

    /// <summary>
    /// Adds the file to the configuration of the host. Precedence, weakest to strongest:
    /// <c>appsettings.json</c> → <c>appsettings.{Environment}.json</c> → the files of
    /// <see cref="ExternalConfigurationDirectory"/> → user secrets (Development only) → this file →
    /// environment variables → command-line arguments. The file is inserted in
    /// front of the unprefixed environment variables rather than appended, so that a key passed through
    /// the environment or the command line still wins over it.
    /// </summary>
    /// <remarks>
    /// Changes are watched in the directory of the file only, never in an ancestor: the watcher is
    /// recursive, and over a large tree it slows the startup down or exhausts the inotify watches.
    /// When the default directory is absent the file is not added at all — there is nothing to read
    /// and nothing to watch; when the file lies directly in a filesystem root it is read without
    /// watching.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The variable is set and no file exists at the path it resolves to.
    /// </exception>
    public static void AddIntegratorSettingsFile(this WebApplicationBuilder builder)
    {
        var configuredPath = Environment.GetEnvironmentVariable(PathEnvironmentVariable);
        var isConfigured = !string.IsNullOrWhiteSpace(configuredPath);
        var relativeOrAbsolutePath = isConfigured ? configuredPath! : DefaultRelativePath;

        // Path.Combine returns the second argument unchanged when it is rooted.
        var fullPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, relativeOrAbsolutePath));

        if (isConfigured && !File.Exists(fullPath))
        {
            throw new InvalidOperationException(
                $"The integrator settings file '{fullPath}' named by the environment variable " +
                $"{PathEnvironmentVariable} does not exist. Mount the file at that path or unset the variable.");
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (directory is null || !Directory.Exists(directory))
        {
            return;
        }

        var isFilesystemRoot = string.Equals(Path.GetPathRoot(directory), directory, StringComparison.Ordinal);

        var fileSource = new JsonConfigurationSource
        {
            // No exclusion filters: the startup check above accepts a hidden or dot-prefixed file, so the
            // provider must read it too rather than silently skip it.
            FileProvider = new PhysicalFileProvider(directory, ExclusionFilters.None),
            Path = Path.GetFileName(fullPath),
            Optional = true,
            ReloadOnChange = !isFilesystemRoot,
        };

        var sources = builder.Configuration.Sources;
        var environmentVariablesIndex = FindLastUnprefixedEnvironmentVariablesSource(sources);

        if (environmentVariablesIndex < 0)
        {
            sources.Add(fileSource);
        }
        else
        {
            sources.Insert(environmentVariablesIndex, fileSource);
        }
    }

    // The host-level environment sources carry the DOTNET_ / ASPNETCORE_ prefixes; the application-level
    // one that maps Veriqa__* keys has none, and the command line follows it.
    private static int FindLastUnprefixedEnvironmentVariablesSource(IList<IConfigurationSource> sources)
    {
        for (var index = sources.Count - 1; index >= 0; index--)
        {
            if (sources[index] is EnvironmentVariablesConfigurationSource { Prefix: null or "" })
            {
                return index;
            }
        }

        return -1;
    }
}
