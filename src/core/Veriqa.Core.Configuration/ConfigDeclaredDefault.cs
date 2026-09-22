// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// The answer a key declares for its CORE level where the record of that level states nothing at the
/// address, as the SCHEMA of declared keys states it (SPEC-012 §10.6, CFG-244).
/// <para>
/// It is a type of its own and not a bare text, because the schema has to tell two different answers
/// apart: "the owner declared no answer" — the whole thing is absent — and "the owner declared one
/// whose value is null", which a key stating that its own default is to be derived elsewhere really
/// does declare (<c>OidcServer.Issuer</c>). A text alone would collapse the two into one, and a
/// reader of the schema would print "not declared" over a key that declares plenty.
/// </para>
/// </summary>
/// <param name="Text">
/// Value of the answer as text, rendered by the one rendering the mechanism prints values with
/// (<see cref="ConfigValueText"/>); null when the declared answer IS null, and for a key whose value
/// is a SECRET — the schema of such a key carries the fact that a default is declared and never its
/// text, because this schema is printed into the client documentation.
/// </param>
public sealed record ConfigDeclaredDefault(string? Text);
