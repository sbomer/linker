using System;

namespace Mono.Linker
{
	// TODO: consider merging these with the edge kinds.
	// TODO: consider factoring out the "intermediate" reasons that are only to be used from
	// MarkStep, and those that are exposed as a potenial runtime dependency.
	// TODO: eventually, only "runtime-like" dependencies (method -> method) and entry methods
	// should be considered.

	// TODO: use low bit or something to denote reasons that are final?
	public enum DependencyKind {
		DirectCall, // method -> method
		VirtualCall, // method -> method
		TriggersCctorThroughFieldAccess, // method -> cctor // this method may trigger the cctor of the specified type.
		TriggersCctorForCalledMethod, // method -> cctor
		AccessesMethodViaReflection, // method -> method, assume it is called from the same method that accesses it.

		OverrideOnInstantiatedType, // type -> method
		EntryAssembly, // assembly -> type, internal. used for 
		// propagating an assembly action that results in keeping all types
		// to the point where we mark the type as an entry type
		// giving the assembly as a reason.
		ConstructedType, // cctor method -> type


		Override, // method -> method. we are marking the target method as an override of another
			// only used when override removal is disabled.

		// not really a mark reason...
		ContainsDangerousCallsite, // method -> callsite. a callsite is a tuple: (caller, callee, instructionindex)

		// marked method on an instantiated type.
		// at this point, we should record it.
		// however, it's not a direct part of the callgraph.

		DeclaringTypeOfCalledMethod, // method -> type
		UnanalyzedReflectionCall,
		FieldAccess, // method -> field
		DeclaringTypeOfMethod, // method -> type
		DeclaringTypeOfType, // type -> type
		Untracked, // () -> *
		EntryMethod, // () -> method
		EntryType, // () -> type
		EntryField, // () -> field
		NestedType, // type -> type. used for marking entire types.
		UserDependencyType, // customattribute -> type
	}

	public struct DependencyInfo {
		public DependencyKind kind;
		public object source;
		public object source2;
		public int instructionIndex;
	}
}
