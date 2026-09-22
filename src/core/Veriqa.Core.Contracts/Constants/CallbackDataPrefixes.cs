// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.ChannelAdapter.Constants;

/// <summary>
/// Ownership markers Veriqa puts into the payload of its own messenger buttons and deep links
/// (SPEC-003 §21.3, CA-194). They are the same for every channel: a router that has to tell a
/// Veriqa update from the integrator's own bot matches against these values, so they are contract,
/// not a per-adapter convention. A payload outside this registry is somebody else's event.
/// </summary>
public static class CallbackDataPrefixes
{
    /// <summary>
    /// Vendor namespace of the markers. Every button payload starts with it, which is what makes
    /// a Veriqa press distinguishable from a press on the integrator's own button — plain English
    /// words are not. Build your own marker from this prefix instead of repeating its value.
    /// </summary>
    public const string Vendor = "vq_";

    /// <summary>
    /// Operation confirmation prefix.
    /// </summary>
    public const string Confirm = Vendor + "confirm_";

    /// <summary>
    /// Operation decline prefix.
    /// </summary>
    public const string Decline = Vendor + "decline_";

    /// <summary>
    /// Authentication start prefix, used by deep links. Deliberately outside the vendor namespace:
    /// it travels inside the pre-filled text of a WhatsApp deep link, whose length is capped by the
    /// QR scannability budget (SPEC-003 §10.4.4). What narrows it instead is the value that follows
    /// it — a 43-character Base62 transaction identifier.
    /// </summary>
    public const string Auth = "auth_";
}
