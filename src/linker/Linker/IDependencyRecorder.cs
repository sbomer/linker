﻿//
// IDependencyRecorder.cs
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

namespace Mono.Linker
{
	/// <summary>
	/// Abstraction exposed by the linker (mostly MarkStep, but not only) - it will call this interface
	/// every time it finds a dependency between two parts of the dependency graph.
	/// </summary>
	public interface IDependencyRecorder
	{
		/// <summary>
		/// Reports a dependency detected by the linker.
		/// </summary>
		/// <param name="source">The source of the dependency (for example the caller method).</param>
		/// <param name="target">The target of the dependency (for example the callee method).</param>
		/// <param name="marked">true if the target is also marked by the MarkStep.</param>
		/// <remarks>The source and target are typically Cecil metadata objects (MethodDefinition, TypeDefinition, ...)
		/// but they can also be the linker steps or really any other object.</remarks>
		void RecordDependency (object source, object target, bool marked);
	}


	// public interface ISimpleRuleDependencyRecorder
	// {
	// 	void RecordDependency (MarkReasonKind kind, object source, object target) {
	// 		switch (kind) {
	// 		case MarkReasonKind.EntryType:
// 
	// 		}
	// 	}
	// }

	public interface IRuleDependencyRecorder
	{
		void RecordMethodWithReason (DependencyInfo reason, MethodDefinition method);
		void RecordFieldWithReason (DependencyInfo reason, FieldDefinition field);
		void RecordTypeWithReason (DependencyInfo reason, TypeDefinition type);
		void RecordTypeSpecWithReason (DependencyInfo reason, TypeSpecification spec);
		void RecordMethodSpecWithReason (DependencyInfo reason, MethodSpecification spec);
		void RecordFieldOnGenericInstance (DependencyInfo reason, FieldReference field);
		void RecordMethodOnGenericInstance (DependencyInfo reason, MethodReference method);
		void RecordDirectCall (MethodDefinition caller, MethodDefinition callee);
		void RecordVirtualCall (MethodDefinition caller, MethodDefinition callee);
		void RecordUnanalyzedReflectionCall (MethodDefinition source, MethodDefinition reflectionMethod, int instructionIndex, ReflectionData data);
		void RecordAnalyzedReflectionAccess (MethodDefinition source, MethodDefinition target);

		// void RecordDangerousMethod (MethodDefinition method);

		void RecordEntryType (TypeDefinition type, EntryInfo info);
		void RecordTypeLinkerInternal (TypeDefinition type);
		void RecordEntryAssembly (AssemblyDefinition assembly, EntryInfo info);

		// void RecordScopeOfType (TypeDefinition type, IMetadataScope scope);
		void RecordEntryField (FieldDefinition field, EntryInfo info);
		void RecordEntryMethod (MethodDefinition method, EntryInfo info);
		void RecordAssemblyCustomAttribute (ICustomAttribute ca, EntryInfo info);


		void RecordInstantiatedByConstructor (MethodDefinition ctor, TypeDefinition type);
		void RecordOverrideOnInstantiatedType (TypeDefinition type, MethodDefinition method);
		void RecordInterfaceImplementation (TypeDefinition type, InterfaceImplementation iface);

		void RecordCustomAttribute (DependencyInfo reason, ICustomAttribute ca);

		void RecordPropertyWithReason (DependencyInfo reason, PropertyDefinition property);
		void RecordEventWithReason (DependencyInfo reason, EventDefinition evt);

		void RecordNestedType (TypeDefinition declaringType, TypeDefinition nestedType);

		void RecordUserDependencyType (CustomAttribute customAttribute, TypeDefinition type);

		void RecordFieldAccessFromMethod (MethodDefinition method, FieldDefinition field);
//		void RecordStaticConstructorForField (FieldDefinition field, MethodDefinition cctor);
		void RecordTriggersStaticConstructorThroughFieldAccess (MethodDefinition method, MethodDefinition cctor);
		void RecordTriggersStaticConstructorForCalledMethod (MethodDefinition method, MethodDefinition cctor);
		void RecordStaticConstructorForField (FieldDefinition field, MethodDefinition cctor);
		void RecordDeclaringTypeOfType (TypeDefinition type, TypeDefinition parent);
		void RecordOverride (MethodDefinition @base, MethodDefinition @override);
	}
}
