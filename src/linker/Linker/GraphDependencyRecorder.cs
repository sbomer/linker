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
		public readonly HashSet<EntryInfo> entryInfo;
		public readonly HashSet<TypeDefinition> internalMarkedTypes;
		LinkContext context;

		public GraphDependencyRecorder (LinkContext context) {
			graph = new SearchableDependencyGraph<NodeInfo, DependencyKind> ();
			unsafeReachingData = new HashSet<UnsafeReachingData> ();
			entryInfo = new HashSet<EntryInfo> ();
			internalMarkedTypes = new HashSet<TypeDefinition> ();
			this.context = context;
		}

		public void RecordTypeLinkerInternal (TypeDefinition type) {
			graph.AddNode (GetOrCreateNode (type));
			internalMarkedTypes.Add (type);
		}

		public void RecordMethodWithReason (DependencyInfo reason, MethodDefinition method) {
			graph.AddEdge (new Edge<NodeInfo, DependencyKind> (GetOrCreateNode (reason.Source), GetOrCreateNode (method), reason.Kind));
		}

		public void RecordFieldWithReason (DependencyInfo reason, FieldDefinition field) {
			graph.AddEdge (new Edge<NodeInfo, DependencyKind> (GetOrCreateNode (reason.Source), GetOrCreateNode (field), reason.Kind));
		}

		public void RecordTypeWithReason (DependencyInfo reason, TypeDefinition type) {
			graph.AddEdge (new Edge<NodeInfo, DependencyKind> (GetOrCreateNode (reason.Source), GetOrCreateNode (type), reason.Kind));
		}

		public void RecordTypeSpecWithReason (DependencyInfo reason, TypeSpecification spec) {
			graph.AddEdge (new Edge<NodeInfo, DependencyKind> (GetOrCreateNode (reason.Source), GetOrCreateNode (spec), reason.Kind));
		}
		public void RecordMethodSpecWithReason (DependencyInfo reason, MethodSpecification spec) {
			graph.AddEdge (new Edge<NodeInfo, DependencyKind> (GetOrCreateNode (reason.Source), GetOrCreateNode (spec), reason.Kind));
		}

		public void RecordFieldOnGenericInstance (DependencyInfo reason, FieldReference field) {
			graph.AddEdge (new Edge<NodeInfo, DependencyKind> (GetOrCreateNode (reason.Source), GetOrCreateNode (field), reason.Kind));
		}

		public void RecordMethodOnGenericInstance (DependencyInfo reason, MethodReference method) {
			graph.AddEdge (new Edge<NodeInfo, DependencyKind> (GetOrCreateNode (reason.Source), GetOrCreateNode (method), reason.Kind));
		}

		public void RecordCustomAttribute (DependencyInfo reason, ICustomAttribute ca) {
			graph.AddEdge (new Edge<NodeInfo, DependencyKind> (GetOrCreateNode (reason.Source), GetOrCreateNode (ca), reason.Kind));
		}

		public void RecordPropertyWithReason (DependencyInfo reason, PropertyDefinition property) {
			graph.AddEdge (new Edge<NodeInfo, DependencyKind> (GetOrCreateNode (reason.Source), GetOrCreateNode (property), reason.Kind));
		}

		public void RecordEventWithReason (DependencyInfo reason, EventDefinition evt) {
			graph.AddEdge (new Edge<NodeInfo, DependencyKind> (GetOrCreateNode (reason.Source), GetOrCreateNode (evt), reason.Kind));
		}

		public void RecordDirectCall (MethodDefinition caller, MethodDefinition callee) {
			// TODO: track instruction index
			graph.AddEdge (new Edge<NodeInfo, DependencyKind> (GetOrCreateNode (caller), GetOrCreateNode (callee), DependencyKind.DirectCall));
		}

		public void RecordVirtualCall (MethodDefinition caller, MethodDefinition callee) {
			// TODO: track instruction index
			graph.AddEdge (new Edge<NodeInfo, DependencyKind> (GetOrCreateNode (caller), GetOrCreateNode (callee), DependencyKind.VirtualCall));
		}

		// read as: Record ContextSensitiveData reaches
		// to mean that reflectionMethod is reached with data that is an unknown or unresolved string, from context ending with callsite in source.
		public void RecordUnanalyzedReflectionCall (MethodDefinition source, MethodDefinition reflectionMethod, int instructionIndex, ReflectionData data) {
			// when we have an unanalyzed reflection call, put the callsite into the graph.
			// just use valuetuple for now.
			// and add an edge from the containing method of the callsite to the dangerous callsite.
			// var callsite = (source, reflectionMethod, instructionIndex);
			var callsite = new Callsite { caller = source, callee = reflectionMethod };
			var reachingData = new UnsafeReachingData { callsite = callsite, data = data };
			unsafeReachingData.Add (reachingData);
		}
		public void RecordAnalyzedReflectionAccess (MethodDefinition source, MethodDefinition target) {
			graph.AddEdge (new Edge<NodeInfo, DependencyKind> (GetOrCreateNode (source), GetOrCreateNode (target), DependencyKind.MethodAccessedViaReflection));
		}

		public void RecordOverride (MethodDefinition @base, MethodDefinition @override) {
			graph.AddEdge (new Edge<NodeInfo, DependencyKind> (GetOrCreateNode (@base), GetOrCreateNode (@override), DependencyKind.Override));
		}

		// public void RecordScopeOfType (TypeDefinition type, IMetadataScope scope) {
		// 	graph.AddEdge (new Edge<NodeInfo, DependencyKind> (GetOrCreateNode (type), GetOrCreateNode (scope), DependencyKind.ScopeOfType));
		// }

		public void RecordInstantiatedByConstructor (MethodDefinition ctor, TypeDefinition type) {
			graph.AddEdge (new Edge<NodeInfo, DependencyKind> (GetOrCreateNode (ctor), GetOrCreateNode (type), DependencyKind.ConstructedType));
		}

		public void RecordOverrideOnInstantiatedType (TypeDefinition type, MethodDefinition method) {
			graph.AddEdge (new Edge<NodeInfo, DependencyKind> (GetOrCreateNode (type), GetOrCreateNode (method), DependencyKind.OverrideOnInstantiatedType));
		}

		public void RecordInterfaceImplementation (TypeDefinition type, InterfaceImplementation iface) {
			graph.AddEdge (new Edge<NodeInfo, DependencyKind> (GetOrCreateNode (type), GetOrCreateNode (iface), DependencyKind.InterfaceImplementationOnType));
		}

		public void RecordEntryType (TypeDefinition type, EntryInfo info) {
			var node = GetOrCreateEntryNode (type);
			graph.AddNode (node);
			entryInfo.Add (info);
		}

		public void RecordEntryAssembly (AssemblyDefinition assembly, EntryInfo info) {
			var node = GetOrCreateEntryNode (assembly);
			graph.AddNode (node);
			entryInfo.Add (info);
		}

		// TODO: combine entry helpers into one?
		public void RecordAssemblyCustomAttribute (ICustomAttribute ca, EntryInfo info) {
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
			System.Diagnostics.Debug.Assert (info.Entry == method);
			graph.AddNode (node);
			entryInfo.Add (info);
		}

		public void RecordNestedType (TypeDefinition declaringType, TypeDefinition nestedType) {
			graph.AddEdge (new Edge<NodeInfo, DependencyKind> (GetOrCreateNode (declaringType), GetOrCreateNode (nestedType), DependencyKind.NestedType));
		}

		public void RecordUserDependencyType (CustomAttribute ca, TypeDefinition type) {
			graph.AddEdge (new Edge<NodeInfo, DependencyKind> (GetOrCreateNode (ca), GetOrCreateNode (type), DependencyKind.UserDependencyType));
		}

		public void RecordFieldAccessFromMethod (MethodDefinition method, FieldDefinition field) {
			graph.AddEdge (new Edge<NodeInfo, DependencyKind> (GetOrCreateNode (method), GetOrCreateNode (field), DependencyKind.FieldAccess));
		}

		public void RecordTriggersStaticConstructorThroughFieldAccess (MethodDefinition method, MethodDefinition cctor) {
			graph.AddEdge (new Edge<NodeInfo, DependencyKind> (GetOrCreateNode (method), GetOrCreateNode (cctor), DependencyKind.TriggersCctorThroughFieldAccess));
		}

		public void RecordTriggersStaticConstructorForCalledMethod (MethodDefinition method, MethodDefinition cctor) {
			graph.AddEdge (new Edge<NodeInfo, DependencyKind> (GetOrCreateNode (method), GetOrCreateNode (cctor), DependencyKind.TriggersCctorForCalledMethod));
		}

		public void RecordStaticConstructorForField (FieldDefinition field, MethodDefinition cctor) {
			graph.AddEdge (new Edge<NodeInfo, DependencyKind> (GetOrCreateNode (field), GetOrCreateNode (cctor), DependencyKind.CctorForField));
		}

		public void RecordDeclaringTypeOfType (TypeDefinition type, TypeDefinition parent) {
			graph.AddEdge (new Edge<NodeInfo, DependencyKind> (GetOrCreateNode (type), GetOrCreateNode (parent), DependencyKind.DeclaringTypeOfType));
		}
	}
}
