// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.AuthServer.Constants;

namespace Veriqa.Core.AuthServer.Configuration;

/// <summary>
/// QR settings of the authentication page — the presentational group of the channel outbound
/// presentation (SPEC-003 §17.3, SPEC-012 §10.3). Configuration section:
/// <see cref="SectionName"/>. The section carries what the QR image is made of
/// (<see cref="PixelsPerModule"/> and its per-channel map); whether the page shows it at all is a
/// setting of the same group read BY PATH, so the section is no longer bound into an object for it.
/// It is the shape of the SECTION an operator writes; what the page is handed is each of those
/// settings resolved on its own, not this object as a whole.
/// <para>
/// The setting has two axes. Ownership levels (core → tenant → application → ui_config) are applied
/// by the canonical resolver PER FIELD (SPEC-012 §10.6): the visibility and the scale are keys of
/// their own, and the first level that states one wins it alone — a level that states the visibility
/// keeps the scale of the level below it. A level states its own part through
/// <see cref="QrCodeOverrideOptions"/>. The channel is the second axis and is a DIMENSION of the
/// scale key (SPEC-012 CFG-237): <see cref="PixelsPerModuleByChannel"/> is how a level states the
/// scale FOR one channel and <see cref="PixelsPerModule"/> how it states the scale for every other,
/// so both are read by the resolver — inside one level, before it drops to the level below (CFG-236).
/// Keying by channel type (rather than by a field in each channel's own options) is what makes the
/// per-channel scale available to third-party SPI channels, which have no options class in the core.
/// </para>
/// </summary>
public sealed class QrCodeOptions
{
    /// <summary>
    /// Configuration section name (SPEC-012 CFG-113). The section is bound HERE — the group is the one
    /// part of the sign-in page section still read as an input record, and it is no longer reached
    /// through a field of <see cref="VeriqaOptions"/> (see that class for why the root no longer carries
    /// the page design at all). This constant is the single place that spells the key out: the binding,
    /// the addresses the keys of the group declare for their core level and the startup validation
    /// messages are all built from it, so an operator is told the exact key to fix.
    /// </summary>
    public const string SectionName = "Veriqa:AuthPageDesign:QrCode";

    /// <summary>
    /// Default QR pixel scale (px per module) when no level and no channel override sets one. Like the
    /// lower bound, it is sized for the channels the core SHIPS: in the shipped display box
    /// (<see cref="AuthPageConstants.QrCodeImgAttributeSizePx"/>) it satisfies the rule
    /// "scale ≥ box ÷ the channel QR's module count" for a QR of ⌈220 ÷ 6⌉ = 37 modules or more, and the
    /// sparsest shipped channel QR has 45 (<see cref="SmallestChannelQrModules"/>). A QR SPARSER than
    /// 37 modules is upscaled by this default even in the shipped box, before the integrator touches the
    /// box at all, and needs its own entry in <see cref="PixelsPerModuleByChannel"/>. Which deep links
    /// get that sparse is stated in SPEC-015 §4.2 and in the custom-channel guide — the core cannot see
    /// a third-party channel's payload, so it neither knows nor checks that.
    /// </summary>
    public const int DefaultPixelsPerModule = 6;

    /// <summary>
    /// Total module count (quiet zone included) of the smallest channel QR shipped by the core —
    /// the bare deep-link payload (SPEC-003 §10.4.4). The LOWER bound is derived from it: having the
    /// fewest modules among the QRs the core ships, it needs the HIGHEST scale to reach the shipped
    /// display box without being upscaled, so among the SHIPPED QRs it is the one that binds
    /// "scale ↔ box". It does not bind that relation for a third-party channel, whose QR may be sparser
    /// still — see <see cref="DefaultPixelsPerModule"/> and <see cref="PixelsPerModule"/>.
    /// </summary>
    public const int SmallestChannelQrModules = 45;

    /// <summary>
    /// Total module count (quiet zone included) of the smallest QR that can exist at all: version 1 is
    /// 21 × 21 modules and the standard adds a four-module quiet zone on each side. The UPPER bound is
    /// derived from this number rather than from <see cref="SmallestChannelQrModules"/>, because a
    /// third-party SPI channel may encode a shorter deep link than any channel the core ships: the
    /// sparser the QR, the higher the scale it needs to fill a given box, and a ceiling derived from the
    /// shipped channels would forbid a value such a channel legitimately needs.
    /// </summary>
    public const int SmallestPossibleQrModules = 29;

    /// <summary>
    /// Largest QR display box (px) the setting is sized for. It is not the shipped box: an integrator
    /// may enlarge the box through <c>--veriqa-qr-size</c> in a custom stylesheet, and the scale has to
    /// keep up with it, so the upper bound must not be pinned to the box the core happens to ship.
    /// 1000 px is past the shorter side of a common desktop viewport, so no sign-in page can display a
    /// larger QR — a value beyond the resulting bound serves no box and is a typo.
    /// </summary>
    public const int LargestSupportedQrBoxPx = 1000;

    /// <summary>
    /// Lower bound of the pixel scale, derived from the shipped display box
    /// (<see cref="AuthPageConstants.QrCodeImgAttributeSizePx"/> — the default of the
    /// <c>--veriqa-qr-size</c> token): the smallest channel QR must not be UPSCALED into the box, since
    /// upscaling blurs the module grid (the page deliberately keeps a smooth resample and does not set
    /// <c>image-rendering: pixelated</c>). Ceiling of 220 ÷ 45 = 5 px per module. Being derived from the
    /// SHIPPED channels, it rules out upscaling for THOSE QRs only: a sparser third-party QR can still be
    /// upscaled inside the shipped box at a perfectly legal value, so a value inside the range is not by
    /// itself a QR that fits its box — see <see cref="PixelsPerModule"/>.
    /// </summary>
    public const int MinPixelsPerModule =
        (AuthPageConstants.QrCodeImgAttributeSizePx + SmallestChannelQrModules - 1) / SmallestChannelQrModules;

    /// <summary>
    /// Upper bound of the pixel scale: the scale that fills <see cref="LargestSupportedQrBoxPx"/> with
    /// the sparsest QR that can exist (<see cref="SmallestPossibleQrModules"/>), i.e. the highest value
    /// the documented rule "scale ≥ box ÷ the channel QR's module count" can ask for on any box a page
    /// is able to show, for any channel — including a third-party one whose QR the core has never seen.
    /// Ceiling of 1000 ÷ 29 = 35 px per module. The bound is deliberately NOT derived from the shipped
    /// box, nor from the shipped channels: an integrator who enlarges the box has to raise the scale
    /// with it, and a narrower ceiling would forbid the very value that rule prescribes. It is a guard
    /// against a typo, not a cost limit — Base64 weight does not bind it anywhere in the range, including
    /// its top: the densest channel QR (77 modules per SPEC-003 §10.4.4) inlines as ≈ 5.5 KiB at 23 px per
    /// module and ≈ 12.4…13.4 KiB at the ceiling itself, measured 2026-08-03 on the shipped QRCoder version
    /// over eight transaction ids. That worst case also needs a 2695 px box no page can show; a QR that
    /// genuinely needs the ceiling is a sparse one and renders around the 1000 px box.
    /// </summary>
    public const int MaxPixelsPerModule =
        (LargestSupportedQrBoxPx + SmallestPossibleQrModules - 1) / SmallestPossibleQrModules;

    /// <summary>
    /// QR pixel scale (px per module) applied to every channel that has no entry in
    /// <see cref="PixelsPerModuleByChannel"/>. Allowed range —
    /// [<see cref="MinPixelsPerModule"/>; <see cref="MaxPixelsPerModule"/>], checked at startup.
    /// <para>
    /// The lower bound comes from the SHIPPED display box (do not ship a QR that is upscaled out of the
    /// box), the upper one from the largest box the setting is sized for
    /// (<see cref="LargestSupportedQrBoxPx"/>) taken with the sparsest QR that can exist
    /// (<see cref="SmallestPossibleQrModules"/>), so the range holds every value the box-to-scale rule
    /// can prescribe for any channel. The rule itself — "box ≤ the channel QR's module count × this
    /// value", or the grid is upscaled and blurs — stays DOCUMENTED rather than enforced, because either
    /// side of it can leave the core's sight: the box goes into a custom stylesheet the core does not
    /// read, the module count into a third-party channel's deep link. Passing startup validation
    /// therefore means the value is in range, not that the QR fits its box: that check belongs to whoever
    /// knows both numbers, and the rule with its threshold is spelled out in SPEC-015 §4.2 and in the
    /// custom-channel guide.
    /// </para>
    /// </summary>
    public int PixelsPerModule { get; set; } = DefaultPixelsPerModule;

    /// <summary>
    /// Per-channel overrides of <see cref="PixelsPerModule"/>, keyed by channel type
    /// (<c>telegram</c>, <c>whatsapp</c>, a third-party SPI channel type). A channel absent from the map
    /// takes <see cref="PixelsPerModule"/>. Every value is bound by the same range as the default.
    /// <para>
    /// This map is where a channel the level default does not fit gets the scale the box-to-scale rule
    /// asks for — most often a third-party channel with a short deep link: the shorter the payload, the
    /// sparser the QR, and the HIGHER the scale it needs to fill the same box.
    /// </para>
    /// </summary>
    public IDictionary<string, int> PixelsPerModuleByChannel { get; set; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
}
