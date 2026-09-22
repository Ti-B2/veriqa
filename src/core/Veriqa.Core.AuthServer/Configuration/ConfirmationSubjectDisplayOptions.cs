// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Veriqa.Core.AuthServer.Configuration.Enums;

namespace Veriqa.Core.AuthServer.Configuration;

/// <summary>
/// The surfaces a deployment lets the subject of a confirmation be SHOWN on, besides the page that
/// asks the question (SPEC-012 §4.11). Configuration section: <c>Veriqa:ConfirmationSubjectDisplay</c>.
/// </summary>
/// <remarks>
/// The default is the EMPTY set (CFG-105): what is being confirmed is shown where it is being asked
/// about, and nowhere else, until a deployment says otherwise. The axis is not part of the
/// <c>ui_config</c> record (CFG-107) — it decides where predicate data of a caller is shown, which
/// is not a presentational choice a page record may make.
/// </remarks>
public sealed class ConfirmationSubjectDisplayOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Veriqa:ConfirmationSubjectDisplay";

    /// <summary>
    /// Member the set is stated at inside the RECORD of a level above the core one — the entry of an
    /// OIDC client for the application level, the record of a tenant for the tenant one. It is a member
    /// of the record and not a property of this class: a level above the core one is addressed inside
    /// its own record by path, whether or not the entry happens to carry a property of that name.
    /// </summary>
    public const string RecordMember = "ConfirmationSubjectDisplaySurfaces";

    /// <summary>
    /// Surfaces the subject is shown on. An empty set — the default (CFG-105). Duplicates are not an
    /// error: this is a set, and a value stated twice is stated once.
    /// </summary>
    public IReadOnlyList<ConfirmationSubjectSurface> Surfaces { get; set; } = [];
}
