using Mono.Cecil;
using Mono.Linker.Analysis;
using System.IO;
using System.Collections.Generic;
using System.Linq;

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
			System.Console.Error.WriteLine("line to error");
			System.Console.WriteLine("line to out");

			// looks like overlapping problem ranges aren't reported.
			// looks like importance doesn't matter as long as formatting is correct
			// and, warnings work.
			// context.Logger.LogMessage (MessageImportance.High, "/home/sven/callgraph/analyzertest/Program.cs(23,31): error CS1022: Type or namespace definition, or end-of-file expected [/home/sven/callgraph/analyzertest/analyzertest.csproj]");
			// context.Logger.LogMessage (MessageImportance.Low, "/home/sven/callgraph/analyzertest/Program.cs(24,31): error CS1022: lowType or namespace definition, or end-of-file expected [/home/sven/callgraph/analyzertest/analyzertest.csproj]");
			context.Logger.LogMessage (MessageImportance.Low, "/home/sven/callgraph/analyzertest/Program.cs(25,31): warning CS1022: warnType or namespace definition, or end-of-file expected [/home/sven/callgraph/analyzertest/analyzertest.csproj]");
			// problem matcher doesn't like IL..., maybe it looks for CS or VB.
			context.Logger.LogMessage (MessageImportance.Low, "/home/sven/callgraph/analyzertest/Program.cs(26,31): warning CS0308: Type or namespace definition, or end-of-file expected [/home/sven/callgraph/analyzertest/analyzertest.csproj]");

			context.Logger.LogMessage (MessageImportance.Low, $"this is a test message to msbuild!");

			context.Logger.LogMessage (MessageImportance.Low, $"warning: TEST to msbuild!");
			var path = "/home/sven/callgraph/analyzertest/Program.cs"; // $msCompile problem matcher looks for full paths
			context.Logger.LogMessage (MessageImportance.Low, path + "(14,15): warning ILA008: TEST to msbuild!");
			// throw new System.Exception("SVEN Was her.");
			// log a warning, just to test this.
			// TODO: how to log an info message?
			context.Logger.LogMessage (MessageImportance.High, path + ": information IL0000: using ILLinker!");
			context.Logger.LogMessage (MessageImportance.High, path + ": informational IL0001: using ILLinker!");
			context.Logger.LogMessage (MessageImportance.High, path + ": info IL0002: using ILLinker!");
			context.Logger.LogMessage (MessageImportance.High, path + ": info : using ILLinker!");
			context.Logger.LogMessage (MessageImportance.High, "info : using ILLinker!");


			// needs to operate on the methods/edges sets.
			// var dependencyGraph = AttributedGraph.FromCallgraph(callgraph)
			// 	.RemoveVirtualEdges()
			// 	.RemoveLinkerAnalyzedEdges()
			// 	.AddCtorEdges()
			// 	.ReduceToReachingUnsafe()

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

		static void Report (CallGraph cg)
		{
			// report the whole graph! it has already been reduced to the stuff we care about.
			var methods = cg.Methods;
			var calls = cg.Calls;
			var callees = cg.Calls.Select((a, b) => (b, a)).ToHashSet();
			var interestingMethods = cg.Methods.Where(m => cg.IsInteresting(m)).ToHashSet();

			// show all paths from the graph.
			// in general, a graph might have cycles, and we don't know where to start.
			// since we know our analysis had to do with interesting methods, start with these.
			// don't report interesting -> interesting.
			// some interesting ones will be completely hidden by other interesting ones.

			// similar for publics:
			// what if there are cycles among public APIs?
		}
	}
}
