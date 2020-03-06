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

	public struct NodeInfo : IStartEndInfo {
		public bool untracked;
		public bool entry;
		public bool dangerous;
		public bool IsStart() => entry || untracked;
		public bool IsEnd() => dangerous;

		public bool Dangerous {
			get => dangerous;
			set => dangerous = value;
		}
		public bool Untracked {
			get => untracked;
			set => untracked = value;
		}
		public bool Entry {
			get => entry;
			set => entry = value;
		}
	}

	/// <summary>
	/// Class which implements IDependencyRecorder and writes the dependencies into an in-memory graph.
	/// </summary>
	public class GraphDependencyRecorder : IRuleDependencyRecorder
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
					Console.Error.WriteLine ("duplicate entry for " + o.ToString ());
					return node;
				} else {
					// it already exsits in the graph, but not as an entry node...
					// this is a problem. the actual graph will not see the Entry bit set.
					throw new Exception("attempted to add entry node for extant non-entry node");
				}
			} else {
				// it didn't already exist... create it, set the bit, and cache it.
				node = new Node<NodeInfo> (value: o);
				node.Entry = true;
				raisedNode [o] = node;
				return node;
			}
		}

		private Node<NodeInfo> GetOrCreateDangerousNode (object o) {
			// TODO: get rid?
			var n = GetOrCreateNode (o);
			n.Dangerous = true;
			raisedNode [o] = n;
			return n;
		}

		private Node<NodeInfo> GetOrCreateUntrackedNode (object o) {
			// dangerous.
			// this can mutate raisedNode properties,
			// without altering them in the graph.

			// we track on theh node whether it was untracked or entry, so that the search
			// can stop at such nodes. if we have marked it as an entry, we want to
			// prefer that rather than untracked, so only actually mark it untracked
			// if it's not already an entry.
			// AND when marking entry, assert that it's not untracked.

			// if not entry, this should be the first time.
			// this might mutate a node that already was seen for another reason.
			if (raisedNode.TryGetValue (o, out Node<NodeInfo> node)) {
				// because we are never mutating the graph,
				// we are forgetting about some untracked.
				// we will now only report errors for nodes that are completely untracked.
				// nodes that are partially untracked are reported, but there might still be a shorter path
				// available with better tracking. FIX THS!



				// this is already here. we don't want to mutate it.
				// but it might not be untracked now.
				// TODO: ensure we track everything!
				if (node.Entry) {
					// System.Diagnostics.Debug.Assert (!node.Untracked);
					// seems to not be true for copy corelib... WHY? figure out later.
					return node;
				}
				if (!node.Untracked) {
					Console.Error.WriteLine("forgetting untracked node! " + o.ToString());
				}
				return node;
			}

			node = new Node<NodeInfo> (value: o);
			node.Untracked = true;

			raisedNode [o] = node;
			return node;
		}

		public readonly SearchableDependencyGraph<NodeInfo, DependencyInfo> graph;

		// in addition to a callgraph, this also stores information about non-understood dataflow to sometimes unsafe methods.
		// potentially unsafe dataflow results.
		public readonly HashSet<UnsafeReachingData> unsafeReachingData;
		public readonly HashSet<EntryInfo> entryInfo;

		public GraphDependencyRecorder () {
			graph = new SearchableDependencyGraph<NodeInfo, DependencyInfo> ();
			unsafeReachingData = new HashSet<UnsafeReachingData> ();
			entryInfo = new HashSet<EntryInfo> ();
		}

		public void RecordMethodWithReason (DependencyInfo reason, MethodDefinition method) {
			graph.AddEdge (new Edge<NodeInfo, DependencyInfo> (GetOrCreateNode (reason.source), GetOrCreateNode (method), reason));
		}

		public void RecordFieldWithReason (DependencyInfo reason, FieldDefinition field) {
			graph.AddEdge (new Edge<NodeInfo, DependencyInfo> (GetOrCreateNode (reason.source), GetOrCreateNode (field), reason));
		}

		public void RecordTypeWithReason (DependencyInfo reason, TypeDefinition type) {
			graph.AddEdge (new Edge<NodeInfo, DependencyInfo> (GetOrCreateNode (reason.source), GetOrCreateNode (type), reason));
		}

		public void RecordCustomAttribute (DependencyInfo reason, CustomAttribute ca) {
			graph.AddEdge (new Edge<NodeInfo, DependencyInfo> (GetOrCreateNode (reason.source), GetOrCreateNode (ca), reason));
		}

		public void RecordDirectCall (MethodDefinition caller, MethodDefinition callee) {
			// TODO: track instruction index
			graph.AddEdge (new Edge<NodeInfo, DependencyInfo> (GetOrCreateNode (caller), GetOrCreateNode (callee), new DependencyInfo { kind = DependencyKind.DirectCall }));
		}
		public void RecordVirtualCall (MethodDefinition caller, MethodDefinition callee) {
			// TODO: track instruction index
			graph.AddEdge (new Edge<NodeInfo, DependencyInfo> (GetOrCreateNode (caller), GetOrCreateNode (callee), new DependencyInfo { kind = DependencyKind.VirtualCall }));
		}

		// read as: Record ContextSensitiveData reaches
		// to mean that reflectionMethod is reached with data that is an unknown or unresolved string, from context ending with callsite in source.
		public void RecordUnanalyzedReflectionCall (MethodDefinition source, MethodDefinition reflectionMethod, int instructionIndex, ReflectionData data) {
			Console.WriteLine ("bad reflection: " + source.ToString() + " -> " + reflectionMethod.ToString());
			// when we have an unanalyzed reflection call, put the callsite into the graph.
			// just use valuetuple for now.
			// and add an edge from the containing method of the callsite to the dangerous callsite.
			// var callsite = (source, reflectionMethod, instructionIndex);
			// graph.AddEdge (new Edge<NodeInfo, DependencyInfo> (GetOrCreateNode (source), GetOrCreateDangerousNode (callsite), new DependencyInfo { kind = DependencyKind.ContainsDangerousCallsite }));
			//graph.AddEdge (new Edge<NodeInfo, MarkReasonKind> (GetOrCreateNode (source), GetOrCreateNode (reflectionMethod), MarkReasonKind.UnanalyzedReflectionCall));
			var callsite = new Callsite { caller = source, callee = reflectionMethod };
			var reachingData = new UnsafeReachingData { callsite = callsite, data = data };
			unsafeReachingData.Add (reachingData);
		}
		public void RecordAnalyzedReflectionAccess (MethodDefinition source, MethodDefinition target) {
			Console.WriteLine ("good reflection: " + source.ToString() + " -> " + target.ToString());
			graph.AddEdge (new Edge<NodeInfo, DependencyInfo> (GetOrCreateNode (source), GetOrCreateNode (target), new DependencyInfo { kind = DependencyKind.MethodAccessedViaReflection }));
		}

		public void RecordOverride (MethodDefinition @base, MethodDefinition @override) {
			graph.AddEdge (new Edge<NodeInfo, DependencyInfo> (GetOrCreateNode (@base), GetOrCreateNode (@override), new DependencyInfo { kind = DependencyKind.Override }));
		}

		public void RecordScopeOfType (TypeDefinition type, IMetadataScope scope) {
			graph.AddEdge (new Edge<NodeInfo, DependencyInfo> (GetOrCreateNode (type), GetOrCreateNode (scope), new DependencyInfo { kind = DependencyKind.ScopeOfType }));
		}

		public void RecordInstantiatedByConstructor (MethodDefinition ctor, TypeDefinition type) {
			graph.AddEdge (new Edge<NodeInfo, DependencyInfo> (GetOrCreateNode (ctor), GetOrCreateNode (@type), new DependencyInfo { kind = DependencyKind.ConstructedType }));
		}

		public void RecordOverrideOnInstantiatedType (TypeDefinition type, MethodDefinition method) {
			graph.AddEdge (new Edge<NodeInfo, DependencyInfo> (GetOrCreateNode (type), GetOrCreateNode (method), new DependencyInfo { kind = DependencyKind.OverrideOnInstantiatedType }));
		}

		public void RecordEntryType (TypeDefinition type, EntryInfo info) {
			var node = GetOrCreateEntryNode (type);
			graph.AddNode (node);
			entryInfo.Add (info);
		}

		// TODO: combine entry helpers into one?
		public void RecordAssemblyCustomAttribute (CustomAttribute ca, EntryInfo info) {
			var node = GetOrCreateEntryNode (ca);
			graph.AddNode (node);
			entryInfo.Add (info);
		}
		public void RecordEntryField (FieldDefinition field, EntryInfo info) {
			var node = GetOrCreateEntryNode (field);
			graph.AddNode (node);
			entryInfo.Add (info);
		}
		public void RecordEntryMethod (MethodDefinition method, EntryInfo info) {
			var node = GetOrCreateEntryNode (method);
			System.Diagnostics.Debug.Assert (info.entry == method);
			graph.AddNode (node);
			entryInfo.Add (info);
		}

		public void RecordDangerousMethod (MethodDefinition method) {
			var node = GetOrCreateDangerousNode (method);
			graph.AddNode (node);
		}

		public void RecordNestedType (TypeDefinition declaringType, TypeDefinition nestedType) {
			graph.AddEdge (new Edge<NodeInfo, DependencyInfo> (GetOrCreateNode (declaringType), GetOrCreateNode (nestedType), new DependencyInfo { kind = DependencyKind.NestedType }));
		}

		public void RecordUserDependencyType (CustomAttribute ca, TypeDefinition type) {
			graph.AddEdge (new Edge<NodeInfo, DependencyInfo> (GetOrCreateNode (ca), GetOrCreateNode (type), new DependencyInfo { kind = DependencyKind.UserDependencyType }));
		}

		public void RecordFieldAccessFromMethod (MethodDefinition method, FieldDefinition field) {
			graph.AddEdge (new Edge<NodeInfo, DependencyInfo> (GetOrCreateNode (method), GetOrCreateNode (field), new DependencyInfo { kind = DependencyKind.FieldAccess }));
		}

		public void RecordTriggersStaticConstructorThroughFieldAccess (MethodDefinition method, MethodDefinition cctor) {
			graph.AddEdge (new Edge<NodeInfo, DependencyInfo> (GetOrCreateNode (method), GetOrCreateNode (cctor), new DependencyInfo { kind = DependencyKind.TriggersCctorThroughFieldAccess }));
		}

		public void RecordTriggersStaticConstructorForCalledMethod (MethodDefinition method, MethodDefinition cctor) {
			graph.AddEdge (new Edge<NodeInfo, DependencyInfo> (GetOrCreateNode (method), GetOrCreateNode (cctor), new DependencyInfo { kind = DependencyKind.TriggersCctorForCalledMethod }));
		}

		public void RecordStaticConstructorForField (FieldDefinition field, MethodDefinition cctor) {
			graph.AddEdge (new Edge<NodeInfo, DependencyInfo> (GetOrCreateNode (field), GetOrCreateNode (cctor), new DependencyInfo { kind = DependencyKind.CctorForField }));
		}

		public void RecordDeclaringTypeOfMethod (MethodDefinition method, TypeDefinition type) {
			graph.AddEdge (new Edge<NodeInfo, DependencyInfo> (GetOrCreateNode (method), GetOrCreateNode (type), new DependencyInfo { kind = DependencyKind.DeclaringTypeOfMethod }));
		}
		public void RecordDeclaringTypeOfType (TypeDefinition type, TypeDefinition parent) {
			graph.AddEdge (new Edge<NodeInfo, DependencyInfo> (GetOrCreateNode (type), GetOrCreateNode (parent), new DependencyInfo { kind = DependencyKind.DeclaringTypeOfType }));
		}

		public void RecordFieldUntracked (FieldDefinition field) {
			// TODO: maybe make this more performant?
			var node = GetOrCreateUntrackedNode (field);
			if (node.Untracked)
				return;
//			graph.AddNode (node);
		}

		// problem: can be an untracked dependency.
		// but we don't want to create a new untracked node each time...
		// ENTRY must be set the first time.
		// DANGEROUS is not set on a node, but on an edge.
		public void RecordTypeUntracked (TypeDefinition type) {
			var node = GetOrCreateUntrackedNode (type);
			if (node.Untracked)
				return;
//			graph.AddNode (node);
		}

		public void RecordMethodUntracked (MethodDefinition method) {
			var node = GetOrCreateUntrackedNode (method);
			if (node.Untracked)
				return;
			graph.AddNode (node);
		}
	}
}
