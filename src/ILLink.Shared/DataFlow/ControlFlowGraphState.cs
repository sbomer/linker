// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
namespace ILLink.Shared.DataFlow
{
	// Data structure to store dataflow states for every basic block in the control flow graph,
	// keeping the exception states shared across different basic blocks owned by the same try or catch region.
	public struct ControlFlowGraphState<TValue, TState, TLattice, TBlock, TRegion, TControlFlowGraph>
		where TValue : struct, IEquatable<TValue>
		where TState : class, IDataFlowState<TValue, TLattice>, new()
		where TLattice : ILattice<TValue>
		where TBlock : IEquatable<TBlock>
		where TRegion : IRegion<TRegion>
		where TControlFlowGraph : IControlFlowGraph<TBlock, TRegion>
	{
		// Dataflow states for each basic block
		readonly Dictionary<TBlock, TState> blockOutput;

		// The control flow graph doesn't contain edges for exceptional control flow:
		// - From any point in a try region to the start of any catch or finally
		// - From any point in a catch region to the start of a finally or the end of a try-catch block
		// These implicit edges are handled by tracking an auxiliary state for each try and catch region,
		// which the transfer functions are expected to update (in addition to the normal state updates)
		// when visiting operations inside of a try or catch region.

		// Dataflow states for exceptions propagating out of try or catch regions
		readonly Dictionary<TRegion, Box<TValue>> exceptionState;

		// Control may flow through a finally region when an exception is thrown from anywhere in the corresponding
		// try or catch regions, or as part of non-exceptional control flow out of a try or catch.
		// We track a separate finally state for the exceptional case. Only the normal (non-exceptional) state is
		// propagated out of the finally.

		// Dataflow states for finally blocks when exception propagate through the finally region
		readonly Dictionary<TBlock, TValue> exceptionFinallyState;

		// Finally regions may be reached (along non-exceptional paths)
		// from multiple branches. This gets updated to track the normal finally input
		// states from all of these branches (which aren't represented explicitly in the CFG).
		readonly Dictionary<TRegion, TValue> finallyInputState;

		readonly TControlFlowGraph cfg;

		public TControlFlowGraph ControlFlowGraph => cfg;

		readonly TLattice lattice;

		public ControlFlowGraphState (TControlFlowGraph cfg, TLattice lattice)
		{
			blockOutput = new ();
			exceptionState = new ();
			exceptionFinallyState = new ();
			finallyInputState = new ();
			this.cfg = cfg;
			this.lattice = lattice;
		}

		public Box<TValue> GetExceptionState (TRegion tryOrCatchOrFilterRegion)
		{
			if (tryOrCatchOrFilterRegion.Kind is not (RegionKind.Try or RegionKind.Catch or RegionKind.Filter))
				throw new ArgumentException (null, nameof (tryOrCatchOrFilterRegion));

			if (!exceptionState.TryGetValue (tryOrCatchOrFilterRegion, out Box<TValue>? state)) {
				state = new Box<TValue> (lattice.Top);
				exceptionState.Add (tryOrCatchOrFilterRegion, state);
			}
			return state;
		}

		public bool TryGetExceptionState (TBlock block, out Box<TValue>? state)
		{
			state = null;
			if (!cfg.TryGetEnclosingTryOrCatchOrFilter (block, out TRegion? tryOrCatchOrFilterRegion))
				return false;

			state = GetExceptionState (tryOrCatchOrFilterRegion);
			return true;
		}

		public TValue GetFinallyInputState (TRegion finallyRegion)
		{
			if (finallyRegion.Kind is not RegionKind.Finally)
				throw new ArgumentException (null, nameof (finallyRegion));

			if (!finallyInputState.TryGetValue (finallyRegion, out TValue state)) {
				state = lattice.Top;
				finallyInputState.Add (finallyRegion, state);
			}
			return state;
		}

		public void SetFinallyInputState (TRegion finallyRegion, TValue state)
		{
			if (finallyRegion.Kind is not RegionKind.Finally)
				throw new ArgumentException (null, nameof (finallyRegion));

			finallyInputState[finallyRegion] = state;
		}

		public bool TryGetExceptionFinallyState (TBlock block, out TValue state)
		{
			state = default;
			if (!cfg.TryGetEnclosingFinally (block, out _))
				return false;

			if (!exceptionFinallyState.TryGetValue (block, out state)) {
				state = lattice.Top;
				exceptionFinallyState.Add (block, state);
			}
			return true;
		}

		public void SetExceptionFinallyState (TBlock block, TValue state)
		{
			if (!cfg.TryGetEnclosingFinally (block, out _))
				throw new InvalidOperationException ();

			exceptionFinallyState[block] = state;
		}

		public TState Get (TBlock block)
		{
			if (!blockOutput.TryGetValue (block, out TState? state)) {
				TryGetExceptionState (block, out Box<TValue>? exceptionState);
				state = new TState () {
					Lattice = lattice,
					Current = lattice.Top,
					Exception = exceptionState
				};
				blockOutput.Add (block, state);
			}
			return state;
		}
	}
}
