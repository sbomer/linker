using System.Collections.Generic;
using System.Linq;
using System;

namespace Mono.Linker
{

	// public interface IEdge<T> {
	// 	T From { get; }
	// 	T To { get; }
	// }

	public struct Node<NodeInfo> where NodeInfo : IStartEndInfo {
		public object Value;
		public NodeInfo info;
		public Node (object value, NodeInfo info = default) {
			this.Value = value;
			this.info = info;
		}

		// TODO: if perf is still a problem, access info directly.
		public bool IsStart() => info.IsStart();
		public bool IsEnd() => info.IsEnd();

		public bool Dangerous {
			get => info.Dangerous;
			set => info.Dangerous = value;
		}
		public bool Untracked {
			get => info.Untracked;
			set => info.Untracked = value;
		}
		public bool Entry {
			get => info.Entry;
			set => info.Entry = value;
		}
	}

	public struct Edge<NodeInfo, EdgeInfo> where NodeInfo : IStartEndInfo {
		public Node<NodeInfo> From;
		public Node<NodeInfo> To;
		public EdgeInfo Info;
		public Edge (Node<NodeInfo> from, Node<NodeInfo> to, EdgeInfo info) {
			this.From = from;
			this.To = to;
			this.Info = info;
		}
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

	public class DependencyGraph<NodeInfo, EdgeInfo> where NodeInfo : IStartEndInfo
	{
		public readonly HashSet<Node<NodeInfo>> nodes;
		readonly HashSet<Edge<NodeInfo, EdgeInfo>> edges;
		public readonly Dictionary<Node<NodeInfo>, HashSet<Edge<NodeInfo, EdgeInfo>>> edgesFrom;
		readonly Dictionary<Node<NodeInfo>, HashSet<Edge<NodeInfo, EdgeInfo>>> edgesTo;

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
			nodes.Add (e.From);
			nodes.Add (e.To);
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
		bool Dangerous { get; set; }
		bool Untracked { get; set; }
		bool Entry { get; set; }
	}

	public interface IStartEndInfo : INodeInfo {
		bool IsStart();
		bool IsEnd(); 
	}

	public class SearchableDependencyGraph<NodeInfo, EdgeInfo> : DependencyGraph<NodeInfo, EdgeInfo> where NodeInfo : IStartEndInfo {

		// This graph algorithm searches for paths that go from each startKind node
		// to each endKind, without going through endKind.
		// In other words, for each start node, it finds end nodes not dominated by any other
		// starting or end nodes, and returns a shortest path for each such end node.
		public Dictionary<Node<NodeInfo>, HashSet<List<Edge<NodeInfo, EdgeInfo>>>> GetAllShortestPaths() {
			var pathsFrom = new Dictionary<Node<NodeInfo>, HashSet<List<Edge<NodeInfo, EdgeInfo>>>> ();
			foreach (var start in nodes.Where(n => n.IsStart())) {
				var paths = GetShortestPaths (start);
				if (paths.Count > 0)
					pathsFrom [start] = GetShortestPaths (start);
			}
			return pathsFrom;
		}

		// never returns null
		// empty set means no paths were found
		// set containing empty list means it's the path from the node to itself
		private HashSet<List<Edge<NodeInfo, EdgeInfo>>> GetShortestPaths(Node<NodeInfo> start) {
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
