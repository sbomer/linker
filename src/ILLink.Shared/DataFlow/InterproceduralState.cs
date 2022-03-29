// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace ILLink.Shared.DataFlow
{

	// Data structure to store dataflow states for every basic block in the control flow graph,
	// keeping the exception states shared across different basic blocks owned by the same try or catch region.
	public class InterproceduralState<TValue, /*TState,*/ TLattice, TMethod> : IEquatable<InterproceduralState<TValue, TLattice, TMethod>>
		where TValue : struct, IEquatable<TValue>
		// where TState : class, IDataFlowState<TValue, TLattice>
		where TLattice : ILattice<TValue>
		where TMethod : IEquatable<TMethod>
	{
		// TODO: defaultvaluedictionary?
		public HashSet<TMethod> MethodsToAnalyze;
		public DefaultValueDictionary<TMethod, TValue> MethodEntry;
		// readonly Dictionary<TMethod, TState> methodExit;

		// This should only be settable from the solver... how to accomplish this?
		readonly TLattice lattice;

		public InterproceduralState (TLattice lattice) {
			this.lattice = lattice;
			// TODO: avoid allocating a hashset each time? and cloning?
			MethodsToAnalyze = new HashSet<TMethod> ();
		}

		public InterproceduralState (InterproceduralState<TValue, TLattice, TMethod> other)
		{
			MethodEntry = new DefaultValueDictionary<TMethod, TValue> (other.MethodEntry);
			MethodsToAnalyze = new HashSet<TMethod> (other.MethodsToAnalyze);
			lattice = other.lattice;
		}

		// public bool changed = false;

		public void ProcessCall (TMethod method, ref TValue state) {
			// TODO: defaultvaluedictionary?

			MethodsToAnalyze.Add (method);
			MethodEntry.Set (method, lattice.Meet (MethodEntry.Get (method), state));

			// if (!MethodEntry.TryGetValue (method, out TValue entryState)) {
			// 	entryState = lattice.Top;
			// }
			// MethodEntry[method] = lattice.Meet (entryState, state);
		}

		public bool Equals (InterproceduralState<TValue, TLattice, TMethod> other) {
			return lattice.Equals (other.lattice) && MethodEntry.Equals (other.MethodEntry)
				&& MethodsToAnalyze.SetEquals (other.MethodsToAnalyze);
		}
	}
}