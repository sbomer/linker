using Mono.Cecil;
using System.Collections.Generic;
using System.Linq;

namespace Mono.Linker.Analysis
{

	public class CallGraph : ICallGraph<MethodDefinition>
	{

		// interface implementation
		public ICollection<MethodDefinition> Methods => methods;

		public ICollection<(MethodDefinition, MethodDefinition)> Calls => edges;

		public bool IsConstructorDependency(MethodDefinition caller, MethodDefinition callee) {
			return constructorDependencies.Contains((caller, callee));
		}

		// TODO: track virtual calls properly.
		// for now, they're approximately handled in the Analyzer
		public ICollection<(MethodDefinition, MethodDefinition)> Overrides => null;

		public bool IsEntry (MethodDefinition m)
		{
			return apiFilter.IsEntryMethod (m);
		}

		public bool IsInteresting (MethodDefinition m)
		{
			return apiFilter.IsInterestingMethod (m);
		}

		public bool IsVirtual (MethodDefinition m1, MethodDefinition m2) {
			// TODO: make this more accurate.
			// it should track whether this is a virtual call.
			return m2.IsVirtual;
		}

		public ApiFilter apiFilter;
		// currently only used when taking edges in as strings

		HashSet<MethodDefinition> methods;
		HashSet<(MethodDefinition, MethodDefinition)> edges;
		public HashSet<(MethodDefinition, MethodDefinition)> constructorDependencies;
		readonly HashSet<(MethodDefinition, MethodDefinition)> directCalls;
		readonly HashSet<(MethodDefinition, MethodDefinition)> virtualCallsToNonVirtualMethods;
		readonly HashSet<(MethodDefinition, MethodDefinition)> virtualCallsToVirtualMethods;

		HashSet<MethodDefinition> virtualCallees;
		
		public CallGraph (
			HashSet<(MethodDefinition, MethodDefinition)> directCalls,
			HashSet<(MethodDefinition, MethodDefinition)> virtualCalls,
			HashSet<(MethodDefinition, MethodDefinition)> overrides,
			ApiFilter apiFilter)
		{
			this.apiFilter = apiFilter;
			this.directCalls = directCalls;
			this.virtualCallsToVirtualMethods = new HashSet<(MethodDefinition, MethodDefinition)>();
			this.virtualCallsToNonVirtualMethods = new HashSet<(MethodDefinition, MethodDefinition)>();
			this.constructorDependencies = new HashSet<(MethodDefinition, MethodDefinition)>();

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

			edges = new HashSet<(MethodDefinition, MethodDefinition)> (directCalls); // don't even include virtuals now.
			edges.UnionWith(this.virtualCallsToNonVirtualMethods); // these are pretty much direct calls,
			// where the C# compiler uses callvirt just for null-checking purposes.
			edges.UnionWith(this.virtualCallsToVirtualMethods);
			// TODO: what if called both directly and virtually? don't want to subtract those out.

			methods = new HashSet<MethodDefinition> ();
			foreach (var e in edges) {
				var (from, to) = e;
				methods.Add (from);
				methods.Add (to);
			}
		}

		public void RemoveVirtualCalls ()
		{
			// TODO: don't subtract out direct calls!
			// this keeps virtual calls to non-virtual methods.
			// later we might also want to keep around unambiguous virtual calls.
			edges.ExceptWith(virtualCallsToVirtualMethods);
		}

		public void RemoveCalls (Dictionary<MethodDefinition, HashSet<MethodDefinition>> calleesToCallers) {
			foreach (var (callee, callers) in calleesToCallers) {
				foreach (var caller in callers) {
					edges.Remove((caller, callee));
				}
			}
		}

		public void RemoveMethods (List<MethodDefinition> methods) {
			// also remove all calls to/from this method.
			edges.RemoveWhere(call =>
				methods.Contains(call.Item1) || methods.Contains(call.Item2));
			this.methods.RemoveWhere(m => methods.Contains(m));
		}

		// This adds an edge from constructor to unsafe instance methods that are
		// called virtually
		public void AddConstructorEdges() {
			if (constructorDependencies.Count != 0) {
				throw new System.InvalidOperationException("constructor edges may only be added once!");
			}

			foreach (var method in methods) {
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
					if (methods.Contains(ctor)) {
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

			edges.UnionWith(constructorDependencies);
		}
	}
}
