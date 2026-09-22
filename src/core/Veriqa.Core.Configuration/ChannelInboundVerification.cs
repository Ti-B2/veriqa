// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// How the authenticity of an INCOMING channel event is verified, as the integrator declares it for a
/// channel (SPEC-003 §8, SPEC-012 CFG-247). It is a DECLARED FACT of the configuration and never
/// something the core derives: the adapter's own statement about itself would guarantee nothing (the
/// core does not verify WHICH check ran, only that one ran and passed), and deriving it here would
/// mean a branch per channel type in the core.
/// <para>
/// The one who declares it is the one who carries the risk — the integrator who brought the bot and
/// chose the transport. Our side of the bargain is the documentation of the shipped channels, which
/// names the value for every channel and mode.
/// </para>
/// <para>
/// It lives in the settings mechanism for the same reason the Logging axis does
/// (<see cref="LoggingConfigKeys"/>): both consumers of the value — the channel contour, which
/// refuses to start a deployment that never declared it, and the audit trail, which writes it into a
/// record — are contours that do not depend on one another, and this assembly is the only floor both
/// already stand on. A product type in a mechanism assembly is the exception this axis buys, not a
/// new norm.
/// </para>
/// </summary>
public enum ChannelInboundVerification
{
    /// <summary>
    /// The body of the incoming request is signed with a key of the platform (HMAC and the like), and
    /// the signature is verified.
    /// </summary>
    Signature,

    /// <summary>
    /// The incoming request presents a shared secret (a header, a token) that is compared with the one
    /// the deployment configured.
    /// </summary>
    SharedSecret,

    /// <summary>
    /// There are no incoming requests at all — the product fetches the events with an outgoing request
    /// of its own (polling).
    /// </summary>
    OutboundFetch,

    /// <summary>
    /// The authenticity of an incoming event is not verified.
    /// </summary>
    None
}
