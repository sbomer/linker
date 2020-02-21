using System;

namespace Mono.Linker
{
	// TODO: consider merging these with the edge kinds.
	public enum MarkReasonKind {
		FieldCctor, // field -> cctor
		FieldAccess, // method -> field
		DeclaringTypeOfMethod, // method -> type
		DeclaringTypeOfType, // type -> type
		DirectCall, // method -> method
		VirtualCall, // method -> method
		TypeCctor, // type -> method
		Untracked, // () -> *
		EntryMethod, // () -> method
		EntryType, // () -> type
		EntryField, // () -> field
		NestedType, // type -> type
		FieldOfType, // type -> type
		UserDependencyType, // customattribute -> type

	}

	public struct MarkReason {
		public MarkReasonKind kind;
		public object source;
	}
}