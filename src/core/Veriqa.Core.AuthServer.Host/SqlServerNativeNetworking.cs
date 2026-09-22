// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Data.SqlClient;

namespace Veriqa.Core.AuthServer.Host;

/// <summary>
/// Startup check of the native network library the SQL Server driver needs on Windows.
/// </summary>
/// <remarks>
/// On Windows, Microsoft.Data.SqlClient reaches SQL Server through Microsoft.Data.SqlClient.SNI.dll, a
/// native library under the Microsoft Software License Terms (on other systems the driver uses its managed
/// implementation and needs nothing). The Windows service archive of this host does not ship that library:
/// the operator who selects SQL Server downloads it from nuget.org and places it next to the executable,
/// accepting its terms. Without it the driver would fail only at the first connection with a bare
/// DllNotFoundException; this check refuses to start instead and says what to download and where to put it.
/// </remarks>
internal static class SqlServerNativeNetworking
{
    /// <summary>
    /// File name of the native library, as the driver imports it.
    /// </summary>
    public const string LibraryFileName = "Microsoft.Data.SqlClient.SNI.dll";

    /// <summary>
    /// NuGet package that carries the library under runtimes/&lt;rid&gt;/native.
    /// </summary>
    public const string PackageId = "Microsoft.Data.SqlClient.SNI.runtime";

    /// <summary>
    /// Driver switch that selects its managed network implementation on Windows. Microsoft documents that
    /// mode as intended for testing and debugging; an operator who sets it has made that choice, and the
    /// native library is not loaded at all then.
    /// </summary>
    public const string ManagedNetworkingSwitch = "Switch.Microsoft.Data.SqlClient.UseManagedNetworkingOnWindows";

    /// <summary>
    /// Package page of the driver on nuget.org; the dependencies tab names the version of
    /// <see cref="PackageId"/> the driver was built against. The driver version is appended.
    /// </summary>
    private const string DriverPackagePageBaseUrl = "https://www.nuget.org/packages/Microsoft.Data.SqlClient/";

    /// <summary>
    /// Throws when SQL Server is selected on Windows and the native library cannot be loaded.
    /// Does nothing on other systems and when the managed network switch is on.
    /// </summary>
    /// <exception cref="InvalidOperationException">The native library is missing on Windows.</exception>
    public static void EnsureAvailable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (AppContext.TryGetSwitch(ManagedNetworkingSwitch, out var useManagedNetworking) && useManagedNetworking)
        {
            return;
        }

        var driverAssembly = typeof(SqlConnection).Assembly;

        // The same resolution the driver's own P/Invoke uses: the application directory and the native
        // asset paths of the deps file — a run from the IDE keeps the library under runtimes/<rid>/native.
        if (NativeLibrary.TryLoad(LibraryFileName, driverAssembly, searchPath: null, out _))
        {
            return;
        }

        var driverVersion = GetDriverVersion(driverAssembly);

        throw new InvalidOperationException(
            $"SQL Server is selected, but the native library '{LibraryFileName}' is not found. "
            + $"The Windows build of Veriqa does not include it: it is Microsoft's, under the Microsoft Software License Terms. "
            + $"Download the NuGet package '{PackageId}' in the version Microsoft.Data.SqlClient {driverVersion} depends on "
            + $"({DriverPackagePageBaseUrl}{driverVersion}), and copy runtimes\\{RuntimeInformation.RuntimeIdentifier}\\native\\{LibraryFileName} "
            + $"from it next to the executable ({AppContext.BaseDirectory}).");
    }

    /// <summary>
    /// Returns the package version of the driver: its informational version without the source revision
    /// suffix, or the assembly version when the attribute is absent.
    /// </summary>
    /// <param name="driverAssembly">Microsoft.Data.SqlClient assembly.</param>
    /// <returns>Version string, for example <c>7.0.3</c>.</returns>
    private static string GetDriverVersion(Assembly driverAssembly)
    {
        var informationalVersion = driverAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrEmpty(informationalVersion))
        {
            return driverAssembly.GetName().Version?.ToString() ?? string.Empty;
        }

        var sourceRevisionStart = informationalVersion.IndexOf('+', StringComparison.Ordinal);

        return sourceRevisionStart < 0 ? informationalVersion : informationalVersion[..sourceRevisionStart];
    }
}
