using Mono.Cecil;
using System.Collections.Generic;
using System.Linq;

namespace Mono.Linker.Analysis
{

	public class CallGraph : ICallGraph<MethodDefinition>
	{

		// interface implementation
		public ICollection<MethodDefinition> Methods => methods;

		public ICollection<(MethodDefinition, MethodDefinition)> Calls => callsOrDependencies;

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
		HashSet<(MethodDefinition, MethodDefinition)> calls;
		HashSet<(MethodDefinition, MethodDefinition)> constructorDependencies;
		HashSet<(MethodDefinition, MethodDefinition)> callsOrDependencies;

		public CallGraph (
			List<(MethodDefinition, MethodDefinition)> edges,
			ApiFilter apiFilter)
		{
			this.apiFilter = apiFilter;
			Initialize (edges);
		}

		public void Initialize (List<(MethodDefinition, MethodDefinition)> edges)
		{
			calls = edges.ToHashSet (); ;
			methods = new HashSet<MethodDefinition> ();
			foreach (var e in edges) {
				var (from, to) = e;
				methods.Add (from);
				methods.Add (to);
			}
		}

		HashSet<MethodDefinition> virtualCallees;
		HashSet<(MethodDefinition, MethodDefinition)> virtualCalls;
		public void RemoveVirtualCalls ()
		{
			virtualCalls = calls.Where(c => IsVirtual(c.Item1, c.Item2)).ToHashSet();
			virtualCallees = virtualCalls.Select(c => c.Item2).ToHashSet();
			calls.ExceptWith(virtualCalls);
		}

		public void RemoveCalls (Dictionary<MethodDefinition, HashSet<MethodDefinition>> calleesToCallers) {
			foreach (var (callee, callers) in calleesToCallers) {
				foreach (var caller in callers) {
					calls.Remove((caller, callee));
				}
			}
		}

		public void RemoveMethods (List<MethodDefinition> methods) {
			// also remove all calls to/from this method.
			calls.RemoveWhere(call =>
				methods.Contains(call.Item1) || methods.Contains(call.Item2));
			constructorDependencies.RemoveWhere(call =>
				methods.Contains(call.Item1) || methods.Contains(call.Item2));
			this.methods.RemoveWhere(m => methods.Contains(m));
			callsOrDependencies = new HashSet<(MethodDefinition, MethodDefinition)>(calls);
			callsOrDependencies.UnionWith(constructorDependencies);
		}

		// This adds an edge from constructor to unsafe instance methods that are
		// called virtually and only virtually.
		public void AddConstructorEdges() {
			if (constructorDependencies != null) {
				throw new System.InvalidOperationException("constructor edges may only be added once!");
			}
			constructorDependencies = new HashSet<(MethodDefinition, MethodDefinition)>();
			foreach (var method in methods) {
				if (!IsInteresting(method))
					continue;

				// if the method is called directly, don't add a constructor dependency
				// since we want to report the direct call instead.
				if (calls.Where(c => c.Item2 == method).Any()) {
					continue;
				}

				// if it is never called virtually, don't add any edges.
				if (!virtualCallees.Contains(method)) {
					// we only get here if the method was never called.
					// this doesn't happen for a console app, but may happen for public methods
					// in netcoreapp.
					// TODO: check that this behaves as expected on netcoreapp
					continue;
				}

				var ctors = method.DeclaringType.Methods.Where (m => m.IsConstructor);
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
					var virtualCallers = virtualCalls.Where(c => c.Item2 == method).Select(c => c.Item1);
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
			callsOrDependencies = new HashSet<(MethodDefinition, MethodDefinition)>(calls);
			callsOrDependencies.UnionWith(constructorDependencies);

		}
	}
}
