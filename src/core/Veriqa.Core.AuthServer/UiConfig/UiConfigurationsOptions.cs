// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.AuthServer.UiConfig;

/// <summary>
/// Configuration catalog of ui_config records for self-hosted (§8 assumption #2).
/// The Veriqa:UiConfigurations section is a "record code → record body" dictionary. Optional:
/// when the section is absent, the catalog is empty. Codes from the client's AllowedUiConfigs remain valid
/// even without a record in the catalog — the store synthesizes an empty record and the renderer uses the
/// global look (1:1, R2). A record in the catalog overrides the look for the corresponding code.
/// </summary>
public sealed class UiConfigurationsOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Veriqa:UiConfigurations";

    /// <summary>
    /// ui_config records by code (key — Code, value — record body).
    /// The IReadOnlyDictionary type follows the repository convention (csharp-rules §2): the .NET 8+
    /// configuration binder can bind a read-only dictionary by creating a Dictionary and assigning it to the property.
    /// </summary>
    public IReadOnlyDictionary<string, UiConfigRecord> Records { get; set; } =
        new Dictionary<string, UiConfigRecord>(StringComparer.Ordinal);
}
