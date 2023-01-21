// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;

// This is needed due to NativeAOT which doesn't enable nullable globally yet
#nullable enable

namespace ILLink.Shared.DataFlow
{
	// A generic implementation of a forward dataflow analysis. Forward means that it flows facts
	// across code in the order of execution, starting from the beginning of a method,
	// and merging values from predecessors.
	public abstract class ForwardDataFlowAnalysis<TValue, TState, TLattice, TBlock, TRegion, TControlFlowGraph, TTransfer>
		where TValue : struct, IEquatable<TValue>
		where TState : class, IDataFlowState<TValue, TLattice>, new()
		where TLattice : ILattice<TValue>
		where TTransfer : ITransfer<TBlock, TValue, TState, TLattice>
		where TBlock : IEquatable<TBlock>
		where TRegion : IRegion<TRegion>
		where TControlFlowGraph : IControlFlowGraph<TBlock, TRegion>
	{

		// Data structure to store dataflow states for every basic block in the control flow graph,
		// keeping the exception states shared across different basic blocks owned by the same try or catch region.
		private struct ControlFlowGraphState
		{
			// Dataflow states for each basic block
			// TODO: I think the fallthrough and conditional exception states should not
			// be different. The exception state is the same for the entire region I think.
			// All possibilities should be allowed in the exception state...
			// so effectively the feature context will always be unknown.
			// Unless it was known to begin with.
			private readonly Dictionary<TBlock, (TState? FallThrough, TState? Conditional)> blockOutput;

			// The control flow graph doesn't contain edges for exceptional control flow:
			// - From any point in a try region to the start of any catch or finally
			// - From any point in a catch region to the start of a finally or the end of a try-catch block
			// These implicit edges are handled by tracking an auxiliary state for each try and catch region,
			// which the transfer functions are expected to update (in addition to the normal state updates)
			// when visiting operations inside of a try or catch region.

			// Dataflow states for exceptions propagating out of try or catch regions
			private readonly Dictionary<TRegion, Box<TValue>> exceptionState;

			// Control may flow through a finally region when an exception is thrown from anywhere in the corresponding
			// try or catch regions, or as part of non-exceptional control flow out of a try or catch.
			// We track a separate finally state for the exceptional case. Only the normal (non-exceptional) state is
			// propagated out of the finally.

			// Dataflow states for finally blocks when exception propagate through the finally region

			// Each block in a finally region has two tracked states - the normal state,
			// and the state for when the finally region was reached via an exception.
			// TODO: should this have a TValue or TState?
			private readonly Dictionary<TBlock, (TValue? FallThrough, TValue? Conditional)> exceptionFinallyState;

			// Finally regions may be reached (along non-exceptional paths)
			// from multiple branches. This gets updated to track the normal finally input
			// states from all of these branches (which aren't represented explicitly in the CFG).
			private readonly Dictionary<TRegion, TValue> finallyInputState;
			private readonly TControlFlowGraph cfg;
			private readonly TLattice lattice;

			public ControlFlowGraphState (TControlFlowGraph cfg, TLattice lattice, TValue entryValue)
			{
				blockOutput = new ();
				exceptionState = new ();
				exceptionFinallyState = new ();
				finallyInputState = new ();
				this.cfg = cfg;
				this.lattice = lattice;

				blockOutput[cfg.Entry] = (new TState () {
					Lattice = lattice,
					Current = entryValue,
					Exception = null
				}, null);
			}

			// TODO: can the exception state of the conditional/fallthrough successor be different?
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

			public bool TryGetExceptionFinallyState (TBlock block, bool isConditional, out TValue state)
			{
				state = default;
				if (!cfg.TryGetEnclosingFinally (block, out _))
					return false;

				if (!exceptionFinallyState.TryGetValue (block, out (TValue? FallThrough, TValue? Conditional) states)) {
					states = isConditional ? (null, lattice.Top) : (lattice.Top, null);
					exceptionFinallyState.Add (block, states);
				}
				// We might have a conditional or fallthrough state only so far, but be asked for the other one.
				if (isConditional && states.Conditional is null) {
					states = (states.FallThrough, lattice.Top);
					exceptionFinallyState[block] = states;
				}
				if (!isConditional && states.FallThrough is null) {
					states = (lattice.Top, states.Conditional);
					exceptionFinallyState[block] = states;
				}

				state = isConditional ? states.Conditional!.Value : states.FallThrough!.Value;
				return true;
			}

			public void SetExceptionFinallyState (TBlock block, bool isConditional, TValue state)
			{
				if (!cfg.TryGetEnclosingFinally (block, out _))
					throw new InvalidOperationException ();

				// TODO: should we check that the state is already set?
				if (!exceptionFinallyState.TryGetValue (block, out (TValue? FallThrough, TValue? Conditional) states))
					states = (null, null);
				if (isConditional)
					states.Conditional = state;
				else
					states.FallThrough = state;
				exceptionFinallyState[block] = states;
			}

			public TState? Get (TBlock block, bool isConditional)
			{
				if (!blockOutput.TryGetValue (block, out (TState? FallThrough, TState? Conditional) states)) {
					TryGetExceptionState (block, out Box<TValue>? exceptionState); // TODO?
					var state = new TState () {
						Lattice = lattice,
						Current = lattice.Top,
						Exception = exceptionState
					};
					states = isConditional ? (null, state) : (state, null);
					blockOutput.Add (block, states);
				}
				// We might have allocated a fallthrough or conditional state only so far, but be requesting
				// the other one now.
				// The exception state should always be the same for both.
				if (isConditional && states.Conditional is null) {
					states = (states.FallThrough, new TState () {
						Lattice = lattice,
						Current = lattice.Top,
						Exception = states.FallThrough!.Exception
					});
					blockOutput[block] = states;
				}
				if (!isConditional && states.FallThrough is null) {
					states = (new TState () {
						Lattice = lattice,
						Current = lattice.Top,
						Exception = states.Conditional!.Exception
					}, states.Conditional);
					blockOutput[block] = states;
				}

				return isConditional ? states!.Conditional : states!.FallThrough;
			}
		}

		[Conditional ("DEBUG")]
		public virtual void TraceStart (TControlFlowGraph cfg) { }

		[Conditional ("DEBUG")]
		public virtual void TraceVisitBlock (TBlock block) { }

		[Conditional ("DEBUG")]
		public virtual void TraceBlockInput (TValue normalState, TValue? exceptionState, TValue? exceptionFinallyState) { }

		[Conditional ("DEBUG")]
		public virtual void TraceBlockOutput (TValue normalState, TValue? exceptionState, TValue? exceptionFinallyState) { }

		protected readonly TLattice lattice;

		private readonly TValue entryValue;

		protected ForwardDataFlowAnalysis (TLattice lattice, TValue entryValue)
		{
			this.lattice = lattice;
			this.entryValue = entryValue;
		}

		// This just runs a dataflow algorithm until convergence. It doesn't cache any results,
		// allowing each particular kind of analysis to decide what is worth saving.

		// Maybe this could be simplified by pushing values forward, instead of pulling them from predecessors.
		// I think that would let us store only one input value per block, instead of one per successor.
		// But for now, we store one output state per successor, to allow differentiating between the conditional
		// and unconditional branches of a conditional branch.
		public void Fixpoint (TControlFlowGraph cfg, TTransfer transfer)
		{
			TraceStart (cfg);

			// Initialize output of each block to the Top value of the lattice
			var cfgState = new ControlFlowGraphState (cfg, lattice, entryValue);

			// For now, the actual dataflow algorithm is the simplest possible version.
			// It is written to be obviously correct, but has not been optimized for performance
			// at all. As written it will almost always perform unnecessary passes over the entire
			// control flow graph. The core abstractions shouldn't need to change even when we write
			// an optimized version of this algorithm - ideally any optimizations will be generic,
			// not specific to a particular analysis.

			// Allocate some objects which will be reused to hold the current dataflow state,
			// to avoid allocatons in the inner loop below.
			var state = new TState () {
				Lattice = lattice,
				Current = lattice.Top,
				Exception = null
			};
			var finallyState = new TState () {
				Lattice = lattice,
				Current = lattice.Top,
				Exception = null
			};

			bool changed = true;
			int iteration = 0;
			while (changed) {

				// Stop at 1000 iterations.
				if (iteration == 20)
					Console.WriteLine("Hey");
				if (iteration > 30)
					throw new Exception ("Dataflow analysis did not converge after 100 iterations");
				Debug.WriteLine("Iteration " + iteration);
				changed = false;
				foreach (var block in cfg.Blocks) {

					TraceVisitBlock (block);

					if (block.Equals (cfg.Entry))
						continue;

					bool isTryOrCatchOrFilterBlock = cfg.TryGetEnclosingTryOrCatchOrFilter (block, out TRegion? tryOrCatchOrFilterRegion);

					bool isTryBlock = isTryOrCatchOrFilterBlock && tryOrCatchOrFilterRegion!.Kind == RegionKind.Try;
					bool isTryStart = isTryBlock && block.Equals (cfg.FirstBlock (tryOrCatchOrFilterRegion!));
					bool isCatchBlock = isTryOrCatchOrFilterBlock && tryOrCatchOrFilterRegion!.Kind == RegionKind.Catch;
					bool isCatchStartWithoutFilter = isCatchBlock && block.Equals (cfg.FirstBlock (tryOrCatchOrFilterRegion!)) && !cfg.HasFilter (tryOrCatchOrFilterRegion!);
					bool isFilterBlock = isTryOrCatchOrFilterBlock && tryOrCatchOrFilterRegion!.Kind == RegionKind.Filter;
					bool isFilterStart = isFilterBlock && block.Equals (cfg.FirstBlock (tryOrCatchOrFilterRegion!));

					bool isCatchOrFilterStart = isCatchStartWithoutFilter || isFilterStart;

					bool isFinallyBlock = cfg.TryGetEnclosingFinally (block, out TRegion? finallyRegion);
					bool isFinallyStart = isFinallyBlock && block.Equals (cfg.FirstBlock (finallyRegion!));

					//
					// Meet over predecessors to get the new value at the start of this block.
					//

					// Compute the dataflow state at the beginning of this block.
					TValue currentState = lattice.Top;
					foreach (var predecessor in cfg.GetPredecessors (block)) {
						TValue predecessorState = cfgState.Get (predecessor.Block, predecessor.IsConditional).Current;

						// Propagate state through all finally blocks.
						foreach (var exitedFinally in predecessor.FinallyRegions) {
							TValue oldFinallyInputState = cfgState.GetFinallyInputState (exitedFinally);
							TValue finallyInputState = lattice.Meet (oldFinallyInputState, predecessorState);

							cfgState.SetFinallyInputState (exitedFinally, finallyInputState);

							// Note: the current approach here is inefficient for long chains of finally regions because
							// the states will not converge until we have visited each block along the chain
							// and propagated the new states along this path.
							if (!changed && !finallyInputState.Equals (oldFinallyInputState))
								changed = true;

							TBlock lastFinallyBlock = cfg.LastBlock (exitedFinally);
							predecessorState = cfgState.Get (lastFinallyBlock, isConditional: false).Current;
						}

						currentState = lattice.Meet (currentState, predecessorState);
					}
					// State at start of a catch also includes the exceptional state from
					// try -> catch exceptional control flow.
					if (isCatchOrFilterStart) {
						TRegion correspondingTry = cfg.GetCorrespondingTry (tryOrCatchOrFilterRegion!);
						Box<TValue> tryExceptionState = cfgState.GetExceptionState (correspondingTry);
						currentState = lattice.Meet (currentState, tryExceptionState.Value);

						// A catch or filter can also be reached from a previous filter.
						foreach (TRegion previousFilter in cfg.GetPreviousFilters (tryOrCatchOrFilterRegion!)) {
							// Control may flow from the last block of a previous filter region to this catch or filter region.
							// Exceptions may also propagate from anywhere in a filter region to this catch or filter region.
							// This covers both cases since the exceptional state is a superset of the normal state.
							Box<TValue> previousFilterExceptionState = cfgState.GetExceptionState (previousFilter);
							currentState = lattice.Meet (currentState, previousFilterExceptionState.Value);
						}
					}
					if (isFinallyStart) {
						TValue finallyInputState = cfgState.GetFinallyInputState (finallyRegion!);
						currentState = lattice.Meet (currentState, finallyInputState);
					}

					// Compute the independent exceptional finally state at beginning of a finally.
					TValue? exceptionFinallyState = null;
					if (isFinallyBlock) {
						// Inside finally regions, must compute the parallel meet state for unhandled exceptions.
						// Using predecessors in the finally. But not from outside the finally.
						exceptionFinallyState = lattice.Top;
						foreach (var predecessor in cfg.GetPredecessors (block)) {
							var isPredecessorInFinally = cfgState.TryGetExceptionFinallyState (predecessor.Block, predecessor.IsConditional, out TValue predecessorFinallyState);
							Debug.Assert (isPredecessorInFinally);
							exceptionFinallyState = lattice.Meet (exceptionFinallyState.Value, predecessorFinallyState);
						}

						// For first block, also initialize it from the try or catch blocks.
						if (isFinallyStart) {
							// Note that try+catch+finally is represented in the region hierarchy like a
							// try/finally where the try block contains a try/catch.
							// So including the corresponding try exception state will also take care of the
							// corresponding catch exception state.
							TRegion correspondingTry = cfg.GetCorrespondingTry (finallyRegion!);
							Box<TValue> tryExceptionState = cfgState.GetExceptionState (correspondingTry);
							exceptionFinallyState = lattice.Meet (exceptionFinallyState.Value, tryExceptionState.Value);
						}
					}

					// Initialize the exception state at the start of try/catch regions. Control flow edges from predecessors
					// within the same try or catch region don't need to be handled here because the transfer functions update
					// the exception state to reflect every operation in the region.
					cfgState.TryGetExceptionState (block, out Box<TValue>? exceptionState);
					TValue? oldExceptionState = exceptionState?.Value;
					if (isTryStart || isCatchOrFilterStart) {
						// Catch/filter regions get the initial state from the exception state of the corresponding try region.
						// This is already accounted for in the non-exceptional control flow state of the catch block above,
						// so we can just use the state we already computed, for both try and catch regions.
						exceptionState!.Value = lattice.Meet (exceptionState!.Value, currentState);

						if (isFinallyBlock) {
							// Exceptions could also be thrown from inside a finally that was entered due to a previous exception.
							// So the exception state must also include values from the exceptional finally state (computed above).
							exceptionState!.Value = lattice.Meet (exceptionState!.Value, exceptionFinallyState!.Value);
						}
					}

					TraceBlockInput (currentState, exceptionState?.Value, exceptionFinallyState);

					//
					// Apply transfer functions to the met input to get an output value for this block.
					//

					state.Current = currentState;
					state.Exception = exceptionState;
					transfer.Transfer (block, state, out TState? fallThrough, out TState? conditional);

					// Must be careful to only request the conditional/fallthrough state if they are needed.
					if (fallThrough != null) {
						var fallThroughState = cfgState.Get (block, isConditional: false);
						if (!changed && !fallThroughState.Current.Equals (fallThrough.Current))
							changed = true;
						fallThroughState.Current = fallThrough.Current;
						Debug.Assert (fallThroughState.Exception == fallThrough.Exception);
					}

					if (conditional != null) {
						var conditionalState = cfgState.Get (block, isConditional: true);
						if (!changed && !conditionalState.Current.Equals (conditional.Current))
							changed = true;
						conditionalState.Current = conditional.Current;
						Debug.Assert (conditionalState.Exception == conditional.Exception);
					}

					bool isInFinally  = cfgState.TryGetExceptionFinallyState (block, isConditional: true, out TValue oldFinallyStateConditional);
					bool isInFinallyFallThrough = cfgState.TryGetExceptionFinallyState (block, isConditional: false, out TValue oldFinallyStateFallThrough);
					Debug.Assert (isInFinally == isInFinallyFallThrough);
					// Debug.Assert (oldFinallyStateConditional = oldFinallyStateFalThrough);

					if (isInFinally) {
						// Independently apply transfer functions for the exception finally state in finally regions.
						finallyState.Current = exceptionFinallyState!.Value;
						finallyState.Exception = exceptionState;
						transfer.Transfer (block, finallyState, out TState? fallThroughFinally, out TState? conditionalFinally);

						if (fallThroughFinally != null) {
							var gotFinally = cfgState.TryGetExceptionFinallyState (block, isConditional: false, out TValue oldFinallyState);
							Debug.Assert (gotFinally);
							if (!changed && !oldFinallyState.Equals (fallThroughFinally.Current)) {

								if (oldFinallyState is ILLink.RoslynAnalyzer.DataFlow.LocalContextState<ILLink.Shared.DataFlow.ValueSet<ILLink.Shared.DataFlow.SingleValue>, ILLink.RoslynAnalyzer.DataFlow.FeatureContext> oldV &&
									fallThroughFinally.Current is ILLink.RoslynAnalyzer.DataFlow.LocalContextState<ILLink.Shared.DataFlow.ValueSet<ILLink.Shared.DataFlow.SingleValue>, ILLink.RoslynAnalyzer.DataFlow.FeatureContext> newV) {
										Console.WriteLine("Hi");
									}
								changed = true;
							}
							// oldFinallyState.Current = fallThroughFinally.Current;
							cfgState.SetExceptionFinallyState (block, isConditional: false, fallThroughFinally.Current);
							// Debug.Assert (oldFinallyState.Exception == fallThroughFinally.Exception);

							Debug.Assert (cfgState.Get (block, isConditional: false).Exception == fallThroughFinally.Exception);
						}
						if (conditionalFinally != null) {
							var gotFinally = cfgState.TryGetExceptionFinallyState (block, isConditional: true, out TValue oldFinallyState);
							if (!changed && !oldFinallyState.Equals (conditionalFinally.Current))
								changed = true;
							// oldFinallyState.Current = conditionalFinally.Current;
							cfgState.SetExceptionFinallyState (block, isConditional: true, conditionalFinally.Current);
							// Debug.Assert (oldFinallyState.Exception == conditionalFinally.Exception);

							Debug.Assert (cfgState.Get (block, isConditional: true).Exception == conditionalFinally.Exception);
						}
					}

					// Either the normal transfer or the finally transfer might change
					// the try/catch state, so this check should happen after both transfers.
					if (exceptionState?.Value.Equals (oldExceptionState!.Value) == false) {
						Debug.Assert (exceptionState != null);
						Debug.Assert (oldExceptionState != null);
						changed = true;

						// Bubble up the changed exception state to the next enclosing try or catch exception state.
						while (cfg.TryGetEnclosingTryOrCatchOrFilter (tryOrCatchOrFilterRegion!, out TRegion? enclosingTryOrCatch)) {
							// Filters can't contain try/catch/filters.
							Debug.Assert (enclosingTryOrCatch.Kind != RegionKind.Filter);
							Box<TValue> tryOrCatchExceptionState = cfgState.GetExceptionState (enclosingTryOrCatch);
							tryOrCatchExceptionState.Value = lattice.Meet (tryOrCatchExceptionState!.Value, exceptionState!.Value);
							tryOrCatchOrFilterRegion = enclosingTryOrCatch;
						}
					}

					TraceBlockOutput (state.Current, exceptionState?.Value, exceptionFinallyState);
				}
				iteration++;
			}
		}
	}
}
