// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using ILLink.Shared.DataFlow;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace ILLink.RoslynAnalyzer.DataFlow
{
	public readonly struct LocalKey : IEquatable<LocalKey>
	{
		readonly ILocalSymbol? Local;

		readonly CaptureId? CaptureId;

		public LocalKey (ILocalSymbol symbol) => (Local, CaptureId) = (symbol, null);

		public LocalKey (CaptureId captureId) => (Local, CaptureId) = (null, captureId);

		public bool Equals (LocalKey other) => SymbolEqualityComparer.Default.Equals (Local, other.Local) &&
			(CaptureId?.Equals (other.CaptureId) ?? other.CaptureId == null);

		public override string ToString ()
		{
			if (Local != null)
				return Local.ToString ();
			return $"capture {CaptureId.GetHashCode ()}";
		}
	}

	// Wrapper struct exists purely to substitute a concrete LocalKey for TKey of DefaultValueDictionary
	public struct LocalState<TValue> : ILatticeValue<LocalState<TValue>>
		where TValue : struct, ILatticeValue<TValue>
	{
		public DefaultValueDictionary<LocalKey, TValue> Dictionary;

		public LocalState (DefaultValueDictionary<LocalKey, TValue> dictionary) => Dictionary = dictionary;

		public bool Equals (LocalState<TValue> other) => Dictionary.Equals (other.Dictionary);

		public TValue Get (LocalKey key) => Dictionary.Get (key);

		public void Set (LocalKey key, TValue value) => Dictionary.Set (key, value);

		public override string ToString () => Dictionary.ToString ();

		public LocalState<TValue> Meet (LocalState<TValue> other) => new (this.Dictionary.Meet (other.Dictionary));
	}
}