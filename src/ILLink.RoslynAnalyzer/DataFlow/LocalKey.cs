// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace ILLink.Shared.DataFlow
{
	public readonly partial struct LocalKey : IEquatable<LocalKey>
	{
		readonly ILocalSymbol? Local;

		readonly CaptureId? CaptureId;

		public LocalKey (ILocalSymbol symbol) => (Local, CaptureId) = (symbol, null);

		public LocalKey (CaptureId captureId) => (Local, CaptureId) = (null, captureId);

		public partial bool Equals (LocalKey other) => SymbolEqualityComparer.Default.Equals (Local, other.Local) &&
			(CaptureId?.Equals (other.CaptureId) ?? other.CaptureId == null);

		public override string ToString ()
		{
			if (Local != null)
				return Local.ToString ();
			return $"capture {CaptureId.GetHashCode ()}";
		}

		public override partial int GetHashCode ()
			=> HashUtils.Combine (SymbolEqualityComparer.Default.GetHashCode(Local), CaptureId?.GetHashCode () ?? 0);
	}
}
