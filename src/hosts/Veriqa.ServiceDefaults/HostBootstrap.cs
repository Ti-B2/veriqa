// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.Hosting;
using Serilog;

namespace Veriqa.ServiceDefaults;

/// <summary>
/// Shared bootstrap wrapper for host entry points: UTF-8 console, the Serilog bootstrap logger,
/// interception of a critical startup error, and a guaranteed flush of the logs.
/// Used by hosts that consume ServiceDefaults (the core host Veriqa.Core.AuthServer.Host and the
/// research demo Veriqa.Demo.Web here; the site host consumes it as a package from the veriqa/site
/// repository) — prevents drift of wrapper copies in their Program.cs.
/// </summary>
public static class HostBootstrap
{
    /// <summary>
    /// Process exit code of a host that finished normally (the delegate returned without throwing).
    /// </summary>
    public const int SuccessExitCode = 0;

    /// <summary>
    /// Process exit code of a host that did not start because of a critical error.
    /// Non-zero by the POSIX/Windows convention: a failed start must be distinguishable
    /// from a normal shutdown for the supervisor that launched the process.
    /// </summary>
    public const int FatalErrorExitCode = 1;

    /// <summary>
    /// Builds and runs the host under the protection of the Serilog bootstrap logger.
    /// </summary>
    /// <remarks>
    /// Order: UTF-8 for the console when the process has one (otherwise Cyrillic in logs on Windows
    /// is displayed incorrectly; under a service manager there is no console and the step is
    /// skipped) → bootstrap logger (catches configuration errors before the host is built) →
    /// run; a critical error is also written to stderr (Serilog may not be
    /// configured — e.g. the Serilog section is missing and Log.Fatal produces no
    /// output) → final <see cref="Log.CloseAndFlush"/>.
    /// </remarks>
    /// <param name="fatalErrorMessage">Critical startup error message (for stderr and Log.Fatal).</param>
    /// <param name="buildAndRunHost">Delegate that builds and runs the host (the body of Program.cs).</param>
    /// <returns>
    /// <see cref="SuccessExitCode"/> when the delegate returned normally (including a shutdown
    /// via SIGTERM / Ctrl+C); <see cref="FatalErrorExitCode"/> when a critical startup error
    /// was intercepted. The caller returns this value from the entry point so that it reaches
    /// the process. A <see cref="HostAbortedException"/> is not a startup failure: design-time
    /// tools (e.g. <c>dotnet ef</c>) throw it to stop the host once they have obtained it, so it
    /// propagates to the caller instead of being reported as a critical error.
    /// </returns>
    public static int Run(string fatalErrorMessage, Action buildAndRunHost)
    {
        // A process running under a service manager (Windows SCM, systemd) has no console attached.
        // The setter calls SetConsoleOutputCP, which fails there with "the handle is invalid", and an
        // exception thrown here would end the process before the bootstrap logger exists — the
        // supervisor would see a bare crash with no diagnostics at all. The code page only matters
        // where a console is actually attached, so failing to set it is not a startup failure.
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch (IOException)
        {
            // No console to set the code page on; the logs still go to the sinks configured below.
        }

        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateBootstrapLogger();

        try
        {
            buildAndRunHost();
            return SuccessExitCode;
        }
        catch (Exception ex) when (ex is not HostAbortedException)
        {
            Console.Error.WriteLine($"{fatalErrorMessage}: {ex}");
#pragma warning disable Serilog004 // The message template comes as a parameter — it has no placeholders
            Log.Fatal(ex, fatalErrorMessage);
#pragma warning restore Serilog004
            return FatalErrorExitCode;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
