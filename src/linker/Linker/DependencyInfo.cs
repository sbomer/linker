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
		// MarkEntireType:
		// these are used when marking members of an entire type.
		// maybe collapse them into one?
		// they indicate that the entire type is to be marked.
		// type -> member
			NestedType, // type -> type. used for marking entire types.
			MethodOfType, // type -> method // used for MarkEntireType (which keeps all methods on the type.)
			PropertyOfType, // type -> method // modified by CustomAttribute
			EventOfType, // type -> method

		// PreserveDependencyAttribute
		// maybe collapse these into one.
			// note that without KeepDependencyAttributes, the attributes themselves don't get marked,
			// but we include them in the dependency graph anyway.
			PreserveDependencyType, // customattribute -> type
			PreserveDependencyField, // customattribute -> field
			PreserveDependencyMethod, // customattribute -> method // slight problem: the attribute might never be marked.

		// reflection detection
			EventAccessedViaReflection,
			FieldAccessedViaReflection,
			PropertyAccessedViaReflection, // modified by CustomAttribute
			MethodAccessedViaReflection, // method -> method, assume it is called from the same method that accesses it.
				// also used for property getters/setters whose properties are accessed via reflection
			TypeAccessedViaReflection,

		// instructions
			DirectCall, // method -> method
			VirtualCall, // method -> method	
			Ldvirtftn, // method is referenced in an instruction that does ldvirtftn
			Ldftn,
			IsInst,
			NewArr,
			Ldtoken, // type or field or method
			OtherInstruction, // non-understood instructions?
		// other method dependencies
			CatchType,
			VariableType, // of method
			FieldAccess, // method -> field


		// tracking instantiated types
		// the special case is ConstructedType,
			InstanceCtor, // cctor method -> type
			InstantiatedInterface,
			InstantiatedValueType, // we mark the type instantiated because it is a value type (no source)
			InstantiatedFullyPreservedType, // we mark fully preserved types as instantiated (no source)
			AlwaysInstantiatedType, // we always mark certain types as instantiated (no source)

		// custom attributes. they may not be marked, but are still recorded logically.
			CustomAttribute, // ICustomAttribute on a ICustomAttributeProvider
			AssemblyOrModuleCustomAttribute, // the "source" isn't in the graph! special case.
			GenericParameterCustomAttribute, // custom attribute on a generic parameter
			GenericParameterConstraintCustomAttribute, // what it sounds like
			ParameterAttribute, // attribute on parameter of a method
			ReturnTypeAttribute, // attribute on return type of a method
			// and custom attribute dependencies
			MethodReferencedByAttribute, // customattribute -> method
			// currently used for DebuggerDisplayAttribute.
			// also for EventDataAttribute (which keeps public instance property methods)
			// also for TypeConverterAttribute (which results in default ctor, and all methods taking one Type argument on the specified converter type being kept)
			FieldReferencedByAttribute,
			// non-understood DebuggerDisplayAttribute, marks all fields
			// SoapHeaderAttribute on a method will keep a field of the declaring type
			TypeReferencedByAttribute, // debuggertypeproxy
			CustomAttributeArgumentType, // this depends on either a CustomAttribute or a SecurityAttribute. both implement ICustomAttribute, so we use the same helper.
			CustomAttributeArgumentValue, // same as above
			CustomAttributeField, // marking named field of a CA
			MethodKeptForNonUnderstoodAttribute, // used for DebuggerDIsplayAttribute when the linker isn't able to parse the string to determine which methods are referenced.
			AttributeConstructor,
			AttributeProperty,
			AttributeType, // used to mark type of a security attribute when the attribute is reached.
				// not used for normal custom attributes, which just mark the attribute ctor (which in turn will mark the type)

		// unmarked things other than custom attributes
			ParameterType, // of method, OR of fnptr
			ReturnType, // of method // OR of function pointer
			PropertyOfPropertyMethod, // modified by CustomAttribute
			EventOfEventMethod,
			// and their dependencies
			EventMethod, // marking a method because its event was marked

		// generics and type modifiers
			// why do we instantiate types?
			GenericArgumentType, // might be used for generic arg of types or methods.
			GenericParameterConstraintType, // when a type (or method?) generic parameter has a base type constraint,
			// this is the "reason" that the base type of the constraint gets marked.
			// from the generic type -> constraint type. no separate node for the parameter.
			DefaultCtorForNewConstrainedGenericArgument, // when marking default ctors for arg types that are generic params
				// because we don't do this through MarkType.
				// this won't cause the type to be instantiated.
			ElementType, // dependency from generic instance type -> generic typedef
			ElementMethod, // dependency from generic instance method-> generic methoddef
			FieldOnGenericInstance, // dependency from fieldref on generic instance -> field on generic typedef
			// when marking a field on a generic instance, blame the original reason that we got to MarkField.
			// then blame the fieldDEF on "fieldrefongenericinstance"
			MethodOnGenericInstance, // similar to above...
			// marking a methodref on generic instance, blame the original reason that we got to MarkMethod.
			// then blame the methodDEF on "methodongenericinstance"
			ModifierType, // dependency from volatile string -> system.volatile
			// used to blame the modifier type on whatever marked the modreq(Foo) typeref.
			//MethodParameter, // used to blame CAs on parameters on whatever marked the method
			//MethodReturnType,
		
		// special preservation behavior other inputs (user, xml, other steps, etc.)
			PreservedMethod, // preserved_methods in Annotations.
			// not used for illink. but for monolinker calendarstep.
			// can be preserved "because" of a type, or because of a method.
			// type -> method, or method -> method
			TypeInAssembly, // currently, only used when MarkEntireAssembly (for copy/save assemblies)
			// XML marks individuals.
			// Assembly sets assembly action, and marks types.
			// we shouldn't try to mark entry types here.
				// marks an entire type. assembly -> type.
				// might rename it to AssemblyAction.
				// means that we are marking an entire type in MarkEntireAssembly.ß
				// only happens for copy.
				// can we mark assemblies for -r?
				// not same code path. it won't "root" the assembly, but only public members inside.
				// which in reality, WILL force the assembly to be kept.
				// maybe not if it's a type forwarder? hmm...
				// only for copy/save assembly being marked in MarkStep.
				// if we set the assembly action, it's set as an entry point.
				// because some actions influence decisions later on. they cause more things to be marked.
				// and therefore they belong in the graph.
			MethodPreservedForType, // type -> method
			FieldPreservedForType, // type -> field
				// for marking fields that come from TypePreserve.Fields

		// simple relationships
			// hierarchy
			BaseType, // type -> base type
			BaseMethod, // method -> method
			// containment (simple member relationships)
			FieldForType, // type -> field.
				// used for marking fields of valuetypes, or explicit layout classes, also static fields for enums
				// also for MarkEntireType (which may be entry, user dependency, etc)
			DeclaringTypeOfMethod, // method -> type
			DeclaringTypeOfType, // type -> type
			DeclaringTypeOfField, // field -> type
			// types of things
			FieldType,


		// special marking for cctor triggering
			TriggersCctorThroughFieldAccess, // method -> cctor // this method may trigger the cctor of the specified type.
			TriggersCctorForCalledMethod, // method -> cctor
			DeclaringTypeOfCalledMethod, // method -> type
			CctorForType, // marks cctor of a type, when reason isn't more explicitly specified.
			CctorForField, // field -> method.
				// marks cctor kept on declaring type for a field in general, for fields that don't have a more explicit reason to be kept.
			
		// interop info
			ReturnTypeMarshalSpec, // method -> method return type's marshal spec.
			// similar to ReturnTypeAttribute.
			ParameterMarshalSpec, // same, for parameters
			FieldMarshalSpec, // field -> its marshalspec.
			InteropMethodDependency, // marks default ctor of return type of an interop method
			// also fields of parameter types
			// fields of declaring type
			// return type fields


		// interface implementations.
			InterfaceImplementationInterfaceType,
			// a type may have an interfaceimpl of another (interface) type. goes from impl -> interface type
			InterfaceImplementationOnType,
			// from type -> impl on it

		// support for special runtime behaviors
			// some dependency kinds that we don't necessarily want to report.
			SerializationMethodForType, // type -> method
				// used to track:
				// type with [Serializable], keeps default ctor and special serialization ctor implied by ISerializable (without checking for ISerializable)
				// MarkType, any methods with On(De)Serializ(ed/ing)Attribute are kept.
			// if a type derives from EventSource, it's an eventsource implementation.
			// we keep all static fields on event source providers that are nested types of the implementation.
			// providers are nested types with name "Keywords"/"Tasks"/"Opcodes"
			// so nested types named "Keywords"/"Tasks"/"Opcodes" within a type derived from EventSource have static fields kept.
			EventSourceProviderField, // from an EventSource derived type to each Keywords/Tasks/Opcodes nested class field
			MethodForSpecialType, // method is kept for type-specific logic
				// useds to mark all methods of MulticastDelegate.


		// instantiations and overrides
			OverrideOnInstantiatedType, // type -> method
			MethodImplOverride,
			VirtualNeededDueToPreservedScope,
			MethodForInstantiatedType,
			Override, // method -> method. we are marking the target method as an override of another
				// only used when override removal is disabled.

		// optimization-specific, and linker internals
			UnreachableBodyRequirement, // the types that must be kept for unreachable bodies opt
			// used for methods, etc. also for types
			LinkerInternal, // some things are not given a particular reason. they are just marked (or the conditions are not easily reported). like DisableReflectionAttribute.
			// used for DisableReflectionAttribute, which gets marked whenever we have any indirectly called methods.
			// TODO: handle case where there's a reason but no source.
			DisablePrivateReflectionDependency, // something needed for the DisablePrivateReflectionAttribute.
			// currently used for default ctor of DisablePrivateReflection attribute, when that is marked.
			// this attribute type gets marked whenever there are any "indirectly called" methods (UserDependency/Reflectionn/XML)
			AlreadyMarkedType, // MarkType for a type that was already marked (InitializeType)
			AlreadyMarkedField,
			AlreadyMarkedMethod,
			BaseDefaultCtorForStubbedMethod,
	}

	readonly public struct DependencyInfo : IEquatable<DependencyInfo> {
		public DependencyKind Kind { get; }
		public object Source { get; }
		public bool Equals (DependencyInfo info) => (Kind, Source) == (info.Kind, info.Source);
		public override bool Equals (Object o) => o is DependencyInfo info && this.Equals (info);
		public override int GetHashCode() => (Kind, Source).GetHashCode ();
		public static bool operator == (DependencyInfo lhs, DependencyInfo rhs) => lhs.Equals (rhs);
		public static bool operator != (DependencyInfo lhs, DependencyInfo rhs) => !lhs.Equals (rhs);
		public DependencyInfo (DependencyKind kind, object source) => (Kind, Source) = (kind, source);
		public DependencyInfo (DependencyKind kind) => (Kind, Source) = (kind, default);
	}
}
