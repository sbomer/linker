//
// Tracer.cs
//
// Copyright (C) 2017 Microsoft Corporation (http://www.microsoft.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Mono.Linker
{

	public struct ReflectionData {
		public ReflectionDataKind kind;
		public string value; // null iff kind is Unknown.
	}

	public enum ReflectionDataKind {
		ResolvedString, // specific string
		UnresolvedString,
		Unknown
	}

	public struct Callsite {
		public MethodDefinition caller;
		public MethodDefinition callee;
	}

	public struct UnsafeReachingData {
		// call chain context
		// in our case, just one callsite
		// which includes caller and callee
		public Callsite callsite;

		// actual data value
		public ReflectionData data;
	}

	readonly public struct NodeInfo : IStartEndInfo, IEquatable<NodeInfo> {
		public bool Entry { get; }
		public bool Dangerous { get; }
		public bool IsStart() => Entry;
		public bool IsEnd() => Dangerous;
		public bool Equals (NodeInfo info) => (Entry, Dangerous).Equals((info.Entry, info.Dangerous));
		public override bool Equals (Object o) => o is NodeInfo info && this.Equals (info);
		public override int GetHashCode () => (Entry, Dangerous).GetHashCode ();
		public static bool operator == (NodeInfo lhs, NodeInfo rhs) => lhs.Equals (rhs);
		public static bool operator != (NodeInfo lhs, NodeInfo rhs) => !lhs.Equals (rhs);
		public NodeInfo (bool entry) => (Entry, Dangerous) = (entry, default);
	}

	/// <summary>
	/// Class which implements IDependencyRecorder and writes the dependencies into an in-memory graph.
	/// </summary>
	public class GraphDependencyRecorder : IDependencyRecorder, IReflectionPatternRecorder
	{
		// mark an instruction if:
		// we're marking a method, it's forceparse or parse with assembly action link, etc...
		// AND (with unreachable bodies opt) it's either static, instantiated, or not worth converting to throw

		// forceParse(m) & isNotUnreachableBody(m) => shouldProcessMethod(m)
		// markedMethod(m) & shouldProcessMethod(m) & methodCalls(m, n) => markedMethod(n)
		// how to deal with callvirt to a non-virtual method?
		// do we track override and the caller separately?
		// or make it look like the caller directly ends up in the override?
		// make it direct!
		readonly Dictionary<object, Node<NodeInfo>> raisedNode = new Dictionary<object, Node<NodeInfo>> ();

		public Node<NodeInfo> GetOrCreateNode (object o) {
			if (raisedNode.TryGetValue (o, out Node<NodeInfo> node))
				return node;
			// TODO: add appropriate node kind
			node = new Node<NodeInfo> (value: o);
			raisedNode [o] = node;
			return node;
		}

		private Node<NodeInfo> GetOrCreateEntryNode (object o) {
			// this should be the first time that we see it as an entry.
			if (raisedNode.TryGetValue (o, out Node<NodeInfo> node)) {
				// if the node already exists, it might already be an entry.
				if (node.Entry) {
					context.LogMessage ("duplicate entry for " + o.ToString ());
					return node;
				} else {
					// it already exsits in the graph, but not as an entry node...
					// this is a problem. the actual graph will not see the Entry bit set.

					// problem. this can happen pretty easily.
					// an "-a" library will have the "copy" action.
					// or, an "-a" executable may explicitly be set "copy".

					// for a library, ProcessLibrary will mark all types as entry types
					// AND MarkStep will go through "copy" assemblies, marking
					// all types again.
					// ResolveFromAssembly will mark the type from a RootAssembly.
					// MarkStep will list the assembly as an EntryAssembly, and mark the type as an AssemblyAction type.

					// ah... not that simple.
					// MarkEntireType can for example MarkField, which marks the field's type, etc...
					// so depending on ordering, we can get a particular type first as either an
					// entry OR as a normal dependency type.

					// what should we do when an entry => a type, and that type is also an entry itself?

					// multiple entries: possible.
					// entry and not entry: impossible.
					// entry and other dependency: possible
					//   prefer the entry reason if it exists.
					//   marking something as an entry will prevent tracing other reasons to keep it.
					//   need a way to mark it as entry without tracing?
					// AVOID:
					//   want to make sure we never hit a dependency node BEFORE it's marked as an entry.
					//   but the algorithm currently does that.
					//   without further changes... would be hard to do.
					//   could only consider things entries in ResolveFromAssemblyStep,
					//   and not here. then how to account for MarkEntireAssembly things?
					//     could mark assembly as an entry, and these as a dependency of the assembly...
					//     maybe assemblies belong in the graph even though they are never marked.
					// problem is that an assembly can be conceptually a dependency, but not kept.
					//  how?
					throw new Exception("attempted to add entry node for extant non-entry node");
				}
			} else {
				// it didn't already exist... create it, set the bit, and cache it.
				node = new Node<NodeInfo> (value: o, new NodeInfo (entry: true));
				raisedNode [o] = node;
				return node;
			}
		}

		public readonly SearchableDependencyGraph<NodeInfo, DependencyKind> graph;

		// in addition to a callgraph, this also stores information about non-understood dataflow to sometimes unsafe methods.
		// potentially unsafe dataflow results.
		public readonly HashSet<UnsafeReachingData> unsafeReachingData;
		public readonly Dictionary<object, HashSet<DependencyInfo>> entryReasons;
		public readonly HashSet<TypeDefinition> internalMarkedTypes;
		LinkContext context;

		public GraphDependencyRecorder (LinkContext context) {
			graph = new SearchableDependencyGraph<NodeInfo, DependencyKind> ();
			unsafeReachingData = new HashSet<UnsafeReachingData> ();
			entryReasons = new Dictionary<object, HashSet<DependencyInfo>> ();
			internalMarkedTypes = new HashSet<TypeDefinition> ();
			this.context = context;
		}

		public void RecordDependency (object source, object target, bool marked) {
			// called for the stack-based tracer dependencies.
			// don't record these.
		}

		private void RecordDependency (object target, DependencyInfo reason) {
			// LinkerInternal has no source.
			if (reason.Kind == DependencyKind.LinkerInternal) {
				Debug.Assert (reason.Source == null);
				Debug.Assert (target is TypeDefinition);
				graph.AddNode (GetOrCreateNode (target));
				internalMarkedTypes.Add (target as TypeDefinition);
				return;
			}

			// if it's an entry? add the entry node to the graph,
			// and record entryinfo.
			if (reason.Kind == DependencyKind.RootAssembly) {
				Debug.Assert (reason.Source is AssemblyDefinition);
				graph.AddNode (GetOrCreateEntryNode (target));
				AddEntryReason (target, reason);
				return;
			}

			if (reason.Kind == DependencyKind.XmlDescriptor) {
				Debug.Assert (reason.Source is string);
				graph.AddNode (GetOrCreateEntryNode (target));
				AddEntryReason (target, reason);
				return;
			}

			if (reason.Kind == DependencyKind.AssemblyOrModuleCustomAttribute) {
				Debug.Assert (reason.Source is AssemblyDefinition || reason.Source is ModuleDefinition);
				Debug.Assert (target is CustomAttribute || target is SecurityAttribute);
				graph.AddNode (GetOrCreateEntryNode (target));
				AddEntryReason (target, reason);
				return;
			}

			if (reason.Kind == DependencyKind.AssemblyAction) {
				Debug.Assert (reason.Source is AssemblyAction);
				Debug.Assert (target is AssemblyDefinition);
				graph.AddNode (GetOrCreateEntryNode (target));
				AddEntryReason (target, reason);
				return;
			}

			// not an entry...
			Debug.Assert (
				reason.Source is MemberReference ||
				reason.Source is ICustomAttribute ||
				reason.Source is InterfaceImplementation ||
				reason.Source is AssemblyDefinition // for TypeInAssembly
				// we never mark anything as a dependency of a ModuleDefinition
				// for entry points, could be an:
				// assembly, string (xml descriptor), assembly action
				// TypeInAssembly?
			);
			Debug.Assert (
				target is MemberReference ||
				target is ICustomAttribute ||
				target is InterfaceImplementation ||
				target is ModuleDefinition || // we mark modules and assemblies as scopes of types
				target is AssemblyDefinition
			);

			if (reason.Source == null)
				throw new InvalidOperationException ("no source provided for reason " + reason.Kind.ToString ());

			graph.AddEdge (new Edge<NodeInfo, DependencyKind> (GetOrCreateNode (reason.Source), GetOrCreateNode (target), reason.Kind));
		}

		public void RecordDependency (object target, DependencyInfo reason, bool marked) {
			switch (target) {
			case IMetadataTokenProvider provider:
				if (marked)
					Debug.Assert (context.Annotations.IsMarked (provider));
				break;
			case CustomAttribute attribute:
				if (marked)
					Debug.Assert (context.Annotations.IsMarked (attribute));
				break;
			case SecurityAttribute attribute:
				// we never mark security attributes
				Debug.Assert (!marked);
				break;
			default:
				throw new Exception ("non-understood target!");
			}
			RecordDependency (target, reason);
		}

		public void RecognizedReflectionAccessPattern (MethodDefinition sourceMethod, MethodDefinition reflectionMethod, IMemberDefinition accessedItem)
		{
			// Do nothing - there's no logging for successfully recognized patterns
		}

		public void UnrecognizedReflectionAccessPattern (MethodDefinition sourceMethod, MethodDefinition reflectionMethod, string message)
		{
			var callsite = new Callsite { caller = sourceMethod, callee = reflectionMethod };
			var reflectionData = new ReflectionData { kind = ReflectionDataKind.Unknown };
			var reachingData = new UnsafeReachingData { callsite = callsite, data = reflectionData };
			unsafeReachingData.Add (reachingData);
		}

		private void AddEntryReason (object o, DependencyInfo reason) {
			Debug.Assert (
				reason.Kind == DependencyKind.AssemblyAction ||
				reason.Kind == DependencyKind.AssemblyOrModuleCustomAttribute ||
				reason.Kind == DependencyKind.XmlDescriptor ||
				reason.Kind == DependencyKind.RootAssembly
			);
			if (!entryReasons.TryGetValue (o, out HashSet<DependencyInfo> reasons)) {
				reasons = new HashSet<DependencyInfo> ();
				entryReasons [o] = reasons;
			}
			reasons.Add (reason);
		}
	}
}
