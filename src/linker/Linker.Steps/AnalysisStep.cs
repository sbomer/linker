using Mono.Cecil;
using System.IO;

namespace Mono.Linker.Steps
{
	public class AnalysisStep : BaseStep
	{
		protected override void Process ()
		{
			string jsonFile = Path.Combine (Context.OutputDirectory, "trimanalysis.json");
			using FileStream fs = File.Create (jsonFile);
			var writer = new JsonPathWriter(Context.Annotations.Recorder, Context.Annotations.Graph, Context.Annotations.UnsafeReachingData, fs, Context);
			writer.Write ();
		}
	}
}
