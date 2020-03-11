using System.Collections.Generic;
using System.Linq;
using System;

namespace Mono.Linker
{

	// public interface IEdge<T> {
	// 	T From { get; }
	// 	T To { get; }
	// }

	readonly public struct Node<NodeInfo> : IEquatable<Node<NodeInfo>> where NodeInfo : IStartEndInfo, IEquatable<NodeInfo> {
		public object Value { get; }
		public NodeInfo Info { get; }
		// nodes can be created specifynig an info or not, but once created
		// no info may be added.
		public Node (object value, in NodeInfo info) => (Value, Info) = (value, info);
		public Node (object value) => (Value, Info) = (value, default);

		// TODO: if perf is still a problem, access info directly.
		public bool IsStart() => Info.IsStart();
		public bool IsEnd() => Info.IsEnd();

		public bool Entry => Info.Entry;
		public bool Equals (Node<NodeInfo> node) => (Value, Info).Equals((node.Value, node.Info));
		public override bool Equals (Object o) => o is Node<NodeInfo> node && this.Equals (node);
		public override int GetHashCode () => (Value, Info).GetHashCode ();
		public static bool operator == (Node<NodeInfo> lhs, Node<NodeInfo> rhs) => lhs.Equals (rhs);
		public static bool operator != (Node<NodeInfo> lhs, Node<NodeInfo> rhs) => !lhs.Equals (rhs);
	}

	readonly public struct Edge<NodeInfo, EdgeInfo> : IEquatable<Edge<NodeInfo, EdgeInfo>> where NodeInfo : IStartEndInfo, IEquatable<NodeInfo> { //}, IEquatable<Edge<NodeInfo, EdgeInfo>> {
		public Node<NodeInfo> From { get; }
		public Node<NodeInfo> To { get; }
		public EdgeInfo Info { get; }
		public Edge (Node<NodeInfo> from, Node<NodeInfo> to, EdgeInfo info) => (From, To, Info) = (from, to, info);
		public bool Equals (Edge<NodeInfo, EdgeInfo> edge) => (From, To, Info).Equals((edge.From, edge.To, edge.Info));
		public override bool Equals (Object o) => o is Edge<NodeInfo, EdgeInfo> edge && this.Equals (edge);
		public override int GetHashCode () => (From, To, Info).GetHashCode ();
		public static bool operator == (Edge<NodeInfo, EdgeInfo> lhs, Edge<NodeInfo, EdgeInfo> rhs) => lhs.Equals (rhs);
		public static bool operator != (Edge<NodeInfo, EdgeInfo> lhs, Edge<NodeInfo, EdgeInfo> rhs) => !lhs.Equals (rhs);
	}

	// encapsulates a basic graph, with "Kind"s for each node and edge.
	// it does not track relationships between nodes and their inner values - that is the
	// responsibility of the consumer. this only operates on values that have already been raised to nodes.
	// the graph only tracks node/edge relationships.
	// it does not depend on the kinds or the node values.
	// it may have multiple nodes for the same object, same kind, or multiple edges between
	// the same nodes. the consumer is free to mutate
	// the node contents and kinds.
	// however, the nodes to which an edge refers must not be changed.

	// guessing that EdgeOfT : IEdge<T>
	// makes it so that it doesn't optimize the property accessess into simple field accesses.

	public class DependencyGraph<NodeInfo, EdgeInfo> where NodeInfo : IStartEndInfo, IEquatable<NodeInfo>
	{
		public readonly HashSet<Node<NodeInfo>> nodes;
		readonly HashSet<Edge<NodeInfo, EdgeInfo>> edges;
		public readonly Dictionary<Node<NodeInfo>, HashSet<Edge<NodeInfo, EdgeInfo>>> edgesFrom;
		public readonly Dictionary<Node<NodeInfo>, HashSet<Edge<NodeInfo, EdgeInfo>>> edgesTo;

		public DependencyGraph() {
			nodes = new HashSet<Node<NodeInfo>> ();
			edges = new HashSet<Edge<NodeInfo, EdgeInfo>> ();
			edgesFrom = new Dictionary<Node<NodeInfo>, HashSet<Edge<NodeInfo, EdgeInfo>>> ();
			edgesTo = new Dictionary<Node<NodeInfo>, HashSet<Edge<NodeInfo, EdgeInfo>>> ();
		}

//		public T untracked;
//		public HashSet<Edge<NodeInfo, EdgeInfo>> edgesFromUntracked;
//		public HashSet<Edge<NodeInfo, EdgeInfo>> edgesToUntracked;

		// public void AddUntrackedEdge(Node<NodeInfo> n, EdgeInfo edgeInfo) {
			// if (untracked == null) {
				// untracked = new T ();
				// nodes.Add (untracked);
// 
				// edgesFromUntracked = new HashSet<Edge<NodeInfo, EdgeInfo>> ();
				// edgesFrom [untracked] = edgesFromUntracked;
// 
				// edgesToUntracked = new HashSet<Edge<NodeInfo, EdgeInfo>> ();
				// edgesTo [untracked] = edgesToUntracked;
			// }
// 
			// var edge = new Edge<NodeInfo, EdgeInfo> (untracked, n, edgeInfo);
			// edges.Add (edge);
			// edgesFromUntracked.Add (edge);
			// edgesToUntracked.Add (edge);
		// }

		public void AddEdge (Edge<NodeInfo, EdgeInfo> e) {
			AddNode (e.From);
			AddNode (e.To);
			if (!edges.Add (e)) {
				return;
			}
			// TODO: how does dictionary work with value type equality?
			if (!edgesFrom.TryGetValue (e.From, out HashSet<Edge<NodeInfo, EdgeInfo>> fromEdges)) {
				fromEdges = new HashSet<Edge<NodeInfo, EdgeInfo>> ();
				edgesFrom [e.From] = fromEdges;
			}
			fromEdges.Add (e);
			if (!edgesTo.TryGetValue (e.To, out HashSet<Edge<NodeInfo, EdgeInfo>> toEdges)) {
				toEdges = new HashSet<Edge<NodeInfo, EdgeInfo>> ();
				edgesTo [e.To] = toEdges;
			}
			toEdges.Add (e);
		}

		public void AddNode(Node<NodeInfo> n) {
			nodes.Add (n);
		}
	}

	// this class does know about the node attribute type (maybe even the edge kind)
	// and uses this knowledge to search the graph.
	public interface INodeInfo {
		bool Dangerous { get; }
		bool Entry { get; }
	}

	public interface IStartEndInfo : INodeInfo {
		bool IsStart();
		bool IsEnd(); 
	}

	public class SearchableDependencyGraph<NodeInfo, EdgeInfo> : DependencyGraph<NodeInfo, EdgeInfo> where NodeInfo : IStartEndInfo, IEquatable<NodeInfo> {

		// This graph algorithm searches for paths that go from each startKind node
		// to each endKind, without going through endKind.
		// In other words, for each start node, it finds end nodes not dominated by any other
		// starting or end nodes, and returns a shortest path for each such end node.
		public Dictionary<Node<NodeInfo>, HashSet<List<Edge<NodeInfo, EdgeInfo>>>> GetAllShortestPaths() {
			var pathsFrom = new Dictionary<Node<NodeInfo>, HashSet<List<Edge<NodeInfo, EdgeInfo>>>> ();
			foreach (var start in nodes.Where(n => n.IsStart())) {
				var paths = GetShortestPathsFrom (start);
				if (paths.Count > 0) {
					throw new Exception("we shouldn't have multiple path sets for a starting point...");
				}
			}
			return pathsFrom;
		}

		// this finds a shortest path from each IsStart() node to the specified node.
		// this may be called on nodes that aren't end (dangerous) nodes.
		// in fact, we're no longer tracking nodes as dangerous on their own.
		// but they might be dangerous within some context.
		// we need to find all potentially dangerous contexts (callsites)
		// and find all paths from starts to those.
		// that don't themselves go through a dangerous edge, except to end in it.
		// since our context has length of one, we can just treat it as an edge.
		// might require a different algorithm in general.
		// don't want to go through any other dangerous edges.
		// if an edge is part of some dangerous data,
		// that edge still might participate in another dangerous
		// path to a different dangerous edge.
		// we don't want to discount such paths.
		// we do want to discount paths through other start nodes.
		// this will have the effct of only showing the most immediate start node to reach a dangerous edge.
		public List<Edge<NodeInfo, EdgeInfo>> GetShortestPathTo (Node<NodeInfo> end) {
			return GetShortestPathsTo (end, returnMultiple: false).Single();
		}

		public HashSet<List<Edge<NodeInfo, EdgeInfo>>> GetShortestPathsTo (Node<NodeInfo> end, bool returnMultiple = true) {
			var starts = new HashSet<Node<NodeInfo>> ();
			// end is NOT necessarily an IsEnd node.

			// find shortest path from start to endEdge.
			// may possibly go through another end edge.
			// but not through another start - only report immediate start nodes.

			// the shortest path ending with a suffix is the shortest path to the start of the suffix,
			// plus the suffix.
			// UNLESS the shortest path to start of suffix itself includes the path we care about?
			// no, that's not possible.
			// what if it goes to target of the suffix?
			// doesn't matter. just get shortest path to the node.

			// allow going through other public APIs?
			// allow going through other dangerous callsites?
			// really, need to do the search as a whole, after knowing all of the constraints.
			// but for now, just ignore those constraints and see what comes out.

			// just report shortest path from each start to this end, without going through other starts.
			// but may go through ends.

			var forwardEdge = new Dictionary<Node<NodeInfo>, Edge<NodeInfo, EdgeInfo>> ();
			var discovered = new HashSet<Node<NodeInfo>> { end };

			if (end.IsStart ()) {
				starts.Add (end);
				goto Return;
			}

			var queue = new Queue<Node<NodeInfo>> ();
			queue.Enqueue (end);
			while (!(queue.Count == 0)) {
				var u = queue.Dequeue ();

				if (!edgesTo.TryGetValue (u, out HashSet<Edge<NodeInfo, EdgeInfo>> toEdges))
					continue;

				foreach (var edge in toEdges) {
					var v = edge.From;

					if (!discovered.Add (v))
						continue;

					if (v.IsEnd ()) {
						// for symmetry, this ignores end nodes, and should continue.
						// however, right now I expect we never mark end nodes.
						throw new Exception ("unexpected end node!");
					}

					forwardEdge [v] = edge;

					if (v.IsStart ()) {
						starts.Add (v);
						if (!returnMultiple)
							goto Return;
						// don't consider paths that go through another start node.
						// only report immediate start nodes.
						continue;
					}

					queue.Enqueue (v);
				}
			}

		Return:
			var paths = new HashSet<List<Edge<NodeInfo, EdgeInfo>>> ();
			foreach (var start in starts) {
				var path = new List<Edge<NodeInfo, EdgeInfo>> ();
				var node = start;
				while (forwardEdge.TryGetValue (node, out Edge<NodeInfo, EdgeInfo> edge)) {
					path.Add (edge);
					node = edge.To;
				}

				path.Reverse ();

				paths.Add (path);
			}

			return paths;
		}

		// never returns null
		// empty set means no paths were found
		// set containing empty list means it's the path from the node to itself
		private HashSet<List<Edge<NodeInfo, EdgeInfo>>> GetShortestPathsFrom(Node<NodeInfo> start) {
			System.Diagnostics.Debug.Assert(start.IsStart ());

			var ends = new HashSet<Node<NodeInfo>> ();

			// tracks current shortest path
			var backEdge = new Dictionary<Node<NodeInfo>, Edge<NodeInfo, EdgeInfo>> ();
			var discovered = new HashSet<Node<NodeInfo>> {
				start
			};

			// zero-length path to self, if self is both start and end node
			if (start.IsEnd ()) {
				ends.Add (start);
				goto Return;
			}

			var queue = new Queue<Node<NodeInfo>> ();
			queue.Enqueue (start);
			while (!(queue.Count == 0)) {
				var u = queue.Dequeue ();

				// no neighbors
				if (!edgesFrom.TryGetValue (u, out HashSet<Edge<NodeInfo, EdgeInfo>> fromEdges))
					continue;

				foreach (var edge in fromEdges) {
					// TODO: how to check for equality on T?
					// implement IComparable?
					// System.Diagnostics.Debug.Assert (edge.From == u);
					var v = edge.To;

					// already discovered this neighbor
					if (!discovered.Add (v))
						continue;

					// ignore paths that go to a different source
					// even if it's a destination
					if (v.IsStart ())
						continue;

					// track the shortest path using back edges
					System.Diagnostics.Debug.Assert (!backEdge.ContainsKey (v));
					backEdge [v] = edge;

					// paths end at destination nodes. report and don't queue.
					if (v.IsEnd ()) {
						ends.Add (v);
						continue;
					}
					
					// consider paths that go through this neighbor
					queue.Enqueue (v);
				}
			}

			Return:
				var paths = new HashSet<List<Edge<NodeInfo, EdgeInfo>>> ();
				// ends is never null, but might be empty, indicating that no result was found.
				// no result will make paths an empty hashset.
				// path from start to itself would be 1-element hashset with empty list
				foreach (var end in ends) {
					var path = new List<Edge<NodeInfo, EdgeInfo>> ();
					// we never add a null path, but the path could be empty if the start was an end.
					var node = end;

					while (backEdge.TryGetValue (node, out Edge<NodeInfo, EdgeInfo> edge)) {
						path.Add (edge);
						node = edge.From;
					}

					paths.Add (path);
				}
				return paths;
		}
	}
}
