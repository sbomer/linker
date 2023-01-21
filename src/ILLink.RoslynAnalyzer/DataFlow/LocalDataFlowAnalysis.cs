// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using ILLink.Shared.DataFlow;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace ILLink.RoslynAnalyzer.DataFlow
{
	// This class is responsible for the interprocedural analysis of local variables.
	// It substitutes type arguments into the generic forward dataflow analysis,
	// creating a simpler abstraction that can track the values of local variables using Roslyn APIs.
	// The kinds of values tracked are still left as unspecified generic parameters TValue and TLattice.
	public abstract class LocalDataFlowAnalysis<TValue, TContext, TLattice, TContextLattice, TTransfer>
		: ForwardDataFlowAnalysis<
			LocalContextState<TValue, TContext>,
			LocalDataFlowState<TValue, TContext, TLattice, TContextLattice>,
			LocalContextLattice<TValue, TContext, TLattice, TContextLattice>,
			BlockProxy,
			RegionProxy,
			ControlFlowGraphProxy,
			TTransfer
		>
		where TValue : struct, IEquatable<TValue>
		where TContext : struct, IEquatable<TContext>
		where TLattice : ILattice<TValue>, new()
		where TContextLattice : ILattice<TContext>, new()
		where TTransfer : LocalDataFlowVisitor<TValue, TContext, TLattice, TContextLattice>
	{
		protected readonly OperationBlockAnalysisContext Context;

		readonly IOperation OperationBlock;

		private static LocalContextLattice<TValue, TContext, TLattice, TContextLattice> GetLatticeAndEntryValue(out LocalContextState<TValue, TContext> entryValue)
		{
			LocalContextLattice<TValue, TContext, TLattice, TContextLattice> lattice = new (new (new TLattice ()), new TContextLattice ());
			// TODO: use new() constraint everywhere that a lattice type parameter is required
			// to enforce that the substituted type can be created out of thin air.
			// TODO: add a static member to each lattice type to ensure that it is only ever
			// constructed once.
			entryValue = lattice.Top;
			return lattice;
		}

		protected LocalDataFlowAnalysis (OperationBlockAnalysisContext context, IOperation operationBlock)
			: base (GetLatticeAndEntryValue (out var entryValue), entryValue)
		{
			Context = context;
			OperationBlock = operationBlock;
		}

		public void InterproceduralAnalyze ()
		{
			var methodGroupLattice = new ValueSetLattice<MethodBodyValue> ();
			var hoistedLocalLattice = new DictionaryLattice<LocalKey, Maybe<TValue>, MaybeLattice<TValue, TLattice>> ();
			var interproceduralStateLattice = new InterproceduralStateLattice<TValue, TLattice> (
				methodGroupLattice, hoistedLocalLattice);
			var interproceduralState = interproceduralStateLattice.Top;

			var oldInterproceduralState = interproceduralState.Clone ();

			if (Context.OwningSymbol is not IMethodSymbol owningMethod)
				return;

			Debug.Assert (owningMethod.MethodKind is not (MethodKind.LambdaMethod or MethodKind.LocalFunction));
			var startMethod = new MethodBodyValue (owningMethod, Context.GetControlFlowGraph (OperationBlock));
			interproceduralState.TrackMethod (startMethod);

			while (!interproceduralState.Equals (oldInterproceduralState)) {
				oldInterproceduralState = interproceduralState.Clone ();

				foreach (var method in oldInterproceduralState.Methods) {
					if (method.Method.IsInRequiresUnreferencedCodeAttributeScope (out _))
						continue;

					AnalyzeMethod (method, ref interproceduralState);
				}
			}
		}

		void AnalyzeMethod (MethodBodyValue method, ref InterproceduralState<TValue, TLattice> interproceduralState)
		{
			var cfg = method.ControlFlowGraph;
			var lValueFlowCaptures = LValueFlowCapturesProvider.CreateLValueFlowCaptures (cfg);
			var visitor = GetVisitor (method.Method, cfg, lValueFlowCaptures, interproceduralState);
			Fixpoint (new ControlFlowGraphProxy (cfg), visitor);

			// The interprocedural state struct is stored as a field of the visitor and modified
			// in-place there, but we also need those modifications to be reflected here.
			interproceduralState = visitor.InterproceduralState;
		}

		protected abstract TTransfer GetVisitor (
			IMethodSymbol method,
			ControlFlowGraph methodCFG,
			ImmutableDictionary<CaptureId, FlowCaptureKind> lValueFlowCaptures,
			InterproceduralState<TValue, TLattice> interproceduralState);
	}
}