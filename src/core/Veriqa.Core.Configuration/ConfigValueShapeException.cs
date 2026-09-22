// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// The one failure of a read that says nothing about the VALUE a level stated: the level states its
/// value in one of two shapes — a text or a subtree — and the setting is read from the other one
/// (<see cref="ConfigNode.TryRead{T}"/>). No parser has seen the value yet, so the deployment has not
/// been told that what it wrote is outside what the setting admits.
/// <para>
/// It is a type of its own because the mechanism answers it differently from a failed read in general
/// (<c>PathConfigKeyRegistrar</c>): a shape the setting is not read from is what the binder of an
/// options class passes over in silence, leaving the class default, so the level is left with
/// what the KEY states over a record saying nothing — the shipped default for a level declared to
/// always speak. A text no converter takes is the opposite case: a value the deployment stated and got
/// wrong, refused by CFG-240 with the step left unset — except at the core level of a protective or
/// gated key, or of a set whose core level is the ceiling of the intersection, which the mechanism
/// answers as over a record saying nothing for any failed read (CFG-211/CFG-212).
/// </para>
/// <para>
/// It stays inside the mechanism — the code that READS a value never catches it, it only lets it out.
/// </para>
/// </summary>
/// <param name="message">Message naming the address and the shape stated there.</param>
/// <param name="path">
/// Address the refused shape was found AT, as precise as the source of the record can spell it. It is
/// carried as data and not only inside the message because the one consumer of this type has to NAME it
/// to an operator, and the address it asked the read at need not be the one that failed: a parse hook of
/// a key is free to read several members of its level — the SRI hash beside a script path, a themed map
/// beside the plain field — and the message of an exception is not a place a report can read an address
/// out of. It also reaches the report of a SECRET key, where the exception itself never does: naming an
/// address leaks nothing of the value stated there.
/// </param>
internal sealed class ConfigValueShapeException(string message, string path) : InvalidOperationException(message)
{
    /// <summary>
    /// Address the refused shape was found at.
    /// </summary>
    public string Path { get; } = path;
}
