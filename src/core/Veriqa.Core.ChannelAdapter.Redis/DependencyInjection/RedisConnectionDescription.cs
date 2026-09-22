// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using StackExchange.Redis;

namespace Veriqa.Core.ChannelAdapter.DependencyInjection;

/// <summary>
/// Renders a Redis connection string as the settings that tell one connection from another, so a
/// configuration error can name the connection it is talking about without printing credentials.
/// </summary>
/// <remarks>
/// <para>
/// A Redis connection string routinely carries <c>password=…</c>, and neither
/// <c>ConfigurationOptions.ToString()</c> nor <c>IConnectionMultiplexer.Configuration</c> removes it
/// (verified against StackExchange.Redis 2.8.41: both echo the password verbatim). Since a start-up
/// failure lands in the log, the rendering is built as an allow-list: it copies the few settings an
/// operator needs to tell one Redis from another and drops everything else, rather than trying to
/// strike out the secret keywords it knows about — an allow-list stays safe when the client gains a
/// new option, a deny-list does not.
/// </para>
/// <para>
/// Beyond the endpoints, the allow-list carries the settings that can make two connections to the
/// same endpoints different targets: the logical database, TLS, and the sentinel master name. Two
/// connections that agree on all of them address the same data, so nothing further is needed to
/// tell an operator which registration to remove.
/// </para>
/// </remarks>
internal static class RedisConnectionDescription
{
    /// <summary>
    /// Stand-in for a connection string that cannot be described (it does not parse).
    /// </summary>
    private const string Unknown = "<connection could not be described>";

    /// <summary>
    /// Stand-in for a connection string that cannot be printed without leaking credentials.
    /// </summary>
    private const string Redacted = "<redacted>";

    /// <summary>
    /// Describes a Redis connection by the settings that identify the data it points at.
    /// </summary>
    /// <param name="configuration">Redis connection string.</param>
    /// <returns>The description of the connection, or a stand-in when it cannot be printed safely.</returns>
    public static string Describe(string? configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration))
        {
            return Unknown;
        }

        try
        {
            var parsed = ConfigurationOptions.Parse(configuration);

            // Copy the allow-listed settings onto an otherwise empty instance, so anything the
            // string carries beyond them — credentials included — cannot reach the rendering.
            var identifying = new ConfigurationOptions
            {
                DefaultDatabase = parsed.DefaultDatabase,
                ServiceName = parsed.ServiceName
            };

            // Ssl is not nullable on the way out and SslHost is inferred from the endpoints when it
            // was not set, so both are copied only for a TLS connection: an unconditional copy would
            // print "ssl=False" plus a host the operator never configured on every plain connection.
            if (parsed.Ssl)
            {
                identifying.Ssl = true;
                identifying.SslHost = parsed.SslHost;
            }

            foreach (var endpoint in parsed.EndPoints)
            {
                identifying.EndPoints.Add(endpoint);
            }

            var description = identifying.ToString();

            // A "user:password@host" endpoint is not one we can print: StackExchange.Redis does not
            // understand the URI form and keeps such a string as a host name verbatim, credentials
            // included.
            return string.IsNullOrEmpty(description) || description.Contains('@', StringComparison.Ordinal)
                ? Redacted
                : description;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Parsing rejects malformed input; the error being reported is a configuration error, and
            // it must not be replaced by a parsing failure.
            return Unknown;
        }
    }
}
