using System;

namespace Mono.Linker
{
	public enum DependencyKind {

	// entry points to the analysis
		RootAssembly, // assembly -> type
		XmlDescriptor, // xml document -> member
		AssemblyAction, // assembly action -> assembly

	// containment relationships
		// members of types
		NestedType,
		MethodOfType,
		PropertyOfType,
		EventOfType,
		FieldOfType,
		// declaring types of members
		DeclaringTypeOfMethod,
		DeclaringTypeOfType,
		DeclaringTypeOfField,

	// type relationships
		BaseType,
		BaseMethod,
		ScopeOfType,

	// types of members, signatures, etc
		FieldType,

	// that indirectly cause other members to be marked
		ParameterType,
		ReturnType,
		PropertyOfPropertyMethod, // dependency from a property method to its property
		EventOfEventMethod, // dependency from an event method to its event
		EventMethod, // dependency from an event to its event method
		// PropertyMethod doesn't exist because marking a property doesn't necessarily mark property methods

	// generics and type modifiers
		GenericArgumentType,
		GenericParameterConstraintType,
		DefaultCtorForNewConstrainedGenericArgument,
		ElementType, // dependency from generic instance type -> generic typedef
		ElementMethod, // dependency from generic instance method-> generic methoddef
		FieldOnGenericInstance, // dependency from fieldref on instantiated generic -> field on generic typedef
		MethodOnGenericInstance, // dependency from methodref on instantiated generic -> method on generic typedef
		ModifierType, // dependency from modified type -> type modifier

	// dependencies for instructions
		DirectCall,
		VirtualCall,
		Ldvirtftn,
		Ldftn,
		IsInst,
		NewArr,
		Ldtoken,
		OtherInstruction, // catch-all for any other instructions

	// other dependencies created by a method body
		VariableType,
		CatchType,
		FieldAccess,

	// tracking instantiations
		InstanceCtor, // marking a ctor causes the declaring type to be marked as instantiated
		// interfaces, value types, fully-preserved, and a few special types are always considered instantiated
		InstantiatedInterface,
		InstantiatedValueType,
		InstantiatedFullyPreservedType,
		AlwaysInstantiatedType,

	// overrides
		OverrideOnInstantiatedType, // override kept for an instantiated type
		Override, // a method kept because it is an override of a kept method
		MethodImplOverride, // an override is kept for the method to which it is attached
		VirtualNeededDueToPreservedScope, // tracks any methods on a type kept because a base method's scope requires it
		MethodForInstantiatedType, // similar, for types that have been instantiated


	//
	// custom attributes
	//

	// custom attributes processed for various attribute providers
		CustomAttribute, // general case of an attribute on a member
		// special cases where the attribute provider isn't recorded as a dependency
		AssemblyOrModuleCustomAttribute,
		ParameterAttribute,
		ReturnTypeAttribute,
		GenericParameterCustomAttribute,
		GenericParameterConstraintCustomAttribute,

	// various dependencies of custom attributes
		// attribute members
		AttributeConstructor, // keeping an attribute keeps its ctor
		AttributeType, // for security attributes, where we mark the type directly
		AttributeProperty, // for security attributes, we mark properties directly
		// TODO: could we handle security attributes more consistent with custom attributes?
		// custom attribute parameters
		CustomAttributeArgumentType,
		CustomAttributeArgumentValue,
		CustomAttributeField,
		// dependencies kept for certain special attributes
		// (XmlSchemaProvider, DebuggerDisplay, DebuggerTypeProxy, SoapHeader, TypeDescriptionProvider)
		TypeReferencedByAttribute,
		MethodReferencedByAttribute,
		FieldReferencedByAttribute,
		MethodKeptForNonUnderstoodAttribute,

	//
	// linker directives and reflection
	//

	// members kept for a PreserveDependencyAttribute.
		PreserveDependencyType,
		PreserveDependencyField,
		PreserveDependencyMethod,

	// members kept for detected reflection patterns, from the method containing the pattern.
		TypeAccessedViaReflection,
		MethodAccessedViaReflection,
		FieldAccessedViaReflection,
		PropertyAccessedViaReflection,
		EventAccessedViaReflection,
	
	// special preservation behavior other inputs (command-line assembly actions, xml, other steps, etc.)
		TypeInAssembly, // type kept when preserving an entire assembly
		PreservedMethod, // explicitly preserved methods (for example set by another step) kept for another method or type
		// members kept due to TypePreserve
		MethodPreservedForType,
		FieldPreservedForType,



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
		public DependencyInfo (DependencyKind kind, object source) => (Kind, Source) = (kind, source);
		public DependencyInfo (DependencyKind kind) => (Kind, Source) = (kind, default);

		public bool Equals (DependencyInfo info) => (Kind, Source) == (info.Kind, info.Source);
		public override bool Equals (Object o) => o is DependencyInfo info && this.Equals (info);
		public override int GetHashCode() => (Kind, Source).GetHashCode ();
		public static bool operator == (DependencyInfo lhs, DependencyInfo rhs) => lhs.Equals (rhs);
		public static bool operator != (DependencyInfo lhs, DependencyInfo rhs) => !lhs.Equals (rhs);
	}
}
