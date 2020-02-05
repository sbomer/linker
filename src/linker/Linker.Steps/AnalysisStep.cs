﻿using Mono.Cecil;
using Mono.Linker.Analysis;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace Mono.Linker.Steps
{
	public class AnalysisStep : BaseStep
	{
		readonly LinkContext context;
		readonly CallgraphDependencyRecorder callgraphDependencyRecorder;
		readonly AnalysisReflectionPatternRecorder reflectionPatternRecorder;
		readonly AnalysisEntryPointsStep entryPointsStep;

		public AnalysisStep(LinkContext context, AnalysisEntryPointsStep entryPointsStep)
		{
			this.context = context;
			this.entryPointsStep = entryPointsStep;
			
			callgraphDependencyRecorder = new CallgraphDependencyRecorder ();
			context.Tracer.AddRecorder (callgraphDependencyRecorder);

			reflectionPatternRecorder = new AnalysisReflectionPatternRecorder ();
			context.ReflectionPatternRecorder = reflectionPatternRecorder;
		}

		protected override void Process ()
		{
			// 1. build the callgraph
			//    with "interesting", "public", "unanalyzed"
			//    don't record virtual calls as part of it.
			var apiFilter = new ApiFilter (reflectionPatternRecorder.UnanalyzedMethods, entryPointsStep.EntryPoints);
			var cg = new CallGraph (
				callgraphDependencyRecorder.DirectCalls,
				callgraphDependencyRecorder.VirtualCalls,
				callgraphDependencyRecorder.Overrides,
				callgraphDependencyRecorder.CctorDependencies,
				callgraphDependencyRecorder.CctorFieldAccessDependencies,
				callgraphDependencyRecorder.TypeDependencies,
				callgraphDependencyRecorder.EntryMethods,
				apiFilter);

			// 2. remove linkeranalyzed edges
			// this could leave over interesting methods that had understood calls.
			cg.RemoveCalls(reflectionPatternRecorder.ResolvedReflectionCalls);

			// 3. add ctor edges (with special attribute)
			cg.AddConstructorEdges();

			// 4. reduce to the subgraph that reaches unsafe
			{
			var (icg, mapping) = IntCallGraph.CreateFrom<IMemberDefinition> (cg);
			var intAnalysis = new IntAnalysis(icg);
			List<int> toRemove = new List<int>();
			for (int i = 0; i < icg.numMethods; i++) {
				if (!intAnalysis.ReachesInteresting(i)) {
					// TODO: should really be
					// reaches interesting AND reachable from public
					// but the linker already started from public entry points.
					// so we should be good.
					toRemove.Add(i);
				}
			}
			cg.RemoveMethods(toRemove.Select(i => mapping.intToMethod[i]).ToList());
			}

			// remove virtual calls
			cg.RemoveVirtualCalls();
			
			{
			var (icg, mapping) = IntCallGraph.CreateFrom<IMemberDefinition> (cg);

			// 5. report!
			string jsonFile = Path.Combine (context.OutputDirectory, "trimanalysis.json");
			using (StreamWriter sw = new StreamWriter (jsonFile)) {
				var formatter = new Formatter (cg, mapping, json: true, sw);
				var analyzer = new Analyzer (cg, icg, mapping, apiFilter, reflectionPatternRecorder.ResolvedReflectionCalls, formatter,
					Grouping.Caller);
				analyzer.Analyze ();
			}
			}
		}
	}
}
