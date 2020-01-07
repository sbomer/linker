using Mono.Cecil;
using Mono.Linker.Analysis;
using System.IO;

namespace Mono.Linker.Steps
{
	public class AnalysisStep : BaseStep
	{
		private LinkContext context;
		private CallgraphDependencyRecorder callgraphDependencyRecorder;
		private AnalysisPatternRecorder patternRecorder;
		private AnalysisEntryPointsStep entryPointsStep;

		public AnalysisStep(LinkContext context, AnalysisEntryPointsStep entryPointsStep)
		{
			this.context = context;
			this.entryPointsStep = entryPointsStep;
			
			callgraphDependencyRecorder = new CallgraphDependencyRecorder ();
			context.Tracer.AddRecorder (callgraphDependencyRecorder);

			patternRecorder = new AnalysisPatternRecorder ();
			context.PatternRecorder = patternRecorder;
		}

		protected override void Process ()
		{
			// 1. build the callgraph
			//    with "interesting", "public", "unanalyzed"
			//    don't record virtual calls as part of it.
			var apiFilter = new ApiFilter (patternRecorder.UnanalyzedMethods, entryPointsStep.EntryPoints);
			var cg = new CallGraph (callgraphDependencyRecorder.Dependencies, apiFilter);
			cg.RemoveVirtualCalls();

			// 2. remove linkeranalyzed edges
			cg.RemoveCalls(patternRecorder.ResolvedReflectionCalls);

			// 3. add ctor edges (with special attribute)
			cg.AddConstructorEdges();

			// 4. reduce to the subgraph that reaches unsafe
			{
			var (icg, mapping) = IntCallGraph.CreateFrom (cg);
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

			{
			var (icg, mapping) = IntCallGraph.CreateFrom (cg);

			// 5. report!
			string jsonFile = Path.Combine (context.OutputDirectory, "trimanalysis.json");
			using (StreamWriter sw = new StreamWriter (jsonFile)) {
				var formatter = new Formatter (cg, mapping, json: true, sw);
				var analyzer = new Analyzer (cg, icg, mapping, apiFilter, patternRecorder.ResolvedReflectionCalls, formatter, Grouping.Callee);
				analyzer.Analyze ();
			}
		}
	}
}
