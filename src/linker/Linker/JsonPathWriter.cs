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
		public Dictionary<Node<NodeInfo>, DestinationSet> destinationSets;
	}

	public class DestinationSet {
		public HashSet<VirtualTrace> traces;
	}

	public class VirtualTrace {
		public List<Edge<NodeInfo, DependencyInfo>> edges;
		public Node<NodeInfo> startNode;
	}

	public class JsonPathWriter {
		private readonly SearchableDependencyGraph<NodeInfo, DependencyKind> graph;
		// graph is used for getting thingns.
		private readonly HashSet<UnsafeReachingData> unsafeReachingData;
		private readonly GraphDependencyRecorder recorder;
		// needs a dependency graph, 
		private readonly Stream stream;
		private readonly LinkContext context;
		public JsonPathWriter (GraphDependencyRecorder dependencyRecorder, SearchableDependencyGraph<NodeInfo, DependencyKind> graph, HashSet<UnsafeReachingData> unsafeReachingData, Stream stream, LinkContext context) {
			this.unsafeReachingData = unsafeReachingData;
			this.graph = graph;
			this.stream = stream;
			this.context = context;
			this.recorder = dependencyRecorder;
			CheckThatEveryNodeHasAReason();
		}

		private void CheckThatEveryNodeHasAReason()
		{
			foreach (var node in graph.nodes) {
				// graph may have null node value, for "NoReason" dependencies. is that ok??? NO!
				// each node either has an edge to it, or an entryinfo recording why it was placed in the graph as an entry point
				// some types are marked "linker internal".
				// for these... we track separate "internal" edges (not entryedges, but similar).
				System.Diagnostics.Debug.Assert (
					graph.edgesTo.ContainsKey (node) ||
					recorder.entryInfo.Any(ei => ei.Entry == node.Value) ||
					recorder.internalMarkedTypes.Any(t => t == node.Value));
				Console.WriteLine("node type: " + node.Value.GetType());
				System.Diagnostics.Debug.Assert (
					node.Value is MethodDefinition || node.Value is FieldDefinition || node.Value is TypeDefinition ||
					node.Value is MethodReference || node.Value is TypeReference || node.Value is FieldReference ||
					node.Value is PropertyDefinition || node.Value is EventDefinition ||
					node.Value is CustomAttribute || node.Value is SecurityAttribute || // ICustomAttribute
					node.Value is InterfaceImplementation ||
					node.Value is AssemblyDefinition
				);
				switch (node.Value) {
				case MethodDefinition method:
					System.Diagnostics.Debug.Assert(context.Annotations.IsMarked(method));
					break;
				case FieldDefinition field:
					System.Diagnostics.Debug.Assert(context.Annotations.IsMarked(field));
					break;
				case TypeDefinition type:
					System.Diagnostics.Debug.Assert(context.Annotations.IsMarked(type));
					break;
				case CustomAttribute attribute:
					System.Diagnostics.Debug.Assert(context.Annotations.IsMarked(attribute));
					break;
				case SecurityAttribute sa:
					// System.Diagnostics.Debug.Assert(context.Annotations.IsMarked(sa));
					// these never get marked in Annotations.
					break;
				case InterfaceImplementation implementation:
					System.Diagnostics.Debug.Assert(context.Annotations.IsMarked(implementation));
					break;
				case IMetadataTokenProvider tokenProvider:
					// method/type/field ref, or property/event
					System.Diagnostics.Debug.Assert(!context.Annotations.IsMarked(tokenProvider));
					break;
				default:
					throw new Exception("WAT");
				}
			}
		}

		// TODO: abstract this to not depend on node type.
		// it goes through IsStart interface, but directly accesses value.
		private List<string> FormatTrace (UnsafeReachingData reachingData, List<Edge<NodeInfo, DependencyKind>> edges) {
			// now, might not be an entry. could also be untracked.
			var trace = new List<string> ();
			var prefixes = new List<string> ();
			// get max prefix length for pretty formatting, then actually concatenate the strings


			// this will be printed even if no path was found.
			// that indicates either that there was no recorded path to the callsite,
			// or it was itself an entry point.
			// do we want to report untracked things here?

			prefixes.Add ($"unknown parameter passed to: "); // callee

			var immediateCaller = reachingData.callsite.caller;
			if (edges.Count == 0) {
			// 	// either there was no path from an entry because we aren't tracking some dependencies,
			// 	// or the callsite was in an entry point itself.
			// 	// at this point we don't know if it was an entry...
			// 	var node = recorder.GetOrCreateNode (immediateCaller);
			// 	System.Diagnostics.Debug.Assert (node.IsStart());
			// 	if (node.Entry) {
			// 		prefixes.Add ($"from entry: ");
			// 	} else if (node.Untracked) {
			// 		prefixes.Add ($"from untracked: ");
			// 	} else {
			// 		throw new Exception("invalid");
			// 	}
			} else {
				var end = edges [0].To;
				System.Diagnostics.Debug.Assert (end.Value == immediateCaller);
			}

			prefixes.Add ($"from: "); // corresponds to caller, which would be same as end.To if there was a path.

			Node<NodeInfo> from = new Node<NodeInfo> ();
			foreach (var edge in edges) {
				from = edge.From;

				var prefixString = edge.Info switch {
					DependencyKind.DirectCall => "called from",
					DependencyKind.VirtualCall => "maybe called virtually from",
					DependencyKind.UnanalyzedReflectionCall => "not understood in call from",
					DependencyKind.TriggersCctorThroughFieldAccess => "maybe triggered by field access from",
					DependencyKind.TriggersCctorForCalledMethod => "maybe triggered by method call to",
					DependencyKind.Override => "override of",
					DependencyKind.OverrideOnInstantiatedType => "of instantiated type",
					DependencyKind.MethodAccessedViaReflection => "reflected over by",
					DependencyKind.GenericArgumentType => "generic argument of",
					_ => $"{edge.Info}",
				};
				prefixString += ": ";
				prefixes.Add (prefixString);

				// not a real mark reason, but only found at end.
				// case DependencyKind.ContainsDangerousCallsite:
				// 	prefixes.Add ($"dangerous callsite: ");
				// 	break;
//				case MarkReasonKind.FieldAccess:
//					prefixes.Add ($"field accessed from: ");
//					break;
//				case MarkReasonKind.DeclaringTypeOfMethod:
//					prefixes.Add ($"declaring type of: ");
//					break;
//					throw new System.NotImplementedException ("tracing " + edge.Info.kind.ToString () + " dependencies is not supported");
			}

//			var outerNode = from;
			object outerValue;
			if (edges.Count > 0) {
//				outerNode = (MemberReference)from.Value;
				outerValue = from.Value;
			} else {
//				outerNode = reachingData.callsite.caller;
				outerValue = reachingData.callsite.caller;
			}

			// this is guaranteed to not create a new one, but retreive an existing one.
			// assert this.
			var outerNodeNode = recorder.GetOrCreateNode (outerValue);
			IEnumerable<EntryInfo> entryInfos = null;
			// this node was either untracked, or an entry node with a matching entryinfo.
			// if it was an entry...

			if (outerNodeNode.Entry) {
				// look for the entry;
				entryInfos = recorder.entryInfo.Where (ei => ei.Entry == outerNodeNode.Value);
				if (entryInfos.Count() == 0) {
					throw new Exception("no entry info found for entry node");
				}
				if (entryInfos.Count() > 1) {
					Console.WriteLine("multiple entry reasons...");
				}

				foreach (var entryInfo in entryInfos) {
					// found one that's not unknown!
					switch (entryInfo.Kind) {
					case EntryKind.XmlDescriptor:
						prefixes.Add ("kept from xml descriptor: ");
						break;
					case EntryKind.RootAssembly:
						prefixes.Add ("kept for root assembly: ");
						break;
					case EntryKind.AssemblyAction:
						prefixes.Add ("kept for copy/save assembly: ");
						break;
					default:
						throw new Exception("can't get here");
					}
				}
			}

			if (entryInfos != null && outerNodeNode.Entry) {
				foreach (var entryInfo in entryInfos) {
					if (entryInfo.Source == null) {
						throw new Exception("why is source null?!");
					}
				}
			}

			var prefixLength = prefixes.Select(p => p.Length).Max();

			int prefixIndex = 0;

			// build the trace
			trace.Add (String.Format ($"{{0,-{prefixLength}}}{{1}}", prefixes [prefixIndex++], reachingData.callsite.callee));
			trace.Add (String.Format ($"{{0,-{prefixLength}}}{{1}}", prefixes [prefixIndex++], immediateCaller));
			foreach (var edge in edges) {
				from = edge.From;
				trace.Add (String.Format ($"{{0,-{prefixLength}}}{{1}}", prefixes [prefixIndex++], from.Value));
			}

			// add entry reason to the trace.
			if (outerNodeNode.Entry) {
				System.Diagnostics.Debug.Assert(entryInfos != null);
				foreach (var entryInfo in entryInfos) {
					trace.Add (String.Format ($"{{0,-{prefixLength}}}{{1}}", prefixes [prefixIndex++], entryInfo.Source.ToString ()));
				}
			} else {
				throw new Exception("untracked!?");
			}

			System.Diagnostics.Debug.Assert (prefixIndex == prefixes.Count);

			return trace;

			// if we only report user code... there might be entry points
			// that aren't user code. what to do with these?

			// want to record all transitions to/from user code,
			// and report one node from each side. but hide everything else that is exclusively non-user code.

			// build a user-code trace
		// 	prefixIndex = 0;
		// 	var collapsedTrace = new List<string> ();
		// 	// always show innermost node
		// 	collapsedTrace.Add (String.Format ($"{{0,-{prefixLength}}}{{1}}", prefixes [prefixIndex++], end.Value));
		// 	// assume for now: all entry points are in user code.
		// 	// zero-length case. dangerous API might be in user code. handled above, no collapsing is necessary.
		// 	// assume for now: dangerous method is in non-user code.
		// 	System.Diagnostics.Debug.Assert (!IsUserCode (end));
		// 	// start a sequence.
// 
		// 	bool prevCallsNonUserCode = false;
// 
		// 	foreach (var edge in edges) {
		// 		var from = edge.From;
		// 		var to = edge.To;
		// 		if (IsUserCode (from) && IsUserCode (to)) {
		// 			// report user -> user.
		// 			collapsedTrace.Add (String.Format ($"{{0,-{prefixLength}}}{{1}}", prefixes [prefixIndex++], from.Value));
		// 			// report caller. callee was already reported.
		// 			prevCallsNonUserCode = false;
		// 		} else if (IsUserCode (from) && !IsUserCode (to)) {
		// 			// user -> non-user.
		// 			// report caller.
		// 			// may need to record callee if it wasn't already.
		// 			// callee will already have been reported if:
		// 			// - it was end, or
		// 			// - it was the only single node in non-user code (which itself called user code)
// 
		// 			// so we need to report if not end, and callee itself calls non-user code
		// 			// the latter will never be true for ends, so simplifies to:
		// 			// callee itself calls non-user code.
		// 			// can be summarized as:
		// 			// we need to report callee if it itself called non-user code
		// 			if (prevCallsNonUserCode) {
		// 				// report implementation detail
		// 				// TODO: integrate this with prefixes.
		// 				collapsedTrace.Add (String.Format ($"{{0,-{prefixLength}}}{{1}}", "in implementation of: ", to.Value));
		// 			}
		// 			// either way, report caller
		// 			collapsedTrace.Add (String.Format ($"{{0,-{prefixLength}}}{{1}}", prefixes [prefixIndex++], from.Value));
// 
		// 			prevCallsNonUserCode = true; // but this never matters
		// 			// since we only check the bool if prev was itself non-user code.
		// 			// but here we are in user code.
		// 		} else if (!IsUserCode (from) && IsUserCode (to)) {
		// 			// non-user -> user
		// 			// report immediate caller of non-user code
		// 			collapsedTrace.Add (String.Format ($"{{0,-{prefixLength}}}{{1}}", prefixes [prefixIndex++], from.Value));
// 
		// 			prevCallsNonUserCode = false;
		// 		} else if (!IsUserCode (from) && !IsUserCode (to)) {
		// 			// non-user -> non-user
		// 			// don't report.
		// 			prefixIndex++;
		// 			prevCallsNonUserCode = true;
		// 		}
		// 	}
// 
		// 	return collapsedTrace;
		// }
		}

		bool IsUserCode (Node<NodeInfo> node) {
			// TODO: determine whether it's part of:
			// context.Recorder.userAssemblies.
			// find assembly.
			var assembly = ((MemberReference)node.Value).Module.Assembly;
			if (context.Annotations.userAssemblies.Contains (assembly)) {
				return true;
			}
			return false;
		}

		public void WritePaths() {
			
		}

		public void Write() {
			// reachingdata -> set of traces to the callsite of the reachingdata
			foreach (var rd in unsafeReachingData) {
//				if (rd == null) {
//					throw new Exception("NUL");
//				}
//				if (rd.callsite == null) {
//					throw new Exception("nul callsite");
//				}
				if (rd.callsite.caller == null) {
					throw new Exception("nul caller");
				}
			}
			if (graph == null)
				throw new Exception ("null graph");
			if (recorder == null)
				throw new Exception ("null recorder");
			var paths = unsafeReachingData.ToDictionary (rd => rd, rd => {
				var shortestPaths = graph.GetShortestPathsTo (recorder.GetOrCreateNode (rd.callsite.caller), returnMultiple: false);
				if (shortestPaths == null) {
					throw new Exception("null shortestpaths");
				}
				return shortestPaths;
			});

//			foreach (var unsafeData in unsafeReachingData) {
				// report one shortest path for each unsafe reaching data.
				// and for each "entry".
				// get shortest path from each entry ending in this callsite.
				// we know the last part of the call chain. it must be in the graph.
				// maybe assert that it's in the graph. for now, just know that it is.
				// search for a path from entry to the beginning of this callsite.

				// var paths = graph.GetAllShortestPaths ();

//   			var pathsLists = paths.ToDictionary (
//  // 				kvp => new string [] { ((MemberReference)kvp.Key.value).Module.ToString(), kvp.Key.value.ToString () },
//  				kvp => "[" + ((MemberReference)kvp.Key.Value).Module.ToString() + "] " + kvp.Key.Value.ToString (),
//   				kvp => new HashSet<List<string>> (
//   					kvp.Value.Select (
//   						edges => FormatTrace (kvp.Key, edges)
//   					).ToList()
//   				)
//   			);

 			var pathsLists = new Dictionary<string, HashSet<List<string>>> ();
 			foreach (var kvp in paths) {
//				var mr = (MemberReference)kvp.Key.Value;
				var reachingData = kvp.Key;
				var categoryNode = reachingData.callsite.caller;

				// there might be multiple dangerous reaching datas for this method.
				// each reachingdata is treated as unique.
 			 	var start = "[" + ((MemberReference)categoryNode).Module.ToString () + "]" + categoryNode.ToString () + " -> " + reachingData.callsite.callee.ToString ();
				// var random = new System.Random();
				// string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
				// var start = new String(Enumerable.Repeat(chars, 100).Select(s => s[random.Next(s.Length)]).ToArray());
				// TODO: fix this.
				// because we are using value types, and the graph doesn't support updating value type fields
				// because it can't return any membersr by ref,
				// some methods get included in the graph twice.
				// CreateInstance: once as a callee,
				// and once again as a reflection target (where it's marked Unsafe, so the Node is different and gets inserted newly into the graph)
				// for now, work around this by including node info in the start string.
				// but really, we should avoid the problem by:
				// - not using value types
				//   involves more allocation on the heap. :(
				// - allowing mutation of value types, by allowing ref returns or mutator methods
				//   which would make the graph depend on the data type of NodeInfo. :(
				// - never insert a METHOD into the graph twice.
				//   the first time we see it, should know whether it's dangerous, entry, etc.
				//   or untracked. might be a problem if we insert untracked, then insert for a reason.
				//   solve by:
				//    not inserting untracked into the graph, storing them separately and only inserting for a reason
				//    actually, can we just get rid of the untracked field?
				//    and then determine the first time we see it whether it is entry or dangerous. YES!
				var mr = (MemberReference)categoryNode;
 			 	var trace = new HashSet<List<string>> (
 			 		kvp.Value.Select (edges => FormatTrace (reachingData, edges)).ToList ()
 			 	);
 
 			 	if (categoryNode is MethodDefinition method) {
 			 		// Console.WriteLine($"[{method.Module}]{method}");
					Console.WriteLine (mr.ToString ());
 			 	}
 			 	// this should never give back multiple trace SETS for a given entry. why does it?
 			 	if (!pathsLists.TryGetValue (start, out HashSet<List<string>> pathsList)) {
 			 		pathsList = trace;
 			 		pathsLists [start] = trace;
 			 	} else {
 			 		// why would we already have added a trace set for given start???
 			 		throw new Exception ("already got set of traces for node " + mr.ToString ());
 			 	}
 			 }

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