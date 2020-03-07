//
// Annotations.cs
//
// Author:
//   Jb Evain (jbevain@novell.com)
//
// (C) 2007 Novell, Inc.
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

using System;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Linq;

namespace Mono.Linker {

	public partial class AnnotationStore {

		protected readonly LinkContext context;

		protected readonly Dictionary<AssemblyDefinition, AssemblyAction> assembly_actions = new Dictionary<AssemblyDefinition, AssemblyAction> ();
		protected readonly Dictionary<MethodDefinition, MethodAction> method_actions = new Dictionary<MethodDefinition, MethodAction> ();
		protected readonly Dictionary<MethodDefinition, object> method_stub_values = new Dictionary<MethodDefinition, object> ();
		protected readonly Dictionary<FieldDefinition, object> field_values = new Dictionary<FieldDefinition, object> ();
		protected readonly HashSet<FieldDefinition> field_init = new HashSet<FieldDefinition> ();
		protected readonly HashSet<TypeDefinition> fieldType_init = new HashSet<TypeDefinition> ();
		protected readonly HashSet<IMetadataTokenProvider> marked = new HashSet<IMetadataTokenProvider> ();
		protected readonly HashSet<IMetadataTokenProvider> processed = new HashSet<IMetadataTokenProvider> ();
		protected readonly Dictionary<TypeDefinition, TypePreserve> preserved_types = new Dictionary<TypeDefinition, TypePreserve> ();
		protected readonly Dictionary<IMemberDefinition, List<MethodDefinition>> preserved_methods = new Dictionary<IMemberDefinition, List<MethodDefinition>> ();
		protected readonly HashSet<IMetadataTokenProvider> public_api = new HashSet<IMetadataTokenProvider> ();
		protected readonly Dictionary<MethodDefinition, List<OverrideInformation>> override_methods = new Dictionary<MethodDefinition, List<OverrideInformation>> ();
		protected readonly Dictionary<MethodDefinition, List<MethodDefinition>> base_methods = new Dictionary<MethodDefinition, List<MethodDefinition>> ();
		protected readonly Dictionary<AssemblyDefinition, ISymbolReader> symbol_readers = new Dictionary<AssemblyDefinition, ISymbolReader> ();
		protected readonly Dictionary<TypeDefinition, List<TypeDefinition>> class_type_base_hierarchy = new Dictionary<TypeDefinition, List<TypeDefinition>> ();
		protected readonly Dictionary<TypeDefinition, List<TypeDefinition>> derived_interfaces = new Dictionary<TypeDefinition, List<TypeDefinition>>();

		protected readonly Dictionary<object, Dictionary<IMetadataTokenProvider, object>> custom_annotations = new Dictionary<object, Dictionary<IMetadataTokenProvider, object>> ();
		protected readonly Dictionary<AssemblyDefinition, HashSet<string>> resources_to_remove = new Dictionary<AssemblyDefinition, HashSet<string>> ();
		protected readonly HashSet<CustomAttribute> marked_attributes = new HashSet<CustomAttribute> ();
		readonly HashSet<TypeDefinition> marked_types_with_cctor = new HashSet<TypeDefinition> ();
		protected readonly HashSet<TypeDefinition> marked_instantiated = new HashSet<TypeDefinition> ();
		protected readonly HashSet<MethodDefinition> indirectly_called = new HashSet<MethodDefinition>();

		private readonly IRuleDependencyRecorder rule_dependency_recorder;
		public SearchableDependencyGraph<NodeInfo, DependencyInfo> Graph => ((GraphDependencyRecorder)rule_dependency_recorder).graph;
		public HashSet<UnsafeReachingData> UnsafeReachingData => ((GraphDependencyRecorder)rule_dependency_recorder).unsafeReachingData;

		public GraphDependencyRecorder Recorder=> ((GraphDependencyRecorder)rule_dependency_recorder);

		public AnnotationStore (LinkContext context) {
			this.context = context;
			rule_dependency_recorder = new GraphDependencyRecorder (context);
		}

		public bool ProcessSatelliteAssemblies { get; set; }

		protected Tracer Tracer {
			get {
				return context.Tracer;
			}
		}

		[Obsolete ("Use Tracer in LinkContext directly")]
		public void PrepareDependenciesDump ()
		{
			Tracer.AddRecorder (new XmlDependencyRecorder (context));
		}

		[Obsolete ("Use Tracer in LinkContext directly")]
		public void PrepareDependenciesDump (string filename)
		{
			Tracer.AddRecorder (new XmlDependencyRecorder (context, filename));
		}

		public ICollection<AssemblyDefinition> GetAssemblies ()
		{
			return assembly_actions.Keys;
		}

		public AssemblyAction GetAction (AssemblyDefinition assembly)
		{
			if (assembly_actions.TryGetValue (assembly, out AssemblyAction action))
				return action;

			throw new InvalidOperationException($"No action for the assembly {assembly.Name} defined");
		}

		public MethodAction GetAction (MethodDefinition method)
		{
			if (method_actions.TryGetValue (method, out MethodAction action))
				return action;

			return MethodAction.Nothing;
		}

		public void SetAction (AssemblyDefinition assembly, AssemblyAction action)
		{
			assembly_actions [assembly] = action;
		}

		public bool HasAction (AssemblyDefinition assembly)
		{
			return assembly_actions.ContainsKey (assembly);
		}

		public void SetAction (MethodDefinition method, MethodAction action)
		{
			method_actions [method] = action;
		}

		public void SetMethodStubValue (MethodDefinition method, object value)
		{
			method_stub_values [method] = value;
		}

		public void SetFieldValue (FieldDefinition field, object value)
		{
			field_values [field] = value;
		}

		public void SetSubstitutedInit (FieldDefinition field)
		{
			field_init.Add (field);
		}

		public bool HasSubstitutedInit (FieldDefinition field)
		{
			return field_init.Contains (field);
		}

		public void SetSubstitutedInit (TypeDefinition type)
		{
			fieldType_init.Add (type);
		}

		public bool HasSubstitutedInit (TypeDefinition type)
		{
			return fieldType_init.Contains (type);
		}

		public void Mark (IMetadataTokenProvider provider)
		{
			marked.Add (provider);
			Tracer.AddDependency (provider, true);
		}

		public void Mark (CustomAttribute attribute)
		{
			marked_attributes.Add (attribute);
		}

		public void Push (IMetadataTokenProvider provider)
		{
			Tracer.Push (provider, false);
		}

		public void MarkAndPush (IMetadataTokenProvider provider)
		{
			Mark (provider);
			Tracer.Push (provider, false);
		}

		public bool IsMarked (IMetadataTokenProvider provider)
		{
			return marked.Contains (provider);
		}

		public bool IsMarked (CustomAttribute attribute)
		{
			return marked_attributes.Contains (attribute);
		}

		public void MarkIndirectlyCalledMethod (MethodDefinition method)
		{
			if (!context.AddReflectionAnnotations)
				return;

			indirectly_called.Add (method);
		}

		public bool HasMarkedAnyIndirectlyCalledMethods ()
		{
			return indirectly_called.Count != 0;
		}

		public bool IsIndirectlyCalled (MethodDefinition method)
		{
			return indirectly_called.Contains (method);
		}

		public void MarkInstantiatedUntracked (TypeDefinition type)
		{
			marked_instantiated.Add (type);
		}

		public bool IsInstantiated (TypeDefinition type)
		{
			return marked_instantiated.Contains (type);
		}

		public void Processed (IMetadataTokenProvider provider)
		{
			processed.Add (provider);
		}

		public bool IsProcessed (IMetadataTokenProvider provider)
		{
			return processed.Contains (provider);
		}

		public bool IsPreserved (TypeDefinition type)
		{
			return preserved_types.ContainsKey (type);
		}

		public void SetPreserve (TypeDefinition type, TypePreserve preserve)
		{
			if (preserved_types.TryGetValue (type, out TypePreserve existing))
				preserved_types [type] = ChoosePreserveActionWhichPreservesTheMost (existing, preserve);
			else
				preserved_types.Add (type, preserve);
		}

		public static TypePreserve ChoosePreserveActionWhichPreservesTheMost (TypePreserve leftPreserveAction, TypePreserve rightPreserveAction)
		{
			if (leftPreserveAction == rightPreserveAction)
				return leftPreserveAction;

			if (leftPreserveAction == TypePreserve.All || rightPreserveAction == TypePreserve.All)
				return TypePreserve.All;

			if (leftPreserveAction == TypePreserve.Nothing)
				return rightPreserveAction;

			if (rightPreserveAction == TypePreserve.Nothing)
				return leftPreserveAction;

			if ((leftPreserveAction == TypePreserve.Methods && rightPreserveAction == TypePreserve.Fields) ||
				(leftPreserveAction == TypePreserve.Fields && rightPreserveAction == TypePreserve.Methods))
				return TypePreserve.All;

			return rightPreserveAction;
		}

		public TypePreserve GetPreserve (TypeDefinition type)
		{
			if (preserved_types.TryGetValue (type, out TypePreserve preserve))
				return preserve;

			throw new NotSupportedException ($"No type preserve information for `{type}`");
		}

		public bool TryGetPreserve (TypeDefinition type, out TypePreserve preserve)
		{
			return preserved_types.TryGetValue (type, out preserve);
		}

		public bool TryGetMethodStubValue (MethodDefinition method, out object value)
		{
			return method_stub_values.TryGetValue (method, out value);
		}

		public bool TryGetFieldUserValue (FieldDefinition field, out object value)
		{
			return field_values.TryGetValue (field, out value);
		}

		public HashSet<string> GetResourcesToRemove (AssemblyDefinition assembly)
		{
			if (resources_to_remove.TryGetValue (assembly, out HashSet<string> resources))
				return resources;

			return null;
		}

		public void AddResourceToRemove (AssemblyDefinition assembly, string name)
		{
			if (!resources_to_remove.TryGetValue (assembly, out HashSet<string> resources))
				resources = resources_to_remove [assembly] = new HashSet<string> ();

			resources.Add (name);
		}

		public void SetPublic (IMetadataTokenProvider provider)
		{
			public_api.Add (provider);
		}

		public bool IsPublic (IMetadataTokenProvider provider)
		{
			return public_api.Contains (provider);
		}

		public void AddOverride (MethodDefinition @base, MethodDefinition @override, InterfaceImplementation matchingInterfaceImplementation = null)
		{
			var methods = GetOverrides (@base);
			if (methods == null) {
				methods = new List<OverrideInformation> ();
				override_methods [@base] = methods;
			}

			methods.Add (new OverrideInformation (@base, @override, matchingInterfaceImplementation));
		}

		public List<OverrideInformation> GetOverrides (MethodDefinition method)
		{
			if (override_methods.TryGetValue (method, out List<OverrideInformation> overrides))
				return overrides;

			return null;
		}

		public void AddBaseMethod (MethodDefinition method, MethodDefinition @base)
		{
			var methods = GetBaseMethods (method);
			if (methods == null) {
				methods = new List<MethodDefinition> ();
				base_methods [method] = methods;
			}

			methods.Add (@base);
		}

		public List<MethodDefinition> GetBaseMethods (MethodDefinition method)
		{
			if (base_methods.TryGetValue (method, out List<MethodDefinition> bases))
				return bases;

			return null;
		}

		public List<MethodDefinition> GetPreservedMethods (TypeDefinition type)
		{
			return GetPreservedMethods (type as IMemberDefinition);
		}

		public void AddPreservedMethod (TypeDefinition type, MethodDefinition method)
		{
			AddPreservedMethod (type as IMemberDefinition, method);
		}

		public List<MethodDefinition> GetPreservedMethods (MethodDefinition method)
		{
			return GetPreservedMethods (method as IMemberDefinition);
		}

		public void AddPreservedMethod (MethodDefinition key, MethodDefinition method)
		{
			AddPreservedMethod (key as IMemberDefinition, method);
		}

		List<MethodDefinition> GetPreservedMethods (IMemberDefinition definition)
		{
			if (preserved_methods.TryGetValue (definition, out List<MethodDefinition> preserved))
				return preserved;

			return null;
		}

		void AddPreservedMethod (IMemberDefinition definition, MethodDefinition method)
		{
			var methods = GetPreservedMethods (definition);
			if (methods == null) {
				methods = new List<MethodDefinition> ();
				preserved_methods [definition] = methods;
			}

			methods.Add (method);
		}

		public void AddSymbolReader (AssemblyDefinition assembly, ISymbolReader symbolReader)
		{
			symbol_readers [assembly] = symbolReader;
		}

		public void CloseSymbolReader (AssemblyDefinition assembly)
		{
			if (!symbol_readers.TryGetValue (assembly, out ISymbolReader symbolReader))
				return;

			symbol_readers.Remove (assembly);
			symbolReader.Dispose ();
		}

		public Dictionary<IMetadataTokenProvider, object> GetCustomAnnotations (object key)
		{
			if (custom_annotations.TryGetValue (key, out Dictionary<IMetadataTokenProvider, object> slots))
				return slots;

			slots = new Dictionary<IMetadataTokenProvider, object> ();
			custom_annotations.Add (key, slots);
			return slots;
		}

		public bool HasPreservedStaticCtor (TypeDefinition type)
		{
			return marked_types_with_cctor.Contains (type);
		}

		public bool SetPreservedStaticCtor (TypeDefinition type)
		{
			return marked_types_with_cctor.Add (type);
		}

		public void SetClassHierarchy (TypeDefinition type, List<TypeDefinition> bases)
		{
			class_type_base_hierarchy [type] = bases;
		}

		public List<TypeDefinition> GetClassHierarchy (TypeDefinition type)
		{
			if (class_type_base_hierarchy.TryGetValue (type, out List<TypeDefinition> bases))
				return bases;

			return null;
		}

		public void AddDerivedInterfaceForInterface (TypeDefinition @base, TypeDefinition derived)
		{
			if (!@base.IsInterface)
				throw new ArgumentException ($"{nameof (@base)} must be an interface");

			if (!derived.IsInterface)
				throw new ArgumentException ($"{nameof (derived)} must be an interface");

			if (!derived_interfaces.TryGetValue (@base, out List<TypeDefinition> derivedInterfaces))
				derived_interfaces [@base] = derivedInterfaces = new List<TypeDefinition> ();
			
			derivedInterfaces.Add(derived);
		}

		public List<TypeDefinition> GetDerivedInterfacesForInterface (TypeDefinition @interface)
		{
			if (!@interface.IsInterface)
				throw new ArgumentException ($"{nameof (@interface)} must be an interface");
			
			if (derived_interfaces.TryGetValue (@interface, out List<TypeDefinition> derivedInterfaces))
				return derivedInterfaces;

			return null;
		}

		// TODO: move these helpers into MarkingHelpers.

		public void MarkCustomAttribute (DependencyInfo reason, CustomAttribute ca)
		{
			// the linker doesn't really distinguish between an assembly and a MainModule.
			// it marks Modules, and later checks if an assembly is used by checking whether the MainModule was marked.
			// because of this, assembly attributes (which don't logically belong to a module) are parent-less.
			// instead, we make the same assumption that they are 1-to-1 with the MainModule.
			switch (reason.kind) {
			case DependencyKind.CustomAttribute:
			case DependencyKind.GenericParameterCustomAttribute:
			case DependencyKind.GenericParameterConstraintCustomAttribute:
			case DependencyKind.ParameterAttribute:
			case DependencyKind.ReturnTypeAttribute:
				rule_dependency_recorder.RecordCustomAttribute (reason, ca);
				break;
			case DependencyKind.PropertyAccessedViaReflection:
			case DependencyKind.PropertyOfType:
			case DependencyKind.PropertyOfPropertyMethod:
			case DependencyKind.EventAccessedViaReflection:
			case DependencyKind.EventOfType:
			case DependencyKind.EventOfEventMethod:
				throw new Exception("can't get here");
			case DependencyKind.AssemblyOrModuleCustomAttribute:
				context.MarkingHelpers.MarkEntryCustomAttribute (ca, new EntryInfo { kind = EntryKind.AssemblyOrModuleCustomAttribute, source = reason.source, entry = ca });
				break;
			default:
				throw new Exception("can't get here");
			}
			Mark (ca);
		}

		public void MarkScopeOfType (TypeDefinition type, IMetadataScope scope)
		{
			if (scope.GetType() != typeof(Mono.Cecil.ModuleDefinition)) {
				throw new Exception("non-module scope!?");
			}
			// rule_dependency_recorder.RecordScopeOfType (type, scope);
			// don't record sccopes, because they would only be used for custom attributes,
			// but we track module attributes as entry points currently.
			Mark (scope);
		}

		public void MarkMethodWithReason (DependencyInfo reason, MethodDefinition method)
		{
			rule_dependency_recorder.RecordMethodWithReason (reason, method);
			Mark (method);
		}

		public void MarkFieldWithReason (DependencyInfo reason, FieldDefinition field)
		{
			// TODO: if we ever don't set source, then the reason source might be NULL.
			// need to handle this better.
			rule_dependency_recorder.RecordFieldWithReason (reason, field);
			Mark (field);
		}

		public void MarkTypeWithReason (DependencyInfo reason, TypeDefinition type)
		{
			rule_dependency_recorder.RecordTypeWithReason (reason, type);
			if (reason.source == null) {
				if (type.ToString() == "System.IDisposable")
					System.Diagnostics.Debugger.Break();
			}
			Mark (type);
		}

		// every call to Annotations.Mark should ultimately go through one of these helpers,
		// each of which tracks a "reason" that the item was marked.

		// linker has a CheckProcessed.
		// to Mark here, we need to be sure that it
		// doesn't get processed twice.
		// actually doing Annotations.Mark twice is not the problem.
		// doing all the process logic is the problem.
		public void MarkMethodCall (MethodDefinition caller, MethodDefinition callee)
		{
			rule_dependency_recorder.RecordDirectCall (caller, callee);
			Mark (callee);
		}

		public void MarkVirtualMethodCall (MethodDefinition caller, MethodDefinition callee)
		{
			rule_dependency_recorder.RecordVirtualCall (caller, callee);
			Mark (callee);
			// TODO: see if there are multiple edges between same nodes.
			// there shouldn't be... maybe?
		}

		public void MarkMethodAccessedViaReflection (MethodDefinition source, MethodDefinition accessedMethod)
		{
			Mark (accessedMethod);
			rule_dependency_recorder.RecordAnalyzedReflectionAccess (source, accessedMethod);
		}


		// think of this as:
		// we report data reaching this API, along with a context.
		// the data is a call string suffix of length one, plus a value.
		// <callsite, value>
		// the value lattice has:
		// known string that resolves to a member < known string that doesn't resolve < unknown string
		// if dataflow analysis results in anything but a "known resolvable string" reaching certain APIs,
		// we report errors. so these dataflow results must be recorded for the reporting infrastructure.
		// ideally, also with a value!
		// later, maybe add an instruction index.
		public void MarkUnanalyzedReflectionCall (MethodDefinition source, MethodDefinition reflectionMethod, int instructionIndex, ReflectionData data)
		{
			// DATA is really: call context + data.
			// (source method -> reflection method), data
			// context is in general a list of callsites.
			// for now, just one callsite.
			rule_dependency_recorder.RecordUnanalyzedReflectionCall (source, reflectionMethod, instructionIndex, data);
		}

		// should we enforce that a method body is only ever reported once?
		// I think so...
		// but right now, we record it as dangerous at the callsite.
		// this is a hack. it should be reported dangerous exactly once,
		// when we scan the definition.

		public void MarkNestedType (TypeDefinition declaringType, TypeDefinition nestedType)
		{
			context.Annotations.Mark (nestedType);
			rule_dependency_recorder.RecordNestedType (declaringType, nestedType);
		}

		public void MarkUserDependencyType (CustomAttribute ca, TypeDefinition type)
		{
			context.Annotations.Mark (type);
			rule_dependency_recorder.RecordUserDependencyType (ca, type);
		}

		public void MarkTriggersStaticConstructorThroughFieldAccess (MethodDefinition method, MethodDefinition cctor)
		{
			context.Annotations.Mark (cctor);
			rule_dependency_recorder.RecordTriggersStaticConstructorThroughFieldAccess (method, cctor);
		}

		public void MarkTriggersStaticConstructorForCalledMethod (MethodDefinition method, MethodDefinition cctor)
		{
			context.Annotations.Mark (cctor);
			rule_dependency_recorder.RecordTriggersStaticConstructorForCalledMethod (method, cctor);
		}

		public void MarkStaticConstructorForField (FieldDefinition field, MethodDefinition cctor)
		{
			context.Annotations.Mark (cctor);
			rule_dependency_recorder.RecordStaticConstructorForField (field, cctor);
		}

		public void MarkInstantiatedByConstructor (MethodDefinition cctor, TypeDefinition type)
		{
			marked_instantiated.Add (type);
			System.Diagnostics.Debug.Assert (cctor.DeclaringType == type);
			rule_dependency_recorder.RecordInstantiatedByConstructor (cctor, type); // calls ctor?
			// really, need an edge from instantiated type -> all methods
			// so, track:
			// 1. instantiated type
			// 2. kept method for the instantiation
			// if we keep it just for the type, and also because it's an override... that's fine.
			// just need to report one.
			// would like to see:


			// dangerous virtual method
			// kept for type instantiated by: cctor

			// method:          T.M
			// on type:         T
			// instantiated by: T::.ctor
			// called from:     A.N

			// virtual method:            T.M
			// on type instantiated from: A.N

			// method:                        T.M
			// kept for instantiated type:     T
			// instantiated from: A.N

			// method:                  T.M
			// on type instantiated by: T::.ctor
			// called from:             A.N


		}

		public void MarkMethodOverrideOnInstantiatedType (TypeDefinition type, MethodDefinition method)
		{
			context.Annotations.Mark (method);
			rule_dependency_recorder.RecordOverrideOnInstantiatedType (type, method);
		}

		public void MarkOverride (MethodDefinition @base, MethodDefinition @override)
		{
			context.Annotations.Mark (@override);
			rule_dependency_recorder.RecordOverride (@base, @override);
		}

		public void MarkFieldAccessFromMethod (MethodDefinition method, FieldDefinition field)
		{
			context.Annotations.Mark (field);
			rule_dependency_recorder.RecordFieldAccessFromMethod (method, field);
		}


		public void MarkFieldUntracked (FieldDefinition field)
		{
			context.Annotations.Mark (field);
			rule_dependency_recorder.RecordFieldUntracked (field);
		}

		public void MarkTypeUntracked (TypeDefinition type)
		{
			context.Annotations.Mark (type);
			rule_dependency_recorder.RecordTypeUntracked (type);
		}

		// consider un-setting Untracked bit
		// if we later track it for a particular reason?
		public void MarkMethodUntracked (MethodDefinition method)
		{
			context.Annotations.Mark (method);
			rule_dependency_recorder.RecordMethodUntracked (method);
		}

		public void MarkEntryMethod (MethodDefinition method)
		{
			// should already be marked!
			// assert that: it already has an entry reason
			// it's already marked.
			System.Diagnostics.Debug.Assert (marked.Contains (method));
			System.Diagnostics.Debug.Assert (Recorder.entryInfo.Any(e => e.entry == method));
			context.Annotations.Mark (method);
			// it should already be in the graph as an entry method.
			// assert that it's already been reported to the recorder?
		}

		// MarkingHelpers mark the field originally, with an EntryInfo.
		// Annotations.MarkEntry* record it pretty much with no dependency.
		public void MarkEntryField (FieldDefinition field)
		{
			System.Diagnostics.Debug.Assert (marked.Contains (field));
			System.Diagnostics.Debug.Assert (Recorder.entryInfo.Any(e => e.entry == field));
			context.Annotations.Mark (field);
		}

		public void MarkDeclaringTypeOfMethod (MethodDefinition method, TypeDefinition type)
		{
			context.Annotations.Mark (type);
			rule_dependency_recorder.RecordDeclaringTypeOfMethod (method, type);
		}

		public void MarkDeclaringTypeOfType (TypeDefinition type, TypeDefinition parent)
		{
			Mark (parent);
			rule_dependency_recorder.RecordDeclaringTypeOfType (type, parent);
		}

		public readonly HashSet<AssemblyDefinition> userAssemblies;
		public void MarkUserAssembly (AssemblyDefinition assembly)
		{
			// TODO
			// userAssemblies.Add (assembly);
		}
	}
}
