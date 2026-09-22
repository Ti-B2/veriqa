// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.CompilerServices;

using Veriqa.Core.Configuration;

// The Logging axis moved down into the settings mechanism — the assembly both of its sides already
// depend on — so that the audit trail can read the mode without depending on the OIDC server. The
// enum took the namespace of its new home with it, and type forwarding is keyed on the *full* type
// name: the exported row this attribute emits carries Veriqa.Core.Configuration.LoggingMode, while
// the pre-move name Veriqa.Core.AuthServer.Configuration.Enums.LoggingMode is exported by nobody
// and still fails to load. Binary compatibility across the move is therefore NOT preserved — that
// break is accepted in the pre-release window, together with the move of the two key properties,
// for which no forwarding mechanism exists at all (a member, unlike a type, cannot be forwarded).
// What the row does give is resolution of the enum through this assembly under its new full name,
// at no runtime cost; it is kept for that, not as a bridge to the pre-move contract.
[assembly: TypeForwardedTo(typeof(LoggingMode))]
