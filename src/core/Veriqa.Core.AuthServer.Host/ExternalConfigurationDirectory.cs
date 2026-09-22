// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;

namespace Veriqa.Core.AuthServer.Host;

/// <summary>
/// An external directory of configuration files. An installation of the host as an OS service keeps
/// its settings outside the program directory (<c>/etc/veriqa</c>, <c>%ProgramData%\Veriqa</c>), so
/// that replacing the program on an update never overwrites the configuration of the installation.
/// The directory is named by <see cref="PathEnvironmentVariable"/>; with the variable unset the host
/// reads exactly what it read before, which keeps the container image and a local run unchanged.
/// </summary>
internal static class ExternalConfigurationDirectory
{
    /// <summary>
    /// Environment variable naming the directory. The value is an absolute path; a relative one is
    /// resolved against the current directory of the process. This is not a <c>Veriqa:</c>
    /// configuration key: it says where the configuration is read from, so it cannot itself be read
    /// from the configuration.
    /// </summary>
    public const string PathEnvironmentVariable = "VERIQA_CONFIG_DIRECTORY";

    /// <summary>Name of the environment-independent file read from the directory.</summary>
    private const string BaseFileName = "appsettings.json";

    /// <summary>
    /// Adds <c>appsettings.json</c> and <c>appsettings.{EnvironmentName}.json</c> of the directory to
    /// the configuration of the host. Both files are optional: a directory prepared by the installer
    /// but not yet filled in is not a startup failure. Precedence, weakest to strongest:
    /// <c>appsettings.json</c> of the content root → <c>appsettings.{Environment}.json</c> of the
    /// content root → <c>appsettings.json</c> of the directory →
    /// <c>appsettings.{Environment}.json</c> of the directory → user secrets (Development only) →
    /// environment variables → command-line arguments. The two sources are inserted right after the
    /// files of the content root rather than appended, so that a key passed through the environment or
    /// the command line still wins over the directory — the documented way of configuring the host.
    /// </summary>
    /// <remarks>
    /// Both files are served by one <see cref="PhysicalFileProvider"/>: a single watcher over the
    /// directory instead of two.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The variable names a directory that does not exist. Ignoring it silently would start the
    /// service on the shipped defaults while the operator believes it runs on the configuration of the
    /// installation, so the host refuses to start instead.
    /// </exception>
    public static void AddExternalConfigurationDirectory(this WebApplicationBuilder builder)
    {
        var configuredDirectory = Environment.GetEnvironmentVariable(PathEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(configuredDirectory))
        {
            return;
        }

        // Directory.Exists answers false for a malformed path instead of throwing, so this one check
        // also covers a value that is not a usable path at all.
        if (!Directory.Exists(configuredDirectory))
        {
            throw new InvalidOperationException(
                $"{PathEnvironmentVariable} points to '{configuredDirectory}', which does not exist. " +
                "Create the directory or unset the variable.");
        }

        // PhysicalFileProvider requires a rooted path; a relative value is resolved the same way
        // Directory.Exists above resolved it.
        var fullPath = Path.GetFullPath(configuredDirectory);
        var environmentFileName = $"appsettings.{builder.Environment.EnvironmentName}.json";
        var sources = builder.Configuration.Sources;
        var (insertIndex, reloadOnChange) = FindContentRootAnchor(sources, environmentFileName);

        // Watching a filesystem root means a recursive watch over the whole disk: it slows the startup
        // down or exhausts the inotify watches. Such a directory is read once, without watching.
        reloadOnChange &= !string.Equals(Path.GetPathRoot(fullPath), fullPath, StringComparison.Ordinal);

        var fileProvider = new PhysicalFileProvider(fullPath);

        sources.Insert(insertIndex, CreateSource(fileProvider, BaseFileName, reloadOnChange));
        sources.Insert(insertIndex + 1, CreateSource(fileProvider, environmentFileName, reloadOnChange));
    }

    private static JsonConfigurationSource CreateSource(IFileProvider fileProvider, string fileName, bool reloadOnChange) =>
        new()
        {
            FileProvider = fileProvider,
            Path = fileName,
            Optional = true,
            ReloadOnChange = reloadOnChange,
        };

    // The position right after the appsettings files of the content root, plus the reloadOnChange they
    // were added with: the directory layers over those files and keeps their reload behaviour.
    // Scanning backwards finds the environment-specific file first — it is added after the base one.
    // With neither file present (a builder whose sources were replaced) the directory goes first: what
    // must hold in every case is that it does not end up over the environment variables.
    private static (int InsertIndex, bool ReloadOnChange) FindContentRootAnchor(
        IList<IConfigurationSource> sources,
        string environmentFileName)
    {
        for (var index = sources.Count - 1; index >= 0; index--)
        {
            if (sources[index] is JsonConfigurationSource { Path: { } path } source
                && (string.Equals(path, environmentFileName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(path, BaseFileName, StringComparison.OrdinalIgnoreCase)))
            {
                return (index + 1, source.ReloadOnChange);
            }
        }

        return (0, true);
    }
}
