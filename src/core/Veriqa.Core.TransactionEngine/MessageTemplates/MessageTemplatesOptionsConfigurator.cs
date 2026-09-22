// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Veriqa.Core.TransactionEngine.MessageTemplates;

/// <summary>
/// Reads the <c>Veriqa:MessageTemplates</c> section into <see cref="MessageTemplatesOptions"/>.
/// <para>
/// The section holds its entries at its ROOT — one member per message kind — while the options type
/// keeps them in a member of its own, so that the root of the section can grow a scalar setting later
/// (SPEC-036 §4.6). <c>services.Configure(section)</c> cannot express that: it binds the section onto
/// the type, and the two shapes differ by exactly one level. So the reading is written out here — the
/// entries are bound one by one, each at its own address — and the type is free of the inheritance
/// from a dictionary that would have fixed the shape forever.
/// </para>
/// </summary>
/// <param name="configuration">Application configuration of the host.</param>
internal sealed class MessageTemplatesOptionsConfigurator(IConfiguration configuration)
    : IConfigureOptions<MessageTemplatesOptions>
{
    /// <inheritdoc />
    public void Configure(MessageTemplatesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        foreach (var entry in configuration.GetSection(MessageTemplatesOptions.SectionName).GetChildren())
        {
            if (entry.Get<MessageTemplateKindOptions>() is { } declaration)
            {
                options.Kinds[entry.Key] = declaration;
            }
        }
    }
}
