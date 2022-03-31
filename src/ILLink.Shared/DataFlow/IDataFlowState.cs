// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace ILLink.Shared.DataFlow
{
	public sealed class Box<T> where T : struct
	{
		public Box (T value) => Value = value;
		public T Value { get; set; }

	}

	public interface IDataFlowState<TValue, TValueLattice>
		where TValue : struct, IEquatable<TValue>
		where TValueLattice : ILattice<TValue>
	{
		TValue Current { get; set; }
		Box<TValue>? Exception { get; set; }
		TValueLattice Lattice { get; init; }
	}

	public abstract class DataFlowState<TValue, TValueLattice>
		where TValue : struct, IEquatable<TValue>
		where TValueLattice : struct, ILattice<TValue>
	{
		public TValue Current { get; set; }
		public TValue Exception { get; set; }
		TValueLattice Lattice { get; init; }

		// Goal: modifying current in any way should track changes in Exception.
		// Can modify by setting, or calling mutating method
		// Want to allow mutating method, because
		//   Setting a single local should be allowed.
		// Would require a flow like:
		// - Get an action builder that will work on a ref to it
		// - Execute it
		// - After executing, merge into exception state
		// BUT there's no interface for TValue. It could be anything.
		// Something like:
		// MutateInPlace ((TValue v) => {v.Mutate()});
		// But want to avoid copying...
		// MutateInPlace ((ref TValue v) => {v.Mutate()})
		// Run the action, then merge in.
	}
}