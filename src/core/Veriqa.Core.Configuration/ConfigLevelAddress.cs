// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.Configuration;

/// <summary>
/// FORM of the address a key's declaration gives one of its levels (SPEC-012 §10.6). The three forms
/// are a value rather than "a string or null" because a consumer of the catalog — the table of the
/// documentation, a startup check — has to tell "the path follows from the name" from "the level has
/// no path at all", and an empty string says neither.
/// </summary>
public enum ConfigLevelAddressKind
{
    /// <summary>
    /// The path was NOT stated and follows from the name of the key: the dots of the name are the
    /// separators of the path. This is the default form — a level whose configuration is spelled the
    /// way the key is named costs nothing to declare.
    /// </summary>
    DerivedFromName = 0,

    /// <summary>
    /// The path was stated explicitly, because the deployed form of the configuration does not match
    /// the name of the key. Already deployed installations keep reading what they read before, and the
    /// mismatch is visible in the declaration instead of living in a binding somewhere else.
    /// </summary>
    Path = 1,

    /// <summary>
    /// The level has NO path — neither stated nor derived. It is addressed by the identity of the key
    /// and the point of the fallback chain alone, and there is no document to walk into. Two kinds of
    /// level are like that, and the form says the same thing about both: a store keeping values as
    /// rows "level × owner × key × dimensions", and a level whose value the OWNER composes and binds
    /// with a getter of its own — a set assembled out of what a deployment registered, a group of
    /// credentials read off an options class. The registrar of the catalogs binds neither, which is
    /// exactly right: for the first there is nothing to read at, and for the second the owner has
    /// already bound it.
    /// </summary>
    Identity = 2
}

/// <summary>
/// Address of ONE level of a declared key: the level, the form of its address and — for the two forms
/// that have one — the address template itself.
/// <para>
/// The template is relative to the record of its own level, and the declaring assembly therefore
/// names no store (SPEC-012 CFG-231): what fetches the record is a reader
/// (<see cref="IConfigRecordReader{TRecord}"/>). Only the CORE level is addressed absolutely, because
/// there the record IS the application configuration of the host (self-hosted ≡ core,
/// SPEC-012 §10.1).
/// </para>
/// </summary>
public sealed class ConfigLevelAddress
{
    /// <summary>
    /// Creates the address of one level.
    /// </summary>
    /// <param name="level">Level being addressed.</param>
    /// <param name="kind">Form of the address.</param>
    /// <param name="template">Address template; null for <see cref="ConfigLevelAddressKind.Identity"/>.</param>
    internal ConfigLevelAddress(ConfigLevel level, ConfigLevelAddressKind kind, ConfigAddressTemplate? template)
    {
        Level = level;
        Kind = kind;
        Template = template;
    }

    /// <summary>
    /// Level being addressed.
    /// </summary>
    public ConfigLevel Level { get; }

    /// <summary>
    /// Form of the address (see <see cref="ConfigLevelAddressKind"/>).
    /// </summary>
    public ConfigLevelAddressKind Kind { get; }

    /// <summary>
    /// Address as it is written — with the substitutions and the optional groups still in it; null
    /// when the level has no path. It is what a documentation table prints; the concrete path of one
    /// step of the chain is built from the template at resolution time.
    /// </summary>
    public string? Address => Template?.Text;

    /// <summary>
    /// Template the concrete path of a chain step is built from; null when the level has no path.
    /// </summary>
    internal ConfigAddressTemplate? Template { get; }
}
