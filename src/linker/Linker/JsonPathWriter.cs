using System;
using System.IO;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace Mono.Linker
{
	// for now, group by start.
	// list of:
	//   start node,
	//   list of:
	//     stacktrace (which is list of:
	//       method
	//     )

	public class Output {
		public Dictionary<Node, DestinationSet> destinationSets;
	}

	public class DestinationSet {
		public HashSet<VirtualTrace> traces;
	}

	public class VirtualTrace {
		public List<Edge> edges;
		public Node startNode;
	}

	public class JsonPathWriter {
		private readonly SearchableDependencyGraph<Node, Edge> graph;
		private readonly Stream stream;
		private readonly LinkContext context;
		public JsonPathWriter (SearchableDependencyGraph<Node, Edge> graph, Stream stream, LinkContext context) {
			this.graph = graph;
			this.stream = stream;
			this.context = context;
		}

		// TODO: abstract this to not depend on node type.
		// it goes through IsStart interface, but directly accesses value.
		private List<string> FormatTrace (Node start, List<Edge> edges) {
			System.Diagnostics.Debug.Assert (start.IsStart ());
			// now, might not be an entry. could also be untracked.
			var trace = new List<string> ();

			if (edges.Count == 0) {
				// zero-length path case where entry is itself dangerous
				System.Diagnostics.Debug.Assert (start.IsStart () && start.Dangerous);
				System.Diagnostics.Debug.Assert (start.Value is MethodDefinition);
				trace.Add ($"dangerous entry API: {start.Value}");
				return trace;
			}

			// non-zero-length path
			Node end = edges [0].To;
			System.Diagnostics.Debug.Assert (end.Dangerous);
			System.Diagnostics.Debug.Assert (end.Value is MethodDefinition);

			// get max prefix length for pretty formatting, then actually concatenate the strings
			var prefixes = new List<string> ();

			prefixes.Add ($"dangerous API: "); // prefix for end
			foreach (var edge in edges) {
				var from = edge.From;
				System.Diagnostics.Debug.Assert (from.Value is MethodDefinition || from.Value is FieldDefinition || from.Value is TypeDefinition);
				switch (edge.Info) {
				case EdgeInfo.Call:
					prefixes.Add ($"called from: ");
					break;
				case EdgeInfo.VirtualCall:
					prefixes.Add ($"maybe called virtually from: ");
					break;
				case EdgeInfo.UnanalyzedReflectionCall:
					prefixes.Add ($"not understood in call from: ");
					break;
				case EdgeInfo.CctorForType:
					prefixes.Add ($"cctor kept for type: ");
					break;
				case EdgeInfo.FieldAccess:
					prefixes.Add ($"field accessed from: ");
					break;
				case EdgeInfo.CctorForField:
					prefixes.Add ($"cctor triggered by field: ");
					break;
				case EdgeInfo.DeclaringTypeOfMethod:
					prefixes.Add ($"declaring type of: ");
					break;
				default:
					throw new System.NotImplementedException ("tracing " + edge.Info.ToString () + " dependencies is not supported");
				}
			}
			var prefixLength = prefixes.Select(p => p.Length).Max();

			int prefixIndex = 0;
			System.Console.WriteLine ("num prefixes: " + prefixes.Count ());
			System.Console.WriteLine ("edges count" + edges.Count ());

			// build the trace
			trace.Add (String.Format ($"{{0,-{prefixLength}}}{{1}}", prefixes [prefixIndex++], end.Value));
			foreach (var edge in edges) {
				System.Console.WriteLine("prefix index: " + prefixIndex);
				var from = edge.From;
				trace.Add (String.Format ($"{{0,-{prefixLength}}}{{1}}", prefixes [prefixIndex++], from.Value));
			}


			return trace;

			// if we only report user code... there might be entry points
			// that aren't user code. what to do with these?

			// want to record all transitions to/from user code,
			// and report one node from each side. but hide everything else that is exclusively non-user code.

			// build a user-code trace
			prefixIndex = 0;
			var collapsedTrace = new List<string> ();
			// always show innermost node
			collapsedTrace.Add (String.Format ($"{{0,-{prefixLength}}}{{1}}", prefixes [prefixIndex++], end.Value));
			// assume for now: all entry points are in user code.
			// zero-length case. dangerous API might be in user code. handled above, no collapsing is necessary.
			// assume for now: dangerous method is in non-user code.
			System.Diagnostics.Debug.Assert (!IsUserCode (end));
			// start a sequence.

			bool prevCallsNonUserCode = false;

			foreach (var edge in edges) {
				var from = edge.From;
				var to = edge.To;
				if (IsUserCode (from) && IsUserCode (to)) {
					// report user -> user.
					collapsedTrace.Add (String.Format ($"{{0,-{prefixLength}}}{{1}}", prefixes [prefixIndex++], from.Value));
					// report caller. callee was already reported.
					prevCallsNonUserCode = false;
				} else if (IsUserCode (from) && !IsUserCode (to)) {
					// user -> non-user.
					// report caller.
					// may need to record callee if it wasn't already.
					// callee will already have been reported if:
					// - it was end, or
					// - it was the only single node in non-user code (which itself called user code)

					// so we need to report if not end, and callee itself calls non-user code
					// the latter will never be true for ends, so simplifies to:
					// callee itself calls non-user code.
					// can be summarized as:
					// we need to report callee if it itself called non-user code
					if (prevCallsNonUserCode) {
						// report implementation detail
						// TODO: integrate this with prefixes.
						collapsedTrace.Add (String.Format ($"{{0,-{prefixLength}}}{{1}}", "in implementation of: ", to.Value));
					}
					// either way, report caller
					collapsedTrace.Add (String.Format ($"{{0,-{prefixLength}}}{{1}}", prefixes [prefixIndex++], from.Value));

					prevCallsNonUserCode = true; // but this never matters
					// since we only check the bool if prev was itself non-user code.
					// but here we are in user code.
				} else if (!IsUserCode (from) && IsUserCode (to)) {
					// non-user -> user
					// report immediate caller of non-user code
					collapsedTrace.Add (String.Format ($"{{0,-{prefixLength}}}{{1}}", prefixes [prefixIndex++], from.Value));

					prevCallsNonUserCode = false;
				} else if (!IsUserCode (from) && !IsUserCode (to)) {
					// non-user -> non-user
					// don't report.
					prefixIndex++;
					prevCallsNonUserCode = true;
				}
			}

			return collapsedTrace;
		}

		bool IsUserCode (Node node) {
			// TODO: determine whether it's part of:
			// context.Recorder.userAssemblies.
			// find assembly.
			var assembly = ((MemberReference)node.Value).Module.Assembly;
			if (context.Annotations.userAssemblies.Contains (assembly)) {
				return true;
			}
			return false;
		}

		public void Write() {
			var paths = graph.GetAllShortestPaths ();
  			var pathsLists = paths.ToDictionary (
 // 				kvp => new string [] { ((MemberReference)kvp.Key.value).Module.ToString(), kvp.Key.value.ToString () },
 				kvp => "[" + ((MemberReference)kvp.Key.Value).Module.ToString() + "] " + kvp.Key.Value.ToString (),
  				kvp => new HashSet<List<string>> (
  					kvp.Value.Select (
  						edges => FormatTrace (kvp.Key, edges)
  					).ToList()
  				)
  			);

// 			 var pathsLists = new Dictionary<string, HashSet<List<string>>> ();
// 			 foreach (var kvp in paths) {
// 			 	var start = "[" + ((MemberReference)kvp.Key.value).Module.ToString () + "]" + kvp.Key.value.ToString ();
// 			 	var trace = new HashSet<List<string>> (
// 			 		kvp.Value.Select (edges => FormatTrace (kvp.Key, edges)).ToList ()
// 			 	);
// 
// 			 	if (kvp.Key.value is MethodDefinition method) {
// 			 		Console.WriteLine($"[{method.Module}]{method}");
// 			 	}
// 			 	// this should never give back multiple trace SETS for a given entry. why does it?
// 			 	if (!pathsLists.TryGetValue (start, out HashSet<List<string>> pathsList)) {
// 			 		pathsList = trace;
// 			 		pathsLists [start] = trace;
// 			 	} else {
// 			 		// why would we already have added a trace set for given start???
// 			 		throw new Exception ("already got set of traces for node " + start);
// 			 	}
// 			 }
//			var pathsLists = graph.Select(kvp =>  (kvp.Key, kvp.Value.ToList ())).ToList ();
			var options = new JsonSerializerOptions {
				WriteIndented = true,
				Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
			};
			var task = JsonSerializer.SerializeAsync (stream, pathsLists, options);
//			task.Wait (); // wraps the exception
			task.GetAwaiter().GetResult();
		}
	}
}