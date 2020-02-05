using System.Collections.Generic;
using System.Diagnostics;

namespace Mono.Linker.Analysis
{
	public class IntMapping<T>
	{
		public Dictionary<int, T> intToMethod;
		public Dictionary<T, int> methodToInt;
	}

	// simple holder for integer callgraph representation
	// does not implement ICallGraph, because it is meant for low-level
	// and efficient manipulation via direct access to the underlying representation.
	public class IntCallGraph
	{

		// this is simply a data type to hold the following:
		public int numMethods;
		public bool [] isInteresting;
		public bool [] isEntry;
		public int [] [] callees;
		public int [] [] callers;

		public static (IntCallGraph, IntMapping<T>) CreateFrom<T> (ICallGraph<T> callGraph)
		{
			int numMethods = callGraph.Nodes.Count;
			bool [] isInteresting = new bool [numMethods];
			bool [] isEntry = new bool [numMethods];
			// bidirectional map int <-> method
			// necessary to build neighbors list,
			// and for callers to translate results back into the
			// original representation.
			var intToMethod = new Dictionary<int, T> ();
			var methodToInt = new Dictionary<T, int> ();

			int i = 0;
			foreach (var m in callGraph.Nodes) {
				intToMethod [i] = m;
				methodToInt [m] = i;
				if (callGraph.IsInteresting (m)) {
					isInteresting [i] = true;
				}
				if (callGraph.IsEntry (m)) {
					isEntry [i] = true;
				}
				// TODO isVirtual? or should that be a property of the edges instead?
				// TODO isSafe? do we need this?
				i++;
			}

			// determine how many callers/callees each method has, so we can build a jagged array
			var calleesMap = new Dictionary<int, List<int>> ();
			var callersMap = new Dictionary<int, List<int>> ();
			int j;
			foreach (var e in callGraph.Edges) {
				var (caller, callee) = e;
				i = methodToInt [caller];
				j = methodToInt [callee];

				// callees edge from i -> j
				if (!calleesMap.TryGetValue (i, out List<int> js)) {
					js = new List<int> ();
					calleesMap [i] = js;
				}
				js.Add (j);


				// callers edge from j -> i
				if (!callersMap.TryGetValue (j, out List<int> @is)) {
					@is = new List<int> ();
					callersMap [j] = @is;
				}
				@is.Add (i);
			}

			// build the jagged array
			int [] [] callees = new int [numMethods] [];
			int [] [] callers = new int [numMethods] [];
			foreach (var entry in calleesMap) {
				i = entry.Key;
				var js = entry.Value;
				Debug.Assert (js != null);
				callees [i] = new int [js.Count];
				for (j = 0; j < js.Count; j++) {
					callees [i] [j] = js [j];
				}
			}
			foreach (var entry in callersMap) {
				j = entry.Key;
				var @is = entry.Value;
				Debug.Assert (@is != null);
				callers [j] = new int [@is.Count];
				for (i = 0; i < @is.Count; i++) {
					callers [j] [i] = @is [i];
				}
			}

			var intCallGraph = new IntCallGraph {
				numMethods = numMethods,
				isInteresting = isInteresting,
				isEntry = isEntry,
				callees = callees,
				callers = callers
			};
			var intMapping = new IntMapping<T> {
				intToMethod = intToMethod,
				methodToInt = methodToInt
			};
			return (intCallGraph, intMapping);
		}
	}
}
