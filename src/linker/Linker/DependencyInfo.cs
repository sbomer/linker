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
		MethodAccessedViaReflection, // method -> method, assume it is called from the same method that accesses it.
		// also used for property getters/setters whose properties are accessed via reflection
		FieldAccessedViaReflection,

		OverrideOnInstantiatedType, // type -> method
		EntryAssembly, // assembly -> type, internal. used for 
		// propagating an assembly action that results in keeping all types
		// to the point where we mark the type as an entry type
		// giving the assembly as a reason.
		ConstructedType, // cctor method -> type

		// why do we instantiate types?
		InstantiatedValueType, // we mark the type instantiated because it is a value type (no source)
		InstantiatedFullyPreservedType, // we mark fully preserved types as instantiated (no source)
		AlwaysInstantiatedType, // we always mark certain types as instantiated (no source)
		MethodForInstantiatedType,
		GenericArgumentType, // might be used for generic arg of types or methods.
		Ldvirtftn, // method is referenced in an instruction that does ldvirtftn
		Ldftn,
		IsInst,
		NewArr,
		Ldtoken, // type or field or method
		CatchType,

		CustomAttributeArgumentType, // this depends on either a CustomAttribute or a SecurityAttribute. both implement ICustomAttribute, so we use the same helper.
		CustomAttributeArgumentValue, // same as above

		EventAccessedViaReflection,


		// some dependency kinds that we don't necessarily want to report.
		SerializationMethodForType, // type -> method
			// used to track:
			// type with [Serializable], keeps default ctor and special serialization ctor implied by ISerializable (without checking for ISerializable)
			// MarkType, any methods with On(De)Serializ(ed/ing)Attribute are kept.
		MethodReferencedByAttribute, // customattribute -> method
			// currently used for DebuggerDisplayAttribute.
			// also for EventDataAttribute (which keeps public instance property methods)
			// also for TypeConverterAttribute (which results in default ctor, and all methods taking one Type argument on the specified converter type being kept)
		FieldReferencedByAttribute,
		TypeReferencedByAttribute, // debuggertypeproxy
		MethodKeptForNonUnderstoodAttribute, // used for DebuggerDIsplayAttribute when the linker isn't able to parse the string to determine which methods are referenced.
		InterfaceImplementation,
			// a type may have an interfaceimpl of another (interface) type.
		GenericParameterConstraintType, // when a type (or method?) generic parameter has a base type constraint,
		// this is the "reason" that the base type of the constraint gets marked.
		// from the generic type -> constraint type. no separate node for the parameter.
		DefaultCtorForNewConstrainedGenericArgument, // when marking default ctors for arg types that are generic params
		BaseDefaultCtorForStubbedMethod,
		ParameterType, // of method
		ParameterAttribute, // attribute on parameter of a method
		ReturnTypeAttribute, // attribute on return type of a method
		ReturnType, // of method
		VariableType, // of method
		ScopeOfType, // scope of type

		MethodPreservedForType, // type -> method
		FieldPreservedForType, // type -> field
			// for marking fields that come from TypePreserve.Fields
		
		FieldForType, // type -> field.
			// used for marking fields of valuetypes, or explicit layout classes, also static fields for enums
			// also for MarkEntireType (which may be entry, user dependency, etc)
		MethodForType,
			// used for MarkEntireType (which keeps all methods on the type.)
		CctorForField, // field -> method.
			// marks cctor kept for a field in general, for fields that don't have a more explicit reason to be kept.
		MethodForSpecialType, // method is kept for type-specific logic
			// useds to mark all methods of MulticastDelegate.
		BaseType, // type -> base type
		BaseMethod, // method -> method
		CctorForType, // marks cctor of a type, when reason isn't more explicitly specified.
		InteropMethodDependency, // marks default ctor of return type of an interop method
			// also fields of parameter types
			// fields of declaring type
			// return type fields
		MethodImplOverride,
		// EventMethod,
			// an EventDefinition is marked when a MethodDefinition that is an event method is marked.
			// an event method was marked for something.
			// because events aren't tracked, this rule takes the following forms:
			// (there is no "event" node - instead it is cut out and all edges go straight to the event methods)
			// event method <- event method that was marked
		// this one, we reuse methodaccessedviareflection.
			// event method <- method that reflected over an event
			// event method <- type with event methods

			// so EventMethod can come from another event method, or a type.
		VirtualNeededDueToPreservedScope,


		Override, // method -> method. we are marking the target method as an override of another
			// only used when override removal is disabled.
		UnreachableBodyRequirement, // the types that must be kept for unreachable bodies opt
			// used for methods, etc. also for types
		AttributeConstructor,
		AttributeProperty,
		PreserveDependency,


		PropertyAccessedViaReflection, // modified by CustomAttribute
		PropertyOfType, // modified by CustomAttribute
		PropertyOfPropertyMethod, // modified by CustomAttribute
		EventOfType,
		EventOfEventMethod,
		EventMethod, // marking a method because its event was marked

		ElementType, // dependency from generic instance type -> generic typedef
		ElementMethod, // dependency from generic instance method-> generic methoddef
		FieldOnGenericInstance, // dependency from fieldref on generic instance -> field on generic typedef
		ModifierType, // dependency from volatile string -> system.volatile
		// used to blame the modifier type on whatever marked the modreq(Foo) typeref.
		//MethodParameter, // used to blame CAs on parameters on whatever marked the method
		//MethodReturnType,


		CustomAttribute, // ICustomAttribute on a ICustomAttributeProvider
		AssemblyOrModuleCustomAttribute, // the "source" isn't in the graph! special case.
		GenericParameterCustomAttribute, // custom attribute on a generic parameter
		GenericParameterConstraintCustomAttribute, // what it sounds like


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
		DeclaringTypeOfField, // field -> type
		Untracked, // () -> *
		FieldType,
		EntryMethod, // () -> method
		EntryType, // () -> type
		EntryField, // () -> field
		NestedType, // type -> type. used for marking entire types.
		UserDependencyType, // customattribute -> type
	}

	public struct DependencyInfo {
		public DependencyKind kind;
		public object source;
		public int instructionIndex;
	}
}
