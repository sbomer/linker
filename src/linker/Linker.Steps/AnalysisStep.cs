using Mono.Cecil;
using System.IO;

namespace Mono.Linker.Steps
{
	public class AnalysisStep : BaseStep
	{
		protected override void Process ()
		{
			string jsonFile = Path.Combine (Context.OutputDirectory, "trimanalysis.json");
			var graphRecorder = Context.GraphRecorder;
			var graph = graphRecorder.graph;
			var unsafeData = graphRecorder.unsafeReachingData;
			using (FileStream fs = File.Create (jsonFile)) {
				var writer = new JsonPathWriter (graphRecorder, fs, Context);
				writer.Write ();
			}
		}
	}
}
