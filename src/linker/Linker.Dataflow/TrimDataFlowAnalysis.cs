// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System.Collections.Generic;
using Mono.Linker.Dataflow;
using Mono.Linker.Steps;
using Mono.Cecil;
using Mono.Cecil.Cil;
using ILLink.Shared.DataFlow;
using ILLink.Shared.TrimAnalysis;

namespace Mono.Linker.Dataflow
{
	class TrimDataFlowAnalysis
	{
		// readonly LinkContext _context;
		// readonly MarkStep _markStep;
		// readonly MarkScopeStack _scopeStack;

		// public TrimDataFlowAnalysis (LinkContext context, MarkStep markStep, MarkScopeStack scopeStack)

		readonly ReflectionMethodBodyScanner _scanner;

		public TrimDataFlowAnalysis (ReflectionMethodBodyScanner scanner)
		{
			// _context = context;
			// _markStep = markStep;
			// _scopeStack = scopeStack;
			_scanner = scanner;
		}

		public class DataFlowState {
			public ValueSet<MethodDefinition> NestedFunctions { get; set; }

			public DataFlowState ()
			{
				NestedFunctions = new ValueSet<MethodDefinition> ();
			}

			public DataFlowState Clone() => new DataFlowState { NestedFunctions = this.NestedFunctions.Clone () };
		}


		public void InterproceduralScan (MethodBody methodBody)
		{
			// Initialize the dataflow states for the method body. This effectively is the state of each
			// basic block in the control flow graph.
			// TODO: initialize state for hoisted locals.

			// This state tracks the interprocedural dataflow state for analysis of
			// local functions and lambdas reachable from this method.
			// For now, this just track the set of reachable methods relevant to the interprocedural
			// analysis.
			// Eventually, this will track, for each method included in the interprocedural analysis,
			// the possible states upon entering the method.

			var dataflowState = new DataFlowState ();

			_scanner.Scan (methodBody, dataflowState);

			// TODO: 
			HashSet<MethodDefinition> visitedNestedFunctions = new ();

			// If there were more methods visited, we might need to scan more.
			DataFlowState oldDataflowState;
			do {
				oldDataflowState = dataflowState.Clone ();
				foreach (var method in dataflowState.NestedFunctions) {
					if (!visitedNestedFunctions.Add (method))
						continue;
					_scanner.Scan (method.Body, dataflowState);
				}
			} while (!dataflowState.Equals (oldDataflowState));
		}
	}
}