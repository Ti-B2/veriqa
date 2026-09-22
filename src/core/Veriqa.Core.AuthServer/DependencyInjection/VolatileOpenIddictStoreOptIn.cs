// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MPL-2.0

namespace Veriqa.Core.AuthServer.DependencyInjection;

/// <summary>
/// Marker of the spoken choice "the volatile in-memory OpenIddict store is acceptable here"
/// (SPEC-012 CFG-118). Registered by <c>UseInMemoryOpenIddictStore</c> — both the builder method
/// and the <see cref="IServiceCollection"/> one, so both public entry points leave the same trace —
/// and read at startup by <see cref="OpenIddictStoreStartupCheckService"/>.
/// <para>
/// The marker lives in the container rather than on the builder because the second entry point
/// (<c>AddVeriqaOpenIddict</c>, the one the shipped standalone host takes) has no builder at all,
/// and the startup check must answer the same question for both.
/// </para>
/// </summary>
internal sealed class VolatileOpenIddictStoreOptIn;
