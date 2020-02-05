using Mono.Cecil;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Mono.Linker.Analysis
{

	public class CallGraph : ICallGraph<IMemberDefinition>
	{
		// interface implementation
		public ICollection<IMemberDefinition> Nodes => (ICollection<IMemberDefinition>)nodes;
		public ICollection<MethodDefinition> Methods => methods;
		public ICollection<TypeDefinition> Types => types;

		public ICollection<(IMemberDefinition, IMemberDefinition)> Edges => 
			(ICollection<(IMemberDefinition, IMemberDefinition)>)edges.Select(e => ((IMemberDefinition)e.Item1, (IMemberDefinition)e.Item2));

		public bool IsConstructorDependency(MethodDefinition caller, MethodDefinition callee) {
			return constructorDependencies.Contains((caller, callee));
		}

		// TODO: track virtual calls properly.
		// for now, they're approximately handled in the Analyzer
		public ICollection<(IMemberDefinition, IMemberDefinition)> Overrides => null;

		public bool IsEntry (IMemberDefinition m)
		{
			if (!(m is MethodDefinition method)) {
				return false;
			}
			if (entryMethods.Contains (m)) {
				return true;
			}
			return apiFilter.IsEntryMethod (method);
		}

		public bool IsInteresting (IMemberDefinition m)
		{
			if (!(m is MethodDefinition method)) {
				return false;
			}
			return apiFilter.IsInterestingMethod (method);
		}

		public bool IsVirtual (MethodDefinition m1, MethodDefinition m2) {
			// TODO: make this more accurate.
			// it should track whether this is a virtual call.
			return m2.IsVirtual;
		}

		public ApiFilter apiFilter;
		// currently only used when taking edges in as strings

		HashSet<IMemberDefinition> nodes;
		HashSet<(IMemberDefinition, IMemberDefinition)> edges;
		public HashSet<(MethodDefinition, MethodDefinition)> constructorDependencies;
		readonly HashSet<(MethodDefinition, MethodDefinition)> directCalls;
		readonly HashSet<(MethodDefinition, MethodDefinition)> virtualCallsToNonVirtualMethods;
		readonly HashSet<(MethodDefinition, MethodDefinition)> virtualCallsToVirtualMethods;

		public readonly HashSet<(TypeDefinition, MethodDefinition)> cctorDependencies;
		public readonly HashSet<(MethodDefinition, MethodDefinition)> cctorFieldAccessDependencies;

		public readonly HashSet<(MethodDefinition, TypeDefinition)> typeDependencies;

		HashSet<MethodDefinition> methods;
		HashSet<TypeDefinition> types;

		// these are only the virtual methods.
		HashSet<MethodDefinition> virtualCallees;
		HashSet<MethodDefinition> entryMethods;
		
        public void Print(IntMapping<IMemberDefinition> mapping) {
			var orderedNodes = nodes.OrderBy<IMemberDefinition, int>(n => mapping.methodToInt [n]);
			foreach (var n in orderedNodes) {
				Console.WriteLine("node(" + mapping.methodToInt [n] + "): " + n);
				var meth = n as MethodDefinition;
				if (meth != null) {
					if (meth.IsVirtual) {
						Console.WriteLine("virtual");
					}
				}
			}

            var orderedDirectCalls = directCalls.OrderBy<(MethodDefinition, MethodDefinition), string>(c => c.Item1.ToString(), StringComparer.InvariantCulture)
                .ThenBy(c => c.Item2.ToString(), StringComparer.InvariantCulture);
	        foreach (var c in orderedDirectCalls) {
	            Console.WriteLine("direct: " + c.Item1 + " -> " + c.Item2);
		    }
            var orderedVirtualCalls = virtualCallsToVirtualMethods.OrderBy<(MethodDefinition, MethodDefinition), string>(c => c.Item1.ToString(), StringComparer.InvariantCulture)
            .ThenBy(c => c.Item2.ToString(), StringComparer.InvariantCulture);
	        foreach (var c in orderedVirtualCalls) {
                Console.WriteLine("virtual: " + c.Item1 + " -> " + c.Item2);
    	    }
			var ctorDeps = constructorDependencies.OrderBy<(MethodDefinition, MethodDefinition), string>(c => c.Item1.ToString(), StringComparer.InvariantCulture)
			.ThenBy(c => c.Item2.ToString(), StringComparer.InvariantCulture);
			foreach (var c in ctorDeps) {
				Console.WriteLine("ctordep: " + c.Item1 + " -> " + c.Item2);
			}
			// show all edges.
			var orderedEdges = edges.OrderBy<(IMemberDefinition, IMemberDefinition), string>(c => c.Item1.ToString(), StringComparer.InvariantCulture)
			.ThenBy(c => c.Item2.ToString(), StringComparer.InvariantCulture);
			foreach (var e in orderedEdges) {
				Console.WriteLine("edge: " + e.Item1 + " -> " + e.Item2);
			}

			var orderedEntry = entryMethods.OrderBy<MethodDefinition, string>(e => e.ToString(), StringComparer.InvariantCulture);
			foreach (var e in orderedEntry) {
				Console.WriteLine("entry: " + e);
			}
		}
		
		public CallGraph (
			HashSet<(MethodDefinition, MethodDefinition)> directCalls,
			HashSet<(MethodDefinition, MethodDefinition)> virtualCalls,
			HashSet<(MethodDefinition, MethodDefinition)> overrides,
			HashSet<(TypeDefinition, MethodDefinition)> cctorDependencies,
			HashSet<(MethodDefinition, MethodDefinition)> cctorFieldAccessDependencies,
			HashSet<(MethodDefinition, TypeDefinition)> typeDependencies,
			HashSet<MethodDefinition> entryMethods,
			ApiFilter apiFilter)
		{
			this.apiFilter = apiFilter;
			this.directCalls = directCalls;
			this.virtualCallsToVirtualMethods = new HashSet<(MethodDefinition, MethodDefinition)>();
			this.virtualCallsToNonVirtualMethods = new HashSet<(MethodDefinition, MethodDefinition)>();
			this.constructorDependencies = new HashSet<(MethodDefinition, MethodDefinition)>();
			this.cctorDependencies = cctorDependencies;
			this.cctorFieldAccessDependencies = cctorFieldAccessDependencies;
			this.typeDependencies = typeDependencies;
			this.entryMethods = entryMethods;

			// each virtual call should have at least one target.
			foreach (var c in virtualCalls) {
				var caller = c.Item1;
				var callee = c.Item2;

				if (!callee.IsVirtual) {
					// C# compiler emits callvirt to non-virtual methods. we are not interested in these.
					virtualCallsToNonVirtualMethods.Add((caller, callee));
					continue;
				}
				if (!callee.DeclaringType.IsInterface) {
					// virtual calls to an interface method can only go to overrides. (not taking into account default interface implementations yet)
					this.virtualCallsToVirtualMethods.Add((caller, callee));
				}
				foreach (var target in overrides.Where(o => o.Item1 == callee).Select(o => o.Item2)) {
					this.virtualCallsToVirtualMethods.Add((caller, target));
				}
			}

			virtualCallees = this.virtualCallsToVirtualMethods.Select(c => c.Item2).ToHashSet();

			edges = new HashSet<(IMemberDefinition, IMemberDefinition)> ();
			edges.UnionWith(directCalls.Select(c => ((IMemberDefinition)c.Item1, (IMemberDefinition)c.Item2))); // don't even include virtuals now.
			edges.UnionWith(this.virtualCallsToNonVirtualMethods.Select(c => ((IMemberDefinition)c.Item1, (IMemberDefinition)c.Item2))); // these are pretty much direct calls,
			// where the C# compiler uses callvirt just for null-checking purposes.
			edges.UnionWith(this.virtualCallsToVirtualMethods.Select(c => ((IMemberDefinition)c.Item1, (IMemberDefinition)c.Item2)));
			// TODO: what if called both directly and virtually? don't want to subtract those out.
			// also consider a static field access to a beforefieldinit cctor as a dependency from the method to the cctor.
			edges.UnionWith(this.cctorFieldAccessDependencies.Select(c => ((IMemberDefinition)c.Item1, (IMemberDefinition)c.Item2)));
			edges.UnionWith(this.cctorDependencies.Select(c => ((IMemberDefinition)c.Item1, (IMemberDefinition)c.Item2)));
			edges.UnionWith(this.typeDependencies.Select(c => ((IMemberDefinition)c.Item1, (IMemberDefinition)c.Item2)));

			nodes = new HashSet<IMemberDefinition> ();
			methods = new HashSet<MethodDefinition> ();
			types = new HashSet<TypeDefinition> ();
			foreach (var e in edges) {
				var (from, to) = e;
				AddNode (from);
				AddNode (to);
			}
		}

		private void AddNode(IMemberDefinition node) {
			nodes.Add (node);
			switch (node) {
			case MethodDefinition method:
				methods.Add (method);
				break;
			case TypeDefinition type:
				types.Add (type);
				break;
			}
		}

		public void RemoveVirtualCalls ()
		{
			// TODO: don't subtract out direct calls!
			// this keeps virtual calls to non-virtual methods.
			// later we might also want to keep around unambiguous virtual calls.
			edges.ExceptWith(virtualCallsToVirtualMethods.Select(c => ((IMemberDefinition)c.Item1, (IMemberDefinition)c.Item2)));
		}

		// this does not remove them from directCalls or virtualCalls, only the result.
		public void RemoveCalls (Dictionary<MethodDefinition, HashSet<MethodDefinition>> calleesToCallers) {
			foreach (var (callee, callers) in calleesToCallers) {
				foreach (var caller in callers) {
					edges.Remove((caller, callee));
				}
			}
		}

		public void RemoveMethods (List<IMemberDefinition> methods) {
			// also remove all calls to/from this method.
			edges.RemoveWhere(call =>
				methods.Contains(call.Item1) || methods.Contains(call.Item2));
			this.nodes.RemoveWhere(m => methods.Contains(m));
		}

		// This adds an edge from constructor to unsafe instance methods that are
		// called virtually
		public void AddConstructorEdges() {
			if (constructorDependencies.Count != 0) {
				throw new System.InvalidOperationException("constructor edges may only be added once!");
			}

			foreach (var node in nodes) {
				MethodDefinition method = node as MethodDefinition;
				if (method == null) {
					continue;
				}
				// we need to add constructor edges for any methods called virtually (even if called directly as well)
				// in case the direct call is only reachable through virtual calls from an otherwise safe caller.

				// if it is never called virtually, don't add any edges.
				if (!virtualCallees.Contains(method)) {
					// we only get here if the method was never called.
					// this doesn't happen for a console app, but may happen for public methods
					// in netcoreapp.
					// TODO: check that this behaves as expected on netcoreapp
					continue;
				}
				var ctors = method.DeclaringType.Methods.Where (m => m.IsConstructor && !m.IsStatic);
				bool constructorCalled = false;
				foreach (var ctor in ctors) {
					var dependency = (ctor, method);
					if (nodes.Contains(ctor)) {
						constructorCalled = true;
						constructorDependencies.Add(dependency);
					}
				}
				if (!constructorCalled) {
					bool skipError = false;
					if (method.DeclaringType.IsInterface) {
						// TODO: interfaces are never constructed.
						// we should track this as:
						// Main -- virtual call to --> I.Virtual
						// I.Virtual -- overriden by --> A.Virtual
						// OR
						// Main -- virtual call to --> A.Virtual
						// TODO: fix this by tracking virtual calls correctly.
						// for now, just skip over interfaces.
						skipError = true;
					}

					// TODO: we get some that call self...
					// probably inaccurate recording.
					var virtualCallers = virtualCallsToVirtualMethods.Where(c => c.Item2 == method).Select(c => c.Item1);
					if (virtualCallers.Count() == 1) {
						if (virtualCallers.Single() == method) {
							// if the method's only caller is itself...
							skipError = true;
						}
					}

					// the Delegate ctor was never called either. constructed by the runtime?
					if (method.DeclaringType.FullName == "System.Delegate" ||
						method.DeclaringType.FullName == "System.Reflection.Emit.ModuleBuilder") {
						skipError = true;
					}

					// actually, let' just alway skip the error for now. we may get some inaccurate results.
					skipError = true;

					if (!skipError) {
						throw new System.Exception("we saw a virtual call to dangerous " + method + " whose type was never constructed... how?");
					}
				}
			}

			edges.UnionWith(constructorDependencies.Select(c => ((IMemberDefinition)c.Item1, (IMemberDefinition)c.Item2)));
		}
	}
}
