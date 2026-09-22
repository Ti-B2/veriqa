// Copyright (c) Dmitrii Erusov
// SPDX-License-Identifier: MIT

namespace Veriqa.Core.Contracts;

/// <summary>
/// An avatar image decoded and validated by <see cref="AvatarDataUri.TryParse"/>.
/// Record equality compares <see cref="Content"/> as a memory region, not byte by byte.
/// </summary>
/// <param name="ContentType">Accepted image media type confirmed by the byte signature, in lower case.</param>
/// <param name="Content">Decoded image bytes, 1 to <see cref="AvatarDataUri.MaxImageBytes"/> long.</param>
public sealed record AvatarImage(string ContentType, ReadOnlyMemory<byte> Content);
