using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Mono.Linker.Analysis
{

	public struct IntBFSResult
	{
		public int [] prev;
		public int [] dist;
		public List<int> destinations;
		public int source;
	}

	public static class IntBFS
	{

		public static void AllPairsBFS (int [] [] neighbors,
									   bool [] isSource,
									   bool [] isDestination,
									   int numMethods,
									   bool excludePathsToSources = false,
									   bool [] ignoreEdgesTo = null,
									   bool [] ignoreEdgesFrom = null,
									   int [][] ignoreEdges = null,
									   Action<IntBFSResult> resultAction = null)
		{

			var sources = new List<int> ();
			for (int i = 0; i < numMethods; i++) {
				if (isSource [i]) {
					sources.Add (i);
				}
			}
			foreach (var source in sources) {
				var r = BFS (source, neighbors,
							isDestination: isDestination,
							isSource: isSource, // used to exclude paths that go through a different source
							ignoreEdgesTo: ignoreEdgesTo,
							ignoreEdgesFrom: ignoreEdgesFrom,
							ignoreEdges: ignoreEdges,
							returnMultiple: true);
				resultAction (r);
			}
		}

		// find paths from source to destination. selects a shortest path for each destination.
		// can report only the shortest path, or all paths.
		// can cutoff the search at some points - this is equivalent to assuming that
		// edges to -> cutoff node do not exist. they won't be traversed.
		// will only report one path to each destination found.

		// the search can be cut off in various ways:
		// by ignoring edges FROM a set of nodes (useful when an API is considered "safe", and we don't want to continue scanning)
		// by ignoring edges TO a set of nodes (useful when the target is a virtual method, and we want to ignore virtual calls)

		// find shortest path from source to the first interesting method
		// continuesearchingfrom is assumed to be a subset of isDestination.
		// that is, nonzeros that are not destinations are ignored.
		public static IntBFSResult BFS (int source,
									    int [] [] neighbors,
										bool [] isDestination,
									    bool [] isSource = null,
									    bool [] ignoreEdgesTo = null,
										bool [] ignoreEdgesFrom = null,
										int [] [] ignoreEdges = null,
									    bool returnMultiple = false,
									    bool includeEdgesFromSource = false)
		{
			var discovered = new bool [neighbors.Length];
			var q = new int [neighbors.Length];
			int q_begin = 0; // beginning or next element
			int q_end = 0; // one past the end.
			var prev = new int [neighbors.Length];
			var dist = new int [neighbors.Length];
			dist [source] = 0;
			int i;
			int numMethods = isDestination.Length;
			Debug.Assert (neighbors.Length == isDestination.Length);
			Debug.Assert (ignoreEdgesTo == null || ignoreEdgesTo.Length == neighbors.Length);
			for (i = 0; i < numMethods; i++) {
				dist [i] = int.MaxValue;
				prev [i] = i;
			}
			var destinations = new List<int> ();

			// this *should* report the zero-length path to self as a base case.
			if (isDestination[source]) {
				// but don't report path from destination to another destination. just report this node.
				Console.WriteLine("  is destination");
				destinations.Add(source);
				goto Return;
			}

			q [q_end] = source;
			q_end++;
			discovered [source] = true;
			while (q_begin < q_end) { // queue not empty
				var u = q [q_begin];
				q_begin++;

				if (neighbors [u] == null)
					continue;

				// ignore edges from?
				// we don't want to queue callers of virtual methods, since the linker
				// over-estimates these.
				if (ignoreEdgesFrom != null && ignoreEdgesFrom [u])
					continue;

				for (int iv = 0; iv < neighbors [u].Length; iv++) {
					var v = neighbors [u] [iv];

					if (discovered [v])
						continue;

					discovered [v] = true;

					int [] destinationsToIgnore = ignoreEdges? [u];
					if (destinationsToIgnore != null && destinationsToIgnore.Contains (v))
						continue;

					prev [v] = u;
					dist [v] = dist [u] + 1;

					// don't report paths from this source that go through a different source. ignore edges from other sources.
					// bottom-up, this means we're not reporting paths with multiple interesting methods on the stack.
					if (isSource != null && isSource [v])
						continue;

					// it's annotated safe (ignoreEdgesTo), it's not a destination and don't queue neighbors.
					if (ignoreEdgesTo != null && ignoreEdgesTo [v])
						continue;

					if (isDestination [v]) {
						destinations.Add (v);
						if (!returnMultiple)
							goto Return;
						// don't queue neighbors of a destination node.
						// we don't want to show paths to a dest through a different dest.
						continue;
					}

					q [q_end] = v;
					q_end++;
				}
			}
		Return:
			return new IntBFSResult { prev = prev, dist = dist, destinations = destinations, source = source };
		}
	}
}
