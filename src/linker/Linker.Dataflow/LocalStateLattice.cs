// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace ILLink.Shared.DataFlow
{
    public partial struct LocalState<TValue> : IEquatable<LocalState<TValue>>
		where TValue : IEquatable<TValue>
	{
		public DefaultValueDictionary<LocalKey, TValue> Dictionary;

		public LocalState (DefaultValueDictionary<LocalKey, TValue> dictionary) => Dictionary = dictionary;
    }

	public readonly partial struct LocalStateLattice<TValue, TValueLattice> : ILattice<LocalState<TValue>>
		where TValue : struct, IEquatable<TValue>
		where TValueLattice : ILattice<TValue>
	{
		public readonly DictionaryLattice<LocalKey, TValue, TValueLattice> Lattice;

		public LocalState<TValue> Top { get; }

		public partial LocalState<TValue> Meet (LocalState<TValue> left, LocalState<TValue> right)
		{
			return new LocalState<TValue> (Lattice.Meet (left.Dictionary, right.Dictionary));
		}
	}
}