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

	// EdgeInfo should be replaced by MarkedReason.
	public enum EdgeInfo {
		Call, // x
		VirtualCall, // x
		UnanalyzedReflectionCall, // this is the only edgeInfo that doesn't have a corresponding reason.
		CctorForField, // x
		CctorForType, // x
		FieldAccess, // x
		DeclaringTypeOfMethod, // x
		DeclaringTypeOfType, // x
		NestedType, // x
		UserDependencyType // x
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
		Dictionary<object, Node<NodeInfo>> raisedNode = new Dictionary<object, Node<NodeInfo>> ();

		private Node<NodeInfo> GetOrCreateNode (object o) {
			if (raisedNode.TryGetValue (o, out Node<NodeInfo> node))
				return node;
			// TODO: add appropriate node kind
			node = new Node<NodeInfo> (value: o);
			raisedNode [o] = node;
			return node;
		}

		private Node<NodeInfo> GetOrCreateEntryNode (object o) {
			var n = GetOrCreateNode (o);
			n.Entry = true;
			raisedNode [o] = n;
			return n;
		}

		private Node<NodeInfo> GetOrCreateDangerousNode (object o) {
			var n = GetOrCreateNode (o);
			n.Dangerous = true;
			raisedNode [o] = n;
			return n;
		}

		private Node<NodeInfo> GetOrCreateUntrackedNode (object o) {
			var n = GetOrCreateNode (o);
			n.Untracked = true;
			raisedNode [o] = n;
			return n;
		}

		public readonly SearchableDependencyGraph<NodeInfo, EdgeInfo> graph;

		public GraphDependencyRecorder () {
			graph = new SearchableDependencyGraph<NodeInfo, EdgeInfo> ();
		}

		public void RecordDirectCall (MethodDefinition caller, MethodDefinition callee) {
			graph.AddEdge (new Edge<NodeInfo, EdgeInfo> (GetOrCreateNode (caller), GetOrCreateNode (callee), EdgeInfo.Call));
		}
		public void RecordVirtualCall (MethodDefinition caller, MethodDefinition callee) {
			graph.AddEdge (new Edge<NodeInfo, EdgeInfo> (GetOrCreateNode (caller), GetOrCreateNode (callee), EdgeInfo.VirtualCall));
		}
		public void RecordUnanalyzedReflectionCall (MethodDefinition source, MethodDefinition reflectionMethod) {
			graph.AddEdge (new Edge<NodeInfo, EdgeInfo> (GetOrCreateNode (source), GetOrCreateNode (reflectionMethod), EdgeInfo.UnanalyzedReflectionCall));
		}
		public void RecordAnalyzedReflectionCall (MethodDefinition source, MethodDefinition reflectionMethod) {
			// SVEN: TODO
		}

		public void RecordEntryType (TypeDefinition type) {
			var node = GetOrCreateEntryNode (type);
			graph.AddNode (node);
		}
		public void RecordEntryField (FieldDefinition field) {
			var node = GetOrCreateEntryNode (field);
			graph.AddNode (node);
		}
		public void RecordEntryMethod (MethodDefinition method) {
			var node = GetOrCreateEntryNode (method);
			graph.AddNode (node);
		}

		public void RecordDangerousMethod (MethodDefinition method) {
			var node = GetOrCreateDangerousNode (method);
			graph.AddNode (node);
		}

		public void RecordNestedType (TypeDefinition declaringType, TypeDefinition nestedType) {
			graph.AddEdge (new Edge<NodeInfo, EdgeInfo> (GetOrCreateNode (declaringType), GetOrCreateNode (nestedType), EdgeInfo.NestedType));
		}

		public void RecordUserDependencyType (CustomAttribute ca, TypeDefinition type) {
			graph.AddEdge (new Edge<NodeInfo, EdgeInfo> (GetOrCreateNode (ca), GetOrCreateNode (type), EdgeInfo.UserDependencyType));
		}

		public void RecordTypeStaticConstructor (TypeDefinition type, MethodDefinition cctor) {
			graph.AddEdge (new Edge<NodeInfo, EdgeInfo> (GetOrCreateNode (type), GetOrCreateNode (cctor), EdgeInfo.CctorForType));
		}

		public void RecordFieldAccessFromMethod (MethodDefinition method, FieldDefinition field) {
			graph.AddEdge (new Edge<NodeInfo, EdgeInfo> (GetOrCreateNode (method), GetOrCreateNode (field), EdgeInfo.FieldAccess));
		}

		public void RecordStaticConstructorForField (FieldDefinition field, MethodDefinition cctor) {
			graph.AddEdge (new Edge<NodeInfo, EdgeInfo> (GetOrCreateNode (field), GetOrCreateNode (cctor), EdgeInfo.CctorForField));
		}

		public void RecordDeclaringTypeOfMethod (MethodDefinition method, TypeDefinition type) {
			graph.AddEdge (new Edge<NodeInfo, EdgeInfo> (GetOrCreateNode (method), GetOrCreateNode (type), EdgeInfo.DeclaringTypeOfMethod));
		}
		public void RecordDeclaringTypeOfType (TypeDefinition type, TypeDefinition parent) {
			graph.AddEdge (new Edge<NodeInfo, EdgeInfo> (GetOrCreateNode (type), GetOrCreateNode (parent), EdgeInfo.DeclaringTypeOfType));
		}


		public void RecordFieldUntracked (FieldDefinition field) {
			// TODO: maybe make this more performant?
			var node = GetOrCreateUntrackedNode (field);
			if (node.Untracked)
				return;
//			graph.AddNode (node);
		}
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
