// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace ILLink.Shared.DataFlow
{
	public partial struct LocalState<TValue> : IEquatable<LocalState<TValue>>
		where TValue : IEquatable<TValue>
	{
		public bool Equals (LocalState<TValue> other) => Dictionary.Equals (other.Dictionary);

		public TValue Get (LocalKey key) => Dictionary.Get (key);

		public void Set (LocalKey key, TValue value) => Dictionary.Set (key, value);

		public override string ToString () => Dictionary.ToString ();
	}

	// Wrapper struct exists purely to substitute a concrete LocalKey for TKey of DictionaryLattice
	public readonly partial struct LocalStateLattice<TValue, TValueLattice> : ILattice<LocalState<TValue>>
		where TValue : struct, IEquatable<TValue>
		where TValueLattice : ILattice<TValue>
	{
		public partial LocalState<TValue> Meet (LocalState<TValue> left, LocalState<TValue> right);
	}
}