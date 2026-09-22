// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Text.Encodings.Web;
using System.Text.Json;
using Veriqa.Core.ChannelAdapter.UI;

namespace Veriqa.Core.AuthServer.UI;

/// <summary>
/// Access to the client script of the sign-in window shipped as an embedded resource of this
/// assembly, and to the JSON form of the configuration that script reads.
/// <para>
/// The script is INLINED into the generated document, exactly as the stylesheets are: the page must
/// stay self-contained (SPEC-015 §1) and is never made to fetch a second file. What the resource
/// changes is where the source lives — a real .js file instead of a string literal inside the
/// renderer — so the script is reachable by a linter, an editor and a diff.
/// </para>
/// <para>
/// Norms the script implements. They are named here and not in the .js file, whose comments are
/// inlined into the page and reach the end user's browser (see <see cref="CorePageEmbeddedText"/>):
/// </para>
/// <list type="bullet">
/// <item><description>the transaction status arrives over SignalR (SPEC-007 §5) and is applied to the
/// page by one handler for every delivery path (SPEC-007 §5.3); after an automatic reconnect the
/// connection joins the transaction group again (SPEC-007 UI-033);</description></item>
/// <item><description>when the client library does not load, the connection cannot start or closes for
/// good, the page degrades to polling the status endpoint (SPEC-007 §5.4);</description></item>
/// <item><description>no transport leaves the user waiting out the TTL: the page re-reads the status
/// when it becomes visible again and when it is restored from the back/forward cache, and the
/// navigation latch is reset on such a restore (SPEC-007 UI-087);</description></item>
/// <item><description>the countdown runs from the server's remainder of the TTL, and zero on the clock
/// is verified with the server before the page ends as expired (SPEC-007 UI-094);</description></item>
/// <item><description>the email intermediate statuses are an additive channel that does not touch the
/// lifecycle handling (SPEC-016 §10.1);</description></item>
/// <item><description>a clipped third-party label gets the tooltip of the contour
/// (SPEC-015 §4.16).</description></item>
/// </list>
/// </summary>
internal static class AuthPageScript
{
    /// <summary>
    /// Resource name of the window's script (<c>UI/Scripts/veriqa-auth-page.js</c>).
    /// </summary>
    private const string ResourceName = "Veriqa.Core.AuthServer.UI.Scripts.veriqa-auth-page.js";

    /// <summary>
    /// Identifier of the data block carrying <see cref="AuthPageScriptConfig"/> into the page — the
    /// one address the script and the renderer share.
    /// <para>
    /// The name binds two pieces of code that nothing else connects: this constant emits the block,
    /// and a bare literal reads it back (<c>getElementById("veriqa-auth-config")</c> in
    /// <c>UI/Scripts/veriqa-auth-page.js</c>) — a .js file kept free of server substitutions has no
    /// way to be told the name. Nothing checks that pair: the compiler does not see the literal and
    /// the component tests read the markup only, so renaming one side leaves the build and the
    /// tests green while the script dies in the browser on its very first statement, taking the
    /// countdown, SignalR, polling and expiry handling with it. The emitter and the reader are
    /// renamed together; everywhere else the name appears — this comment, the header of the .js
    /// file, the design documents — it is prose about the seam, binds nothing, and follows by a
    /// grep for the old value.
    /// </para>
    /// </summary>
    public const string ConfigElementId = "veriqa-auth-config";

    /// <summary>
    /// How the configuration is written into the data block.
    /// <para>
    /// The encoder is stated rather than left to the default: it is what makes the block safe to put
    /// into the document. The HTML-safe encoder escapes <c>&lt;</c>, <c>&gt;</c> and <c>&amp;</c>, so a
    /// value can neither close the block early with <c>&lt;/script&gt;</c> nor open an HTML comment,
    /// and it escapes everything outside basic Latin, which covers the line terminators U+2028 and
    /// U+2029 as well. The contents of a data block are raw text, so this encoding IS the protection —
    /// HTML-encoding the block would not protect it, it would corrupt the JSON.
    /// </para>
    /// </summary>
    private static readonly JsonSerializerOptions ConfigSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.Default
    };

    /// <summary>
    /// Source of the window's script, ready to be inlined.
    /// </summary>
    public static string Js => CorePageEmbeddedText.Load(typeof(AuthPageScript).Assembly, ResourceName);

    /// <summary>
    /// Writes the page configuration as the JSON the data block carries.
    /// </summary>
    /// <param name="config">Configuration of the page being rendered.</param>
    /// <returns>JSON safe to place inside the data block as it is.</returns>
    public static string SerializeConfig(AuthPageScriptConfig config)
    {
        return JsonSerializer.Serialize(config, ConfigSerializerOptions);
    }
}
