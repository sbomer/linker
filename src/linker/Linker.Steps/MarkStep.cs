//
// MarkStep.cs
//
// Author:
//   Jb Evain (jbevain@gmail.com)
//
// (C) 2006 Jb Evain
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
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using Mono.Linker;

namespace Mono.Linker.Steps {

	public partial class MarkStep : IStep {

		protected LinkContext _context;

		protected Queue<(MethodDefinition, DependencyInfo)> _methods;
		protected List<MethodDefinition> _virtual_methods;
		protected Queue<AttributeProviderPair> _assemblyLevelAttributes;
		protected Queue<(AttributeProviderPair, DependencyInfo)> _lateMarkedAttributes;
		protected List<TypeDefinition> _typesWithInterfaces;
		protected List<MethodBody> _unreachableBodies;

		public MarkStep ()
		{
			_methods = new Queue<(MethodDefinition, DependencyInfo)> ();
			_virtual_methods = new List<MethodDefinition> ();
			_assemblyLevelAttributes = new Queue<AttributeProviderPair> ();
			_lateMarkedAttributes = new Queue<(AttributeProviderPair, DependencyInfo)> ();
			_typesWithInterfaces = new List<TypeDefinition> ();
			_unreachableBodies = new List<MethodBody> ();
		}

		public AnnotationStore Annotations => _context.Annotations;
		public Tracer Tracer => _context.Tracer;

		public virtual void Process (LinkContext context)
		{
			_context = context;

			Initialize ();
			Process ();
			Complete ();
		}

		void Initialize ()
		{
			foreach (AssemblyDefinition assembly in _context.GetAssemblies ())
				InitializeAssembly (assembly);
		}

		protected virtual void InitializeAssembly (AssemblyDefinition assembly)
		{
			Tracer.Push (assembly);
			try {
				switch (_context.Annotations.GetAction (assembly)) {
				case AssemblyAction.Copy:
				case AssemblyAction.Save:
					MarkEntireAssembly (assembly);
					break;
				case AssemblyAction.Link:
				case AssemblyAction.AddBypassNGen:
				case AssemblyAction.AddBypassNGenUsed:
					MarkAssembly (assembly);

					foreach (TypeDefinition type in assembly.MainModule.Types)
						InitializeType (type);
					break;
				}
			} finally {
				Tracer.Pop ();
			}
		}

		void Complete ()
		{
			foreach (var body in _unreachableBodies) {
				Annotations.SetAction (body.Method, MethodAction.ConvertToThrow);
			}
		}

		void InitializeType (TypeDefinition type)
		{
			if (type.HasNestedTypes) {
				foreach (var nested in type.NestedTypes)
					InitializeType (nested);
			}

			// problem is that this does full MarkType logic
			// which can recursively mark types
			// BEFORE we have encountered all of the "entry" types.
			// so some of the processing is done before we've determined entries.
			// that would be OK, if not for the fact that our graph is immutable.
			// we could either:
			// allow mutating the graph (dangerous!)
			//   is it actually so bad?
			//   Entry info isn't used until later.
			// OR, track entry info differently somehow.

			// the current algorithm will  sometimes do initialization
			// (for example for an assembly that has been marked "copy")
			// AFTER it has marked the type in MarkType()
			// but never before the type has at least been early-marked in a different step.
			// when the type is marked, we should record it.
			// what's the reason to mark it early on?
			// or to mark it here?

			// I want multiple markings to correspond to multiple conceptual reasons.
			// once because it's a copy assembly, once because it's a field type. fine.
			// base reason is user input.
			// all user input should be processed before we do recursive logic.
			// some parts of markstep will do MarkType for things already marked in annotations,
			// for entry user reasons.
			// user => mark annotation
			// mark annotation => initialize in markstep

			if (Annotations.IsMarked (type))
				MarkType (type, new DependencyInfo (DependencyKind.EntryType));

			// this type has already been marked.
			// it could only have been marked as an entry type so far.
			// TODO: assert that the type has previously been marked as an entry type.
			// no, it might have been marked as declaring type of the entry method.
			// or even declaring type of a kept type.
			// TODO: FIX THIS BUG.
			// we need to track the reason that a type was early-marked,
			// so that it can be appropriately set here.

			// if this was the declaring type of the entry point method,
			// want to trigger its cctor from entry method.

			// xml roots are incompatible with proper tracing.
			// how to report them?
			// just report all methods as potentially called.

			if (type.HasFields)
				InitializeFields (type);
			if (type.HasMethods)
				InitializeMethods (type.Methods);
		}

		protected bool IsFullyPreserved (TypeDefinition type)
		{
			if (Annotations.TryGetPreserve (type, out TypePreserve preserve) && preserve == TypePreserve.All)
				return true;

			switch (Annotations.GetAction (type.Module.Assembly)) {
			case AssemblyAction.Save:
			case AssemblyAction.Copy:
			case AssemblyAction.CopyUsed:
			case AssemblyAction.AddBypassNGen:
			case AssemblyAction.AddBypassNGenUsed:
				return true;
			}

			return false;
		}

		void InitializeFields (TypeDefinition type)
		{
			foreach (FieldDefinition field in type.Fields)
				if (Annotations.IsMarked (field)) {
					// TODO: assert that it was previously marked as an entry.
					MarkField (field, new DependencyInfo (DependencyKind.EntryField));
				}
		}

		void InitializeMethods (Collection<MethodDefinition> methods)
		{
			foreach (MethodDefinition method in methods)
				if (Annotations.IsMarked (method)) {
					// this enqueues marked methods in marked types.
					// can there be marked methods in unmarked types?

					// can get here for a preserveall type (from xml)
					// which has preserve info applied in MarkType from InitializeType.

					// TODO: assert that it was previously marked as an entry.
					// we don't really want to do this twice...
					// maybe we can just get rid of tracing from the earlier steps?
					// we mark it once up-front. (early marking)
					// then we actually process it again (and mark it as well)

					// I think this is a bug, similar to InitializeTypes.
					// there might be a way to mark the method before it's initialized.
					// then when initializing, it would already be in the graph as a non-Entry.
					// need to make sure anything marked as an ENTRY is so before it's ever put in
					// the dependency graph. this lets us avoid mutating things.
					EnqueueMethod (method, new DependencyInfo (DependencyKind.EntryMethod));
				}
		}

		// this logic can run multiple times... :(

		// MarkEntireType may be called AFTER MarkType has already run for a type.
		// SO, we can't assume that marking a type here will be the first time.
		// either:
		// - don't mark it as an entry here, OR
		//     cleaner and simpler if we ensure that "entry" marking happens before any recursive scanning.
		//     can't mark "entry" in MET. must give each MET a reason.
		//     reason can be: nested, userdep, type in assembly. done!
		//     type in assembly: where does assembly come from? need to record it in the graph or as an entry.
		//     MarkStep never sets action - only responds to already-set actions.
		//     assembly action is the entry.
		//     we will have types marked as entry AND in the graph - that is OK.
		// - allow marking it as an entry here, even though it was already marked for another reason.
		void MarkEntireType (TypeDefinition type, DependencyInfo reason)
		{
			if (type.HasNestedTypes) {
				foreach (TypeDefinition nested in type.NestedTypes)
					MarkEntireType (nested, new DependencyInfo (DependencyKind.NestedType, type));
			}

			switch (reason.Kind) {
			case DependencyKind.TypeInAssembly:
				Annotations.MarkTypeWithReason (reason, type);
				break;
			case DependencyKind.NestedType:
				Annotations.MarkNestedType ((TypeDefinition)reason.Source, type);
				break;
			case DependencyKind.UserDependencyType:
				Annotations.MarkUserDependencyType ((CustomAttribute)reason.Source, type);
				break;
			default:
				throw new NotImplementedException ("don't support kind " + reason.Kind);
			}

			MarkCustomAttributes (type, new DependencyInfo (DependencyKind.CustomAttribute, type));
			MarkTypeSpecialCustomAttributes (type);

			if (type.HasInterfaces) {
				foreach (InterfaceImplementation iface in type.Interfaces) {
					MarkInterfaceImplementation (iface, type);
				}
			}

			MarkGenericParameterProvider (type);

			if (type.HasFields) {
				foreach (FieldDefinition field in type.Fields) {
					MarkField (field, new DependencyInfo (DependencyKind.FieldForType, type));
				}
			}

			if (type.HasMethods) {
				foreach (MethodDefinition method in type.Methods) {
					// Annotations.Mark (method);
					// isn't this Mark redundant?
					Annotations.SetAction (method, MethodAction.ForceParse);
					// we don't have a particular reason this method can be called...
					// so don't track it.
					// can only get here
					// because copy/save assembly caused everything in it to be
					// marked.
					// unknown entry reason for now.
					// untracked dependency.
					EnqueueMethod (method, new DependencyInfo (DependencyKind.MethodForType, type));
				}
			}

			if (type.HasProperties) {
				// TODO: this won't actually mark property methods!
				// but they'll be marked by the method marking logic. so no bug here.
				foreach (var property in type.Properties) {
					MarkProperty (property, new DependencyInfo (DependencyKind.PropertyOfType, type));
				}
			}

			if (type.HasEvents) {
				foreach (var ev in type.Events) {
					MarkEvent (ev, new DependencyInfo (DependencyKind.EventOfType, type));
				}
			}
		}

		void Process ()
		{
			while (ProcessPrimaryQueue () || ProcessLazyAttributes () || ProcessLateMarkedAttributes ())

			// deal with [TypeForwardedTo] pseudo-attributes
			foreach (AssemblyDefinition assembly in _context.GetAssemblies ()) {
				if (!assembly.MainModule.HasExportedTypes)
					continue;

				foreach (var exported in assembly.MainModule.ExportedTypes) {
					bool isForwarder = exported.IsForwarder;
					var declaringType = exported.DeclaringType;
					while (!isForwarder && (declaringType != null)) {
						isForwarder = declaringType.IsForwarder;
						declaringType = declaringType.DeclaringType;
					}

					if (!isForwarder)
						continue;
					TypeDefinition type = exported.Resolve ();
					if (type == null)
						continue;
					if (!Annotations.IsMarked (type))
						continue;
					Tracer.Push (type);
					try {
						_context.MarkingHelpers.MarkExportedType (exported, assembly.MainModule);
					} finally {
						Tracer.Pop ();
					}
				}
			}
		}

		bool ProcessPrimaryQueue ()
		{
			if (QueueIsEmpty ())
				return false;

			while (!QueueIsEmpty ()) {
				ProcessQueue ();
				ProcessVirtualMethods ();
				ProcessMarkedTypesWithInterfaces ();
				ProcessPendingBodies ();
				DoAdditionalProcessing ();
			}

			return true;
		}

		void ProcessQueue ()
		{
			while (!QueueIsEmpty ()) {
				(MethodDefinition method, DependencyInfo reason) = _methods.Dequeue ();
				Tracer.Push (method);
				try {
					ProcessMethod (method, reason);
				} catch (Exception e) {
					throw new MarkException (string.Format ("Error processing method: '{0}' in assembly: '{1}'", method.FullName, method.Module.Name), e, method);
				} finally {
					Tracer.Pop ();
				}
			}
		}

		bool QueueIsEmpty ()
		{
			return _methods.Count == 0;
		}

		protected virtual void EnqueueMethod (MethodDefinition method, DependencyInfo reason)
		{
			_methods.Enqueue ((method, reason));
		}

		void ProcessVirtualMethods ()
		{
			foreach (MethodDefinition method in _virtual_methods) {
				Tracer.Push (method);
				ProcessVirtualMethod (method);
				Tracer.Pop ();
			}
		}

		void ProcessMarkedTypesWithInterfaces ()
		{
			// We may mark an interface type later on.  Which means we need to reprocess any time with one or more interface implementations that have not been marked
			// and if an interface type is found to be marked and implementation is not marked, then we need to mark that implementation

			// copy the data to avoid modified while enumerating error potential, which can happen under certain conditions.
			var typesWithInterfaces = _typesWithInterfaces.ToArray ();

			foreach (var type in typesWithInterfaces) {
				// Exception, types that have not been flagged as instantiated yet.  These types may not need their interfaces even if the
				// interface type is marked
				if (!Annotations.IsInstantiated (type))
					continue;

				MarkInterfaceImplementations (type);
			}
		}

		void ProcessPendingBodies ()
		{
			for (int i = 0; i < _unreachableBodies.Count; i++) {
				var body = _unreachableBodies [i];
				if (Annotations.IsInstantiated (body.Method.DeclaringType)) {
					MarkMethodBody (body);
					_unreachableBodies.RemoveAt (i--);
				}
			}
		}

		void ProcessVirtualMethod (MethodDefinition method)
		{
			var overrides = Annotations.GetOverrides (method);
			if (overrides == null)
				return;

			foreach (OverrideInformation @override in overrides)
				ProcessOverride (@override, method);
		}

		void ProcessOverride (OverrideInformation overrideInformation, MethodDefinition virtualMethod)
		{
			// TODO: is it guaranteed that method is the same as the virtual method we are processing?
			var method = overrideInformation.Override;
			var @base = overrideInformation.Base;
			// System.Diagnostics.Debug.Assert (method == virtualMethod);
			if (method != virtualMethod) {
				_context.LogMessage("override processing for " + method.ToString() + " gives override: " + method.ToString() + " of " + @base.ToString());
			}
			if (!Annotations.IsMarked (method.DeclaringType))
				return;

			if (Annotations.IsProcessed (method))
				return;

			if (Annotations.IsMarked (method))
				return;

			var isInstantiated = Annotations.IsInstantiated (method.DeclaringType);

			// We don't need to mark overrides until it is possible that the type could be instantiated
			// Note : The base type is interface check should be removed once we have base type sweeping
			if (IsInterfaceOverrideThatDoesNotNeedMarked (overrideInformation, isInstantiated))
				return;

			if (!isInstantiated && !@base.IsAbstract && _context.IsOptimizationEnabled (CodeOptimizations.OverrideRemoval, method))
				return;

			// only track instantiations if override removal is enabled.
			// if it's disabled, all overrides are kept, so there's no instantiation site to blame.
			if (_context.IsOptimizationEnabled (CodeOptimizations.OverrideRemoval, method)) {
				if (isInstantiated) {
					MarkMethod (method, new DependencyInfo (DependencyKind.OverrideOnInstantiatedType, method.DeclaringType));
				} else if (@base.IsAbstract) {
					// handle abstract methods the same as we do any method when override removal is disabled.
					MarkMethod (method, new DependencyInfo (DependencyKind.Override, @base)); // don't track this dependency yet.
				}
			} else {
				// if it's disabled, all overrides of called methods are kept.
				// there's nothing to blame but the called method.
				MarkMethod (method, new DependencyInfo (DependencyKind.Override, @base));
			}

			// here:
			// depending on why the type was instantiated, we may want to track dependencies differently.
			// if instantiated due to ctor call, would want an edge ctor -> virtual method
			// if instantiated because it's an interface... there is no ctor.
			// general thing that works: edge from type to the override

			// this is where we mark an override of a virtual method.
			// BUT we don't know WHY whe virtual itself was marked yet.
			// maybe because it was called, maybe something else.
			// we only want to track overrides if we know that this method has been called.
			// we at this point want there to be an edge from the original caller
			// to this method.
			// need to remember the original caller.
			// if we ever make this more accurate, the actual callers would be present.
			// but here, ANY caller to the base method needs to be updated.
			// but there's no way we will have that information at this point.
			// if we mark the target for a calvirt...
			// do we make its mark reason depend on the reason for marking the method itself?
			// when do we want to create an edge from a virtual to an override?
			// whenever the linker keeps it? or for more specific reasons?
			// the linker should only keep it if... it's really necessary.
			// for now, always track overrides.

			// because marking a type instantiated goes through annotations, when we check it,
			// it no longer has a reason for being instantiated.
			// -> give it a reason (track more stuff for instantiated types)
			// -> or just have a type -> method dependency for instantiated types
			// for now:
			//   separately track why a type was instantiated,
			//   and the consequences of the instantiation.

			// he have:
			// marked a virtual
			// with an override
			// whose declaring type is marked
			// that's not already processed: problem!
			//    we need to be able to re-process an override.
			//    as an override of a different virtual call target.
			//    well, I guess it's enough to just report one. don't need to make the graph complete.
			// and also not already marked
			//    problem: we will only ever mark an override if it's not already marked!
			// and not an unneeded interface override
			// and (for override removal), it's instantiated or abstract

			ProcessVirtualMethod (method);
		}

		bool IsInterfaceOverrideThatDoesNotNeedMarked (OverrideInformation overrideInformation, bool isInstantiated)
		{
			if (!overrideInformation.IsOverrideOfInterfaceMember || isInstantiated)
				return false;

			if (overrideInformation.MatchingInterfaceImplementation != null)
				return !Annotations.IsMarked (overrideInformation.MatchingInterfaceImplementation);

			var interfaceType = overrideInformation.InterfaceType;
			var overrideDeclaringType = overrideInformation.Override.DeclaringType;

			if (!IsInterfaceImplementationMarked (overrideDeclaringType, interfaceType)) {
				var derivedInterfaceTypes = Annotations.GetDerivedInterfacesForInterface (interfaceType);

				// There are no derived interface types that could be marked, it's safe to skip marking this override
				if (derivedInterfaceTypes == null)
					return true;

				// If none of the other interfaces on the type that implement the interface from the @base type are marked, then it's safe to skip
				// marking this override
				if (!derivedInterfaceTypes.Any (d => IsInterfaceImplementationMarked (overrideDeclaringType, d)))
					return true;
			}

			return false;
		}

		bool IsInterfaceImplementationMarked (TypeDefinition type, TypeDefinition interfaceType)
		{
			return type.HasInterface (@interfaceType, out InterfaceImplementation implementation) && Annotations.IsMarked (implementation);
		}

		void MarkMarshalSpec (IMarshalInfoProvider spec, DependencyInfo reason)
		{
			if (!spec.HasMarshalInfo)
				return;

			if (spec.MarshalInfo is CustomMarshalInfo marshaler)
				MarkType (marshaler.ManagedType, reason);
		}

		void MarkCustomAttributes (ICustomAttributeProvider provider, DependencyInfo reason)
		{
			if (!provider.HasCustomAttributes)
				return;

			bool markOnUse = _context.KeepUsedAttributeTypesOnly && Annotations.GetAction (GetAssemblyFromCustomAttributeProvider (provider)) == AssemblyAction.Link;

			Tracer.Push (provider);
			try {
				foreach (CustomAttribute ca in provider.CustomAttributes) {
					if (ProcessLinkerSpecialAttribute (ca, provider, reason)) {
						continue;
					}

					if (markOnUse) {
						_lateMarkedAttributes.Enqueue ((new AttributeProviderPair (ca, provider), reason));
						continue;
					}

					MarkCustomAttribute (ca, reason);
					MarkSpecialCustomAttributeDependencies (ca);
				}
			} finally {
				Tracer.Pop ();
			}
		}

		protected virtual bool ProcessLinkerSpecialAttribute (CustomAttribute ca, ICustomAttributeProvider provider, DependencyInfo reason)
		{
			if (IsUserDependencyMarker (ca.AttributeType) && provider is MemberReference mr) {
				MarkUserDependency (mr, ca);

				if (_context.KeepDependencyAttributes || Annotations.GetAction (mr.Module.Assembly) != AssemblyAction.Link) {
					MarkCustomAttribute (ca, reason);
				}

				return true;
			}

			return false;
		}

		protected static AssemblyDefinition GetAssemblyFromCustomAttributeProvider (ICustomAttributeProvider provider)
		{
			return provider switch {
				MemberReference mr => mr.Module.Assembly,
				AssemblyDefinition ad => ad,
				ModuleDefinition md => md.Assembly,
				InterfaceImplementation ii => ii.InterfaceType.Module.Assembly,
				GenericParameterConstraint gpc => gpc.ConstraintType.Module.Assembly,
				ParameterDefinition pd => pd.ParameterType.Module.Assembly,
				MethodReturnType mrt => mrt.ReturnType.Module.Assembly,
				_ => throw new NotImplementedException (provider.GetType ().ToString ()),
			};
		}

		protected virtual bool IsUserDependencyMarker (TypeReference type)
		{
			return PreserveDependencyLookupStep.IsPreserveDependencyAttribute (type);
		}

		protected virtual void MarkUserDependency (MemberReference context, CustomAttribute ca)
		{
			if (ca.HasProperties && ca.Properties [0].Name == "Condition") {
				var condition = ca.Properties [0].Argument.Value as string;
				switch (condition) {
				case "":
				case null:
					break;
				case "DEBUG":
					if (!_context.KeepMembersForDebugger)
						return;

					break;
				default:
					// Don't have yet a way to match the general condition so everything is excluded
					return;
				}
			}

			AssemblyDefinition assembly;
			var args = ca.ConstructorArguments;
			if (args.Count >= 3 && args [2].Value is string assemblyName) {
				if (!_context.Resolver.AssemblyCache.TryGetValue (assemblyName, out assembly)) {
					_context.LogMessage (MessageImportance.Low, $"Could not resolve '{assemblyName}' assembly dependency");
					return;
				}
			} else {
				assembly = null;
			}

			TypeDefinition td;
			if (args.Count >= 2 && args [1].Value is string typeName) {
				td = FindType (assembly ?? context.Module.Assembly, typeName);

				if (td == null) {
					_context.LogMessage (MessageImportance.Low, $"Could not resolve '{typeName}' type dependency");
					return;
				}
			} else {
				td = context.DeclaringType.Resolve ();
			}

			string member = null;
			string[] signature = null;
			if (args.Count >= 1 && args [0].Value is string memberSignature) {
				memberSignature = memberSignature.Replace (" ", "");
				var sign_start = memberSignature.IndexOf ('(');
				var sign_end = memberSignature.LastIndexOf (')');
				if (sign_start > 0 && sign_end > sign_start) {
					var parameters = memberSignature.Substring (sign_start + 1, sign_end - sign_start - 1);
					signature = string.IsNullOrEmpty (parameters) ? Array.Empty<string> () : parameters.Split (',');
					member = memberSignature.Substring (0, sign_start);
				} else {
					member = memberSignature;
				}
			}

			if (member == "*") {
				MarkEntireType (td, new DependencyInfo (DependencyKind.UserDependencyType, ca));
				return;
			}

			if (MarkDependencyMethod (td, member, signature, context))
				return;

			if (MarkDependencyField (td, member, new DependencyInfo (DependencyKind.UserDependencyField, ca)))
				return;

			_context.LogMessage (MessageImportance.High, $"Could not resolve dependency member '{member}' declared in type '{td.FullName}'");
		}

		static TypeDefinition FindType (AssemblyDefinition assembly, string fullName)
		{
			fullName = fullName.ToCecilName ();

			var type = assembly.MainModule.GetType (fullName);
			return type?.Resolve ();
		}

		bool MarkDependencyMethod (TypeDefinition type, string name, string[] signature, MemberReference context)
		{
			bool marked = false;

			int arity_marker = name.IndexOf ('`');
			if (arity_marker < 1 || !int.TryParse (name.Substring (arity_marker + 1), out int arity)) {
				arity = 0;
			} else {
				name = name.Substring (0, arity_marker);
			}
			
			foreach (var m in type.Methods) {
				if (m.Name != name)
					continue;

				if (m.GenericParameters.Count != arity)
					continue;

				if (signature == null) {
					MarkIndirectlyCalledMethod (m, new DependencyInfo (DependencyKind.PreserveDependency, context));
					marked = true;
					continue;
				}

				var mp = m.Parameters;
				if (mp.Count != signature.Length)
					continue;

				int i = 0;
				for (; i < signature.Length; ++i) {
					if (mp [i].ParameterType.FullName != signature [i].Trim ().ToCecilName ()) {
						i = -1;
						break;
					}
				}

				if (i < 0)
					continue;

				MarkIndirectlyCalledMethod (m, new DependencyInfo (DependencyKind.PreserveDependency, context));
				marked = true;
			}

			return marked;
		}

		bool MarkDependencyField (TypeDefinition type, string name, DependencyInfo reason)
		{
			foreach (var f in type.Fields) {
				if (f.Name == name) {
					MarkField (f, reason);
					return true;
				}
			}

			return false;
		}

		void LazyMarkCustomAttributes (ICustomAttributeProvider provider, ModuleDefinition module)
		{
			if (!provider.HasCustomAttributes)
				return;

			foreach (CustomAttribute ca in provider.CustomAttributes)
				_assemblyLevelAttributes.Enqueue (new AttributeProviderPair (ca, module));
		}

		protected virtual void MarkCustomAttribute (CustomAttribute ca, DependencyInfo reason)
		{
			Tracer.Push ((object)ca.AttributeType ?? (object)ca);
			try {
				Annotations.MarkCustomAttribute (reason, ca);
				MarkMethod (ca.Constructor, new DependencyInfo (DependencyKind.AttributeConstructor, ca));

				MarkCustomAttributeArguments (ca);

				TypeReference constructor_type = ca.Constructor.DeclaringType;
				TypeDefinition type = constructor_type.Resolve ();

				if (type == null) {
					HandleUnresolvedType (constructor_type);
					return;
				}

				MarkCustomAttributeProperties (ca, type);
				MarkCustomAttributeFields (ca, type);
			} finally {
				Tracer.Pop ();
			}
		}

		protected virtual bool ShouldMarkCustomAttribute (CustomAttribute ca, ICustomAttributeProvider provider)
		{
			var attr_type = ca.AttributeType;

			if (_context.KeepUsedAttributeTypesOnly) {
				switch (attr_type.FullName) {
				// These are required by the runtime
				case "System.ThreadStaticAttribute":
				case "System.ContextStaticAttribute":
				case "System.Runtime.CompilerServices.IsByRefLikeAttribute":
					return true;
				// Attributes related to `fixed` keyword used to declare fixed length arrays
				case "System.Runtime.CompilerServices.FixedBufferAttribute":
					return true;
				case "System.Runtime.InteropServices.InterfaceTypeAttribute":
				case "System.Runtime.InteropServices.GuidAttribute":
				case "System.Runtime.CompilerServices.InternalsVisibleToAttribute":
					return true;
				}
				
				if (!Annotations.IsMarked (attr_type.Resolve ()))
					return false;
			}

			return true;
		}

		protected virtual bool ShouldMarkTypeStaticConstructor (TypeDefinition type)
		{
			if (Annotations.HasPreservedStaticCtor (type))
				return false;
			
			if (type.IsBeforeFieldInit && _context.IsOptimizationEnabled (CodeOptimizations.BeforeFieldInit, type))
				return false;

			return true;
		}

		protected void MarkStaticConstructor (TypeDefinition type, DependencyInfo reason)
		{
			foreach (var method in type.Methods) {
				if (IsNonEmptyStaticConstructor (method)) {
					MethodDefinition cctor = MarkMethod (method, reason);
					if (cctor != null)
						Annotations.SetPreservedStaticCtor (type);
				}
			}
		}

		protected virtual bool ShouldMarkTopLevelCustomAttribute (AttributeProviderPair app, MethodDefinition resolvedConstructor)
		{
			var ca = app.Attribute;

			if (!ShouldMarkCustomAttribute (app.Attribute, app.Provider))
				return false;

			// If an attribute's module has not been marked after processing all types in all assemblies and the attribute itself has not been marked,
			// then surely nothing is using this attribute and there is no need to mark it
			if (!Annotations.IsMarked (resolvedConstructor.Module) &&
				!Annotations.IsMarked (ca.AttributeType) &&
				Annotations.GetAction (resolvedConstructor.Module.Assembly) == AssemblyAction.Link)
				return false;

			if (ca.Constructor.DeclaringType.Namespace == "System.Diagnostics") {
				string attributeName = ca.Constructor.DeclaringType.Name;
				if (attributeName == "DebuggerDisplayAttribute" || attributeName == "DebuggerTypeProxyAttribute") {
					var displayTargetType = GetDebuggerAttributeTargetType (app.Attribute, (AssemblyDefinition) app.Provider);
					if (displayTargetType == null || !Annotations.IsMarked (displayTargetType))
						return false;
				}
			}
			
			return true;
		}

		protected void MarkSecurityDeclarations (ISecurityDeclarationProvider provider, DependencyInfo reason)
		{
			// most security declarations are removed (if linked) but user code might still have some
			// and if the attributes references types then they need to be marked too
			if ((provider == null) || !provider.HasSecurityDeclarations)
				return;

			foreach (var sd in provider.SecurityDeclarations)
				MarkSecurityDeclaration (sd, reason);
		}

		protected virtual void MarkSecurityDeclaration (SecurityDeclaration sd, DependencyInfo reason)
		{
			if (!sd.HasSecurityAttributes)
				return;
			
			foreach (var sa in sd.SecurityAttributes)
				MarkSecurityAttribute (sa, reason);
		}

		// TODO: security attributes can be removed by a later step.
		// maybe that's why they don't get marked here?
		// don't think so - RemoveSecurityStep happens before MarkStep.
		protected virtual void MarkSecurityAttribute (SecurityAttribute sa, DependencyInfo reason)
		{
			TypeReference security_type = sa.AttributeType;
			TypeDefinition type = security_type.Resolve ();
			if (type == null) {
				HandleUnresolvedType (security_type);
				return;
			}

			// the sa never acutually gets marked.
			// unlike custom attributes. so we can't include sa in the graph, can we?
			// maybe we just should.
			switch (reason.Kind) {
			case DependencyKind.AssemblyOrModuleCustomAttribute:
				// track this unmarked sa as an entry.
				_context.MarkingHelpers.MarkEntryCustomAttribute (sa, new EntryInfo (EntryKind.AssemblyOrModuleCustomAttribute, reason.Source, sa));
				break;
			default:
				// otherwise, security attribute is recorded with reason (even though never marked)
				_context.MarkingHelpers.MarkSecurityAttribute (sa, reason);
				break;
			}

			// this will mark ca -> attribute type
			MarkType (security_type, new DependencyInfo (DependencyKind.AttributeType, sa));
			// these will mark ca -> properties, ca -> fields.
			MarkCustomAttributeProperties (sa, type);
			MarkCustomAttributeFields (sa, type);
		}

		protected void MarkCustomAttributeProperties (ICustomAttribute ca, TypeDefinition attribute)
		{
			if (!ca.HasProperties)
				return;

			foreach (var named_argument in ca.Properties)
				MarkCustomAttributeProperty (named_argument, attribute, ca, new DependencyInfo (DependencyKind.AttributeProperty, ca));
		}

		protected void MarkCustomAttributeProperty (CustomAttributeNamedArgument namedArgument, TypeDefinition attribute, ICustomAttribute ca, DependencyInfo reason)
		{
			PropertyDefinition property = GetProperty (attribute, namedArgument.Name);
			Tracer.Push (property);
			if (property != null)
				MarkMethod (property.SetMethod, reason);

			MarkCustomAttributeArgument (namedArgument.Argument, ca);
			Tracer.Pop ();
		}

		PropertyDefinition GetProperty (TypeDefinition type, string propertyname)
		{
			while (type != null) {
				PropertyDefinition property = type.Properties.FirstOrDefault (p => p.Name == propertyname);
				if (property != null)
					return property;

				// what if it's generic?
				type = type.BaseType?.Resolve ();
			}

			return null;
		}

		protected void MarkCustomAttributeFields (ICustomAttribute ca, TypeDefinition attribute)
		{
			if (!ca.HasFields)
				return;

			foreach (var named_argument in ca.Fields)
				MarkCustomAttributeField (named_argument, attribute, ca);
		}

		protected void MarkCustomAttributeField (CustomAttributeNamedArgument namedArgument, TypeDefinition attribute, ICustomAttribute ca)
		{
			FieldDefinition field = GetField (attribute, namedArgument.Name);
			if (field != null)
				MarkField (field, new DependencyInfo (DependencyKind.CustomAttributeField, ca));

			MarkCustomAttributeArgument (namedArgument.Argument, ca);
		}

		FieldDefinition GetField (TypeDefinition type, string fieldname)
		{
			while (type != null) {
				FieldDefinition field = type.Fields.FirstOrDefault (f => f.Name == fieldname);
				if (field != null)
					return field;

				// generic?
				type = type.BaseType?.Resolve ();
			}

			return null;
		}

		MethodDefinition GetMethodWithNoParameters (TypeDefinition type, string methodname)
		{
			while (type != null) {
				MethodDefinition method = type.Methods.FirstOrDefault (m => m.Name == methodname && !m.HasParameters);
				if (method != null)
					return method;

				// generic?
				type = type.BaseType.Resolve ();
			}

			return null;
		}

		void MarkCustomAttributeArguments (CustomAttribute ca)
		{
			if (!ca.HasConstructorArguments)
				return;

			foreach (var argument in ca.ConstructorArguments)
				MarkCustomAttributeArgument (argument, ca);
		}

		void MarkCustomAttributeArgument (CustomAttributeArgument argument, ICustomAttribute ca)
		{
			var at = argument.Type;

			if (at.IsArray) {
				// for some reason, custom attribute arguments that are arrays are modeled
				// as arrays of custom attribute arguments,
				// each of which has a type and a value,
				// instead of an array of certain type, with a list of values attached.
				// so this will result in redundant marking of the type, I guess.
				var et = at.GetElementType ();

				MarkType (et, new DependencyInfo (DependencyKind.CustomAttributeArgumentType, ca));
				if (argument.Value == null)
					return;

				foreach (var caa in (CustomAttributeArgument [])argument.Value)
					MarkCustomAttributeArgument (caa, ca);

				return;
			}

			if (at.Namespace == "System") {
				switch (at.Name) {
				case "Type":
					MarkType (argument.Type, new DependencyInfo (DependencyKind.CustomAttributeArgumentType, ca));
					MarkType ((TypeReference)argument.Value, new DependencyInfo (DependencyKind.CustomAttributeArgumentValue, ca));
					return;

				case "Object":
					var boxed_value = (CustomAttributeArgument)argument.Value;
					MarkType (boxed_value.Type, new DependencyInfo (DependencyKind.CustomAttributeArgumentType, ca));
					// don't understand this logic... don't worry about it for now
					// worry when we get there. just mark "null" reason, and expect a NRE later
					MarkCustomAttributeArgument (boxed_value, null);
					return;
				}
			}
		}

		protected bool CheckProcessed (IMetadataTokenProvider provider)
		{
			if (Annotations.IsProcessed (provider))
				return true;

			Annotations.Processed (provider);
			return false;
		}

		protected void MarkAssembly (AssemblyDefinition assembly)
		{
			if (CheckProcessed (assembly))
				return;

			ProcessModule (assembly);

			MarkAssemblyCustomAttributes (assembly);

			MarkSecurityDeclarations (assembly, new DependencyInfo (DependencyKind.AssemblyOrModuleCustomAttribute, assembly));

			foreach (ModuleDefinition module in assembly.Modules)
				LazyMarkCustomAttributes (module, module);
		}

		void MarkEntireAssembly (AssemblyDefinition assembly)
		{
			// don't think we mark the module for this assembly.
			// this could be a bug. if nothing uses a type referencing this assembly's
			// mainmodule, it would actually be removed.

			// TODO: prove that a "-a" assembly will be removed
			// because its module doesn't ever get marked.
			MarkCustomAttributes (assembly, new DependencyInfo (DependencyKind.AssemblyOrModuleCustomAttribute, assembly));
			MarkCustomAttributes (assembly.MainModule, new DependencyInfo (DependencyKind.AssemblyOrModuleCustomAttribute, assembly.MainModule));

			if (assembly.MainModule.HasExportedTypes) {
				// TODO: This needs more work accross all steps
			}

			// now that assemblies are in the graph, need to mark entry assemblies.
			foreach (TypeDefinition type in assembly.MainModule.Types) {
				MarkEntireType (type, new DependencyInfo (DependencyKind.TypeInAssembly, assembly));
			}
			// PROBLEM! by the time we get here, the type might already have been marked for another reason.
			// mark it instead as a dependency of the assembly.
		}

		void ProcessModule (AssemblyDefinition assembly)
		{
			// Pre-mark <Module> if there is any methods as they need to be executed 
			// at assembly load time
			foreach (TypeDefinition type in assembly.MainModule.Types)
			{
				if (type.Name == "<Module>" && type.HasMethods)
				{
					MarkType (type, new DependencyInfo (DependencyKind.TypeInAssembly, assembly));
					break;
				}
			}
		}

		bool ProcessLazyAttributes ()
		{
			if (Annotations.HasMarkedAnyIndirectlyCalledMethods () && MarkDisablePrivateReflectionAttribute ())
				return true;

			var startingQueueCount = _assemblyLevelAttributes.Count;
			if (startingQueueCount == 0)
				return false;

			var skippedItems = new List<AttributeProviderPair> ();
			var markOccurred = false;

			while (_assemblyLevelAttributes.Count != 0) {
				var assemblyLevelAttribute = _assemblyLevelAttributes.Dequeue ();
				var customAttribute = assemblyLevelAttribute.Attribute;

				var resolved = customAttribute.Constructor.Resolve ();
				if (resolved == null) {
					HandleUnresolvedMethod (customAttribute.Constructor);
					continue;
				}

				if (!ShouldMarkTopLevelCustomAttribute (assemblyLevelAttribute, resolved)) {
					skippedItems.Add (assemblyLevelAttribute);
					continue;
				}

				string attributeFullName = customAttribute.Constructor.DeclaringType.FullName;
				switch (attributeFullName) {
				case "System.Diagnostics.DebuggerDisplayAttribute":
					MarkTypeWithDebuggerDisplayAttribute (GetDebuggerAttributeTargetType (assemblyLevelAttribute.Attribute, (AssemblyDefinition) assemblyLevelAttribute.Provider), customAttribute);
					break;
				case "System.Diagnostics.DebuggerTypeProxyAttribute":
					MarkTypeWithDebuggerTypeProxyAttribute (GetDebuggerAttributeTargetType (assemblyLevelAttribute.Attribute, (AssemblyDefinition) assemblyLevelAttribute.Provider), customAttribute);
					break;
				}

				markOccurred = true;
				MarkCustomAttribute (customAttribute, new DependencyInfo (DependencyKind.AssemblyOrModuleCustomAttribute, assemblyLevelAttribute.Provider));
			}

			// requeue the items we skipped in case we need to make another pass
			foreach (var item in skippedItems)
				_assemblyLevelAttributes.Enqueue (item);

			return markOccurred;
		}

		bool ProcessLateMarkedAttributes ()
		{
			var startingQueueCount = _lateMarkedAttributes.Count;
			if (startingQueueCount == 0)
				return false;

			var skippedItems = new List<(AttributeProviderPair, DependencyInfo)> ();
			var markOccurred = false;

			while (_lateMarkedAttributes.Count != 0) {
				var (attributeProviderPair, reason) = _lateMarkedAttributes.Dequeue ();
				var customAttribute = attributeProviderPair.Attribute;

				var resolved = customAttribute.Constructor.Resolve ();
				if (resolved == null) {
					HandleUnresolvedMethod (customAttribute.Constructor);
					continue;
				}

				if (!ShouldMarkCustomAttribute (customAttribute, attributeProviderPair.Provider)) {
					skippedItems.Add ((attributeProviderPair, reason));
					continue;
				}

				markOccurred = true;
				MarkCustomAttribute (customAttribute, reason);
				MarkSpecialCustomAttributeDependencies (customAttribute);
			}

			// requeue the items we skipped in case we need to make another pass
			foreach (var item in skippedItems)
				_lateMarkedAttributes.Enqueue (item);

			return markOccurred;
		}

		protected void MarkField (FieldReference reference, DependencyInfo reason)
		{


			if (reference.DeclaringType is GenericInstanceType) {
				// Console.WriteLine("marking generic field ref " + reference.ToString());
				// with an additional reason of marking the type.
				// this is necessary because the fieldref may have a generic instance type
				// which is different from the type of the resolved field.
				// we want to make sure that the generic parameters of this type get marked.
				// blame the resolved field, not the generic instance (which is never marked on its own)
				if (reference.Resolve () != null) {
					switch (reason.Kind) {
					case DependencyKind.FieldAccess:
					case DependencyKind.Ldtoken:
						// expect that we can get a generic fieldref from these instructions.
						// what does resolving a field do?
						// field with generic type? only allowed if type parameter is on theh type.
						// unlike methods.
						break;
					default:
						// but I don't think other things can produce fieldrefs to fields on generic instances.
						throw new NotImplementedException("weird");
					}

					// need to blame this field ref on the original reason (without actually marking it)
					_context.MarkingHelpers.MarkFieldOnGenericInstance (reference, reason);
					// need to blame this field ref.
					MarkType (reference.DeclaringType, new DependencyInfo (DependencyKind.DeclaringTypeOfField, reference));
					// but the MarkField of the def needs to be blamed on this ref, not the original reason.
					reason = new DependencyInfo (DependencyKind.FieldOnGenericInstance, reference);

				} else {
					throw new Exception("what to do here?");
					// used to MarkType (reference.DeclaringType)...
					// if the field ref is a generic type, 
					//MarkType (reference.DeclaringType); // no reason, hopefully uncommon.
				}

				// BUT: problem is this field itself won't actually get marked.
				// only its resolved one will.

				// TODO:
				// what to do when we mark things as result of a generic instantiation,
				// when the instantiation doesn't exist as a definition, but only as a reference (and therefore doesn't get "marked" per-se)?
				// unknown so far.
				// should never mark GenericInstType.
			}
			FieldDefinition field = reference.Resolve ();

			if (field == null) {
				HandleUnresolvedField (reference);
				return;
			}

			MarkField (field, reason);
		}

		// Mark* methods should have the semantics that they ultimately
		// call Annotations.Mark, and they always record some kind of dependency using Recorder.
		// they can additionally have logic that is only ever done once per method.
		// which is Process*.

		void MarkField (FieldDefinition field, DependencyInfo reason)
		{
			if (CheckProcessed (field))
				return;

			MarkType (field.DeclaringType, new DependencyInfo (DependencyKind.DeclaringTypeOfField, field));
			MarkType (field.FieldType, new DependencyInfo (DependencyKind.FieldType, field));
			MarkCustomAttributes (field, new DependencyInfo (DependencyKind.CustomAttribute, field));
			MarkMarshalSpec (field, new DependencyInfo (DependencyKind.FieldMarshalSpec, field));
			DoAdditionalFieldProcessing (field);

			var parent = field.DeclaringType;
			if (!Annotations.HasPreservedStaticCtor (parent))
				switch (reason.Kind) {
				case DependencyKind.FieldAccess:
					var methodAccessingField = reason.Source;
					MarkStaticConstructor (parent, new DependencyInfo (DependencyKind.TriggersCctorThroughFieldAccess, methodAccessingField));
					break;
				case DependencyKind.EntryField:
				case DependencyKind.FieldForType:
				case DependencyKind.FieldPreservedForType:
				case DependencyKind.InteropMethodDependency:
				case DependencyKind.FieldReferencedByAttribute:
				case DependencyKind.Ldtoken:
				case DependencyKind.FieldOnGenericInstance:
				case DependencyKind.EventSourceProviderField:
					// generic: mark cctor for this field if we don't have a better reason.
					MarkStaticConstructor (parent, new DependencyInfo (DependencyKind.CctorForField, field));
					break;
				default:
					throw new NotImplementedException (reason.Kind.ToString());
				}

			if (Annotations.HasSubstitutedInit (field)) {
				Annotations.SetPreservedStaticCtor (parent);
				Annotations.SetSubstitutedInit (parent);
			}

			switch (reason.Kind) {
			case DependencyKind.FieldAccess:
				// field was accessed from a method.
				// let's record it for now, but not sure how to report it.
				Annotations.MarkFieldAccessFromMethod ((MethodDefinition)reason.Source, field);
				break;
			case DependencyKind.FieldForType:
			case DependencyKind.FieldPreservedForType:
			case DependencyKind.InteropMethodDependency:
			case DependencyKind.FieldReferencedByAttribute:
			case DependencyKind.Ldtoken:
			case DependencyKind.FieldOnGenericInstance:
			case DependencyKind.EventSourceProviderField:
				Annotations.MarkFieldWithReason (reason, field);
				break;
			case DependencyKind.EntryField:
				Annotations.MarkEntryField (field);
				break;
			default:
				throw new NotImplementedException (reason.Kind.ToString());
			}
		}

		protected virtual bool IgnoreScope (IMetadataScope scope)
		{
			AssemblyDefinition assembly = ResolveAssembly (scope);
			return Annotations.GetAction (assembly) != AssemblyAction.Link;
		}

		void MarkScope (IMetadataScope scope, TypeDefinition type)
		{
			// scope is an AssemblyNameReference, or ModuleReference, or ModuleDefinition.
			// 
			// if (scope is IMetadataTokenProvider provider)
			// 	Annotations.Mark (provider);
			Annotations.MarkScopeOfType (type, scope);
		}

		protected virtual void MarkSerializable (TypeDefinition type)
		{
			MarkDefaultConstructor (type, new DependencyInfo (DependencyKind.SerializationMethodForType, type));
			if (!_context.IsFeatureExcluded ("deserialization"))
				MarkMethodsIf (type.Methods, IsSpecialSerializationConstructor, new DependencyInfo (DependencyKind.SerializationMethodForType, type));
		}
		protected virtual TypeDefinition MarkType (TypeReference reference, DependencyInfo reason)
		{
			if (reference == null)
				return null;

			// mark any generic parameters for the same reason
			// that we mark the generic instantiation itself.
			(reference, reason) = GetOriginalType (reference, reason);

			if (reference is FunctionPointerType)
				return null;

			if (reference is GenericParameter)
				return null;

//			if (IgnoreScope (reference.Scope))
//				return null;

			TypeDefinition type = reference.Resolve ();

			if (type == null) {
				HandleUnresolvedType (reference);
				return null;
			}

			switch (reason.Kind) {
			case DependencyKind.EntryType:
				// we don't report a specific reason for an entry type.
				// can get here for INitializeAssembly,
				// or for xml/root types.
				// can we assert that it was maybe already marked???
				// TODO
				if (!Annotations.IsMarked (type)) {
					throw new Exception("WAT");
				}
				break;
			case DependencyKind.BaseType:
			case DependencyKind.DeclaringTypeOfField:
			case DependencyKind.DeclaringTypeOfType:
			case DependencyKind.FieldType:
			case DependencyKind.GenericArgumentType: // generic instantiation typeref -> argument type
			case DependencyKind.DeclaringTypeOfMethod:
			case DependencyKind.InterfaceImplementationInterfaceType:
			case DependencyKind.GenericParameterConstraintType:
			case DependencyKind.TypeReferencedByAttribute:
			case DependencyKind.ParameterType:
			case DependencyKind.ReturnType:
			case DependencyKind.VariableType:
			case DependencyKind.IsInst:
			case DependencyKind.NewArr:
			case DependencyKind.Ldtoken:
			case DependencyKind.CatchType:
			case DependencyKind.CustomAttributeArgumentType:
			case DependencyKind.CustomAttributeArgumentValue:
			case DependencyKind.UnreachableBodyRequirement:
			case DependencyKind.DeclaringTypeOfCalledMethod:
			case DependencyKind.ElementType: // instantiation -> resolved type
			case DependencyKind.ModifierType: // volatile string -> system.volatile
			case DependencyKind.AttributeType:
			case DependencyKind.TypeAccessedViaReflection:
				Annotations.MarkTypeWithReason (reason, type);
				break;
			case DependencyKind.LinkerInternal:
				Annotations.MarkTypeLinkerInternal (type);
				break;
			// since we can get here for generic arguments of methods, all
			// the "method" dependencies need to be supported as well.
			case DependencyKind.DirectCall:
			case DependencyKind.VirtualCall:
			case DependencyKind.Ldftn:
				throw new Exception("shouldn't blame a typedef of generic method arg on the method's caller, but on generic method itself!");
			default:
				throw new NotImplementedException(reason.Kind.ToString());
			}

			if (type.HasMethods) {
				if (ShouldMarkTypeStaticConstructor (type)) {
					switch (reason.Kind) {
					case DependencyKind.DeclaringTypeOfCalledMethod:
						MarkStaticConstructor (type, new DependencyInfo (DependencyKind.TriggersCctorForCalledMethod, reason.Source));
						break;
					case DependencyKind.BaseType:
					case DependencyKind.DeclaringTypeOfField:
					case DependencyKind.DeclaringTypeOfType:
					case DependencyKind.FieldType:
					case DependencyKind.GenericArgumentType:
					case DependencyKind.DeclaringTypeOfMethod:
					case DependencyKind.InterfaceImplementationInterfaceType:
					case DependencyKind.GenericParameterConstraintType:
					case DependencyKind.TypeReferencedByAttribute:
					case DependencyKind.ParameterType:
					case DependencyKind.ReturnType:
					case DependencyKind.VariableType:
					case DependencyKind.IsInst:
					case DependencyKind.NewArr:
					case DependencyKind.Ldtoken:
					case DependencyKind.CatchType:
					case DependencyKind.CustomAttributeArgumentType:
					case DependencyKind.CustomAttributeArgumentValue:
					case DependencyKind.UnreachableBodyRequirement:
					// DeclaringTypeOfCalledMethod?
					// ElementType?
					// ModifierType?
					case DependencyKind.EntryType:
					case DependencyKind.ElementType:
					case DependencyKind.AttributeType:
					case DependencyKind.TypeAccessedViaReflection:
						// for entrytype, we don't have a reason the type is kept, beyond the user said so.
						// maybe EntryType can be an intermediate reason that a method is kept.
						// we can track an entry kind that is:
						// xml, resolvefromassemblystep, or entrypoint
						// then we can optionally add extra tracing to show why the linker kept something
						// as opposed to just why we thought it was callable at runtime.
						MarkStaticConstructor (type, new DependencyInfo (DependencyKind.CctorForType, type));
						break;
					// if we get here for a method...
					case DependencyKind.DirectCall:
					case DependencyKind.VirtualCall:
					case DependencyKind.Ldftn:
						throw new Exception("blocked by above");
					default:
						// we also mark a type's cctor if the type is marked for any other reason, even if the cctor may not have been
						// called at runtime. if parameters or return type mark a type, if called with null it might still not trigger cctor
						// but for now, we still mark it. just consider it an untracked cctor in that case.
						// meaning it has an untracked reason for inclusion, possibly in addition to a real reason.
						// so it's not really untracked, but for a non-understood reason.
						throw new NotImplementedException(reason.Kind.ToString());

					}
				}
			}

			if (CheckProcessed (type))
				return null;

			Tracer.Push (type);

			MarkScope (type.Scope, type);
MarkType (type.BaseType, new DependencyInfo (DependencyKind.BaseType, type));
MarkType (type.DeclaringType, new DependencyInfo (DependencyKind.DeclaringTypeOfType, type));
MarkCustomAttributes (type, new DependencyInfo (DependencyKind.CustomAttribute, type));
MarkSecurityDeclarations (type, new DependencyInfo (DependencyKind.CustomAttribute, type));

			if (type.IsMulticastDelegate ()) {
				MarkMulticastDelegate (type);
			}

			if (type.IsSerializable ())
				MarkSerializable (type);

			// this has *some* logic for EventSource... but I think it's incomplete.
			// it marks all static fields of Keywords/OpCodes/Tasks subclasses of an EventSource-derived type.
			// don't we also need to keep the EventSource attribute (which gives the source name?)
			// other logic keeps public&instance&property methods on types with EventDataAttribute.
			// I don't think this is enough.
			if (!_context.IsFeatureExcluded ("etw") && BCL.EventTracingForWindows.IsEventSourceImplementation (type, _context)) {
				MarkEventSourceProviders (type);
			}

			MarkTypeSpecialCustomAttributes (type);

			MarkGenericParameterProvider (type);

			// keep fields for value-types and for classes with LayoutKind.Sequential or Explicit
			if (type.IsValueType || !type.IsAutoLayout)
				MarkFields (type, includeStatic: type.IsEnum, reason: new DependencyInfo (DependencyKind.FieldForType, type));

			// There are a number of markings we can defer until later when we know it's possible a reference type could be instantiated
			// For example, if no instance of a type exist, then we don't need to mark the interfaces on that type
			// However, for some other types there is no benefit to deferring
			if (type.IsInterface) {
				// There's no benefit to deferring processing of an interface type until we know a type implementing that interface is marked
				MarkRequirementsForInstantiatedTypes (type, new DependencyInfo (DependencyKind.InstantiatedInterface)); // no source
			} else if (type.IsValueType) {
				// Note : Technically interfaces could be removed from value types in some of the same cases as reference types, however, it's harder to know when
				// a value type instance could exist.  You'd have to track initobj and maybe locals types.  Going to punt for now.
				MarkRequirementsForInstantiatedTypes (type, new DependencyInfo (DependencyKind.InstantiatedValueType)); // no source
			} else if (IsFullyPreserved (type)) {
				// Here for a couple reasons:
				// * Edge case to cover a scenario where a type has preserve all, implements interfaces, but does not have any instance ctors.
				//    Normally TypePreserve.All would cause an instance ctor to be marked and that would in turn lead to MarkInterfaceImplementations being called
				//    Without an instance ctor, MarkInterfaceImplementations is not called and then TypePreserve.All isn't truly respected.
				// * If an assembly has the action Copy and had ResolveFromAssemblyStep ran for the assembly, then InitializeType will have led us here
				//    When the entire assembly is preserved, then all interfaces, base, etc will be preserved on the type, so we need to make sure
				//    all of these types are marked.  For example, if an interface implementation is of a type in another assembly that is linked,
				//    and there are no other usages of that interface type, then we need to make sure the interface type is still marked because
				//    this type is going to retain the interface implementation
				MarkRequirementsForInstantiatedTypes (type, new DependencyInfo (DependencyKind.InstantiatedFullyPreservedType)); // no source
			} else if (AlwaysMarkTypeAsInstantiated (type)) {
				// TODO: could just use untracked reasons for these instant
				MarkRequirementsForInstantiatedTypes (type, new DependencyInfo (DependencyKind.AlwaysInstantiatedType)); // no source
			}

			if (type.HasInterfaces)
				_typesWithInterfaces.Add (type);

			if (type.HasMethods) {
				// recursively checks base methods. if any are from abstract class in a non-link assembly,
				// it keeps methods that are overrides of these.
				// TODO: ambiguous what we should blame.
				// could blame declaring type, or could blame the base method with preserved scope.
				// for now, let's blame the declaring type.
				MarkMethodsIf (type.Methods, IsVirtualNeededByTypeDueToPreservedScope, new DependencyInfo (DependencyKind.VirtualNeededDueToPreservedScope, type));

				if (_context.IsFeatureExcluded ("deserialization"))
					MarkMethodsIf (type.Methods, HasOnSerializeAttribute, new DependencyInfo (DependencyKind.SerializationMethodForType, type));
				else
					MarkMethodsIf (type.Methods, HasOnSerializeOrDeserializeAttribute, new DependencyInfo (DependencyKind.SerializationMethodForType, type));
			}

			DoAdditionalTypeProcessing (type);

			Tracer.Pop ();

			ApplyPreserveInfo (type);

			return type;
		}

		// Allow subclassers to mark additional things in the main processing loop
		protected virtual void DoAdditionalProcessing ()
		{
		}

		// Allow subclassers to mark additional things
		protected virtual void DoAdditionalTypeProcessing (TypeDefinition type)
		{
		}
		
		// Allow subclassers to mark additional things
		protected virtual void DoAdditionalFieldProcessing (FieldDefinition field)
		{
		}

		// Allow subclassers to mark additional things
		protected virtual void DoAdditionalPropertyProcessing (PropertyDefinition property)
		{
		}

		// Allow subclassers to mark additional things
		protected virtual void DoAdditionalEventProcessing (EventDefinition evt)
		{
		}

		// Allow subclassers to mark additional things
		protected virtual void DoAdditionalInstantiatedTypeProcessing (TypeDefinition type)
		{
		}

		void MarkAssemblyCustomAttributes (AssemblyDefinition assembly)
		{
			if (!assembly.HasCustomAttributes)
				return;

			foreach (CustomAttribute attribute in assembly.CustomAttributes) {
				// the linker doesn't currently mark an assembly other than by marking its mainmodule,
				// which is used to check if the assembly is marked.
				// therefore we blame the assembly-level attributes on the main module as well,
				// even though they are technically on the assembly, not the main module.
				// this is likely safe in a world without multi-module assemblies.
				// the provider is definitely the assembly though...
				// let's fix this elsewhere.
				_assemblyLevelAttributes.Enqueue (new AttributeProviderPair (attribute, assembly));
			}
		}

		TypeDefinition GetDebuggerAttributeTargetType (CustomAttribute ca, AssemblyDefinition asm)
		{
			TypeReference targetTypeReference = null;
			foreach (var property in ca.Properties) {
				if (property.Name == "Target") {
					targetTypeReference = (TypeReference) property.Argument.Value;
					break;
				}

				if (property.Name == "TargetTypeName") {
					if (TypeNameParser.TryParseTypeAssemblyQualifiedName ((string) property.Argument.Value, out string typeName, out string assemblyName)) {
						if (string.IsNullOrEmpty (assemblyName))
							targetTypeReference = asm.MainModule.GetType (typeName);
						else
							targetTypeReference = _context.GetAssemblies ().FirstOrDefault (a => a.Name.Name == assemblyName)?.MainModule.GetType (typeName);
					}
					break;
				}
			}

			return targetTypeReference?.Resolve ();
		}
		
		void MarkTypeSpecialCustomAttributes (TypeDefinition type)
		{
			if (!type.HasCustomAttributes)
				return;

			foreach (CustomAttribute attribute in type.CustomAttributes) {
				var attrType = attribute.Constructor.DeclaringType;
				switch (attrType.Name) {
				case "XmlSchemaProviderAttribute" when attrType.Namespace == "System.Xml.Serialization":
					MarkXmlSchemaProvider (type, attribute);
					break;
				case "DebuggerDisplayAttribute" when attrType.Namespace == "System.Diagnostics":
					MarkTypeWithDebuggerDisplayAttribute (type, attribute);
					break;
				case "DebuggerTypeProxyAttribute" when attrType.Namespace == "System.Diagnostics":
					MarkTypeWithDebuggerTypeProxyAttribute (type, attribute);
					break;
				case "EventDataAttribute" when attrType.Namespace == "System.Diagnostics.Tracing":
					MarkMethodsIf (type.Methods, MethodDefinitionExtensions.IsPublicInstancePropertyMethod, new DependencyInfo (DependencyKind.MethodReferencedByAttribute, type));
					break;
				case "TypeDescriptionProviderAttribute" when attrType.Namespace == "System.ComponentModel":
					MarkTypeConverterLikeDependency (attribute, l => l.IsDefaultConstructor ());
					break;
				}
			}
		}

		//
		// Used for known framework attributes which can be applied to any element
		//
		bool MarkSpecialCustomAttributeDependencies (CustomAttribute ca)
		{
			var dt = ca.Constructor.DeclaringType;
			if (dt.Name == "TypeConverterAttribute" && dt.Namespace == "System.ComponentModel") {
				MarkTypeConverterLikeDependency (ca, l =>
					l.IsDefaultConstructor () ||
					l.Parameters.Count == 1 && l.Parameters [0].ParameterType.IsTypeOf ("System", "Type"));
				return true;
			}

			return false;
		}

		void MarkMethodSpecialCustomAttributes (MethodDefinition method)
		{
			if (!method.HasCustomAttributes)
				return;

			foreach (CustomAttribute attribute in method.CustomAttributes) {
				switch (attribute.Constructor.DeclaringType.FullName) {
				case "System.Web.Services.Protocols.SoapHeaderAttribute":
					MarkSoapHeader (method, attribute);
					break;
				}
			}
		}

		void MarkXmlSchemaProvider (TypeDefinition type, CustomAttribute attribute)
		{
			if (TryGetStringArgument (attribute, out string name))
				MarkNamedMethod (type, name, new DependencyInfo (DependencyKind.MethodReferencedByAttribute, attribute));
		}

		protected virtual void MarkTypeConverterLikeDependency (CustomAttribute attribute, Func<MethodDefinition, bool> predicate)
		{
			var args = attribute.ConstructorArguments;
			if (args.Count < 1)
				return;

			TypeDefinition tdef = null;
			switch (attribute.ConstructorArguments [0].Value) {
			case string s:
				tdef = ResolveFullyQualifiedTypeName (s);
				break;
			case TypeReference type:
				tdef = type.Resolve ();
				break;
			}

			if (tdef == null)
				return;

			MarkMethodsIf (tdef.Methods, predicate, new DependencyInfo (DependencyKind.MethodReferencedByAttribute, attribute));
		}

		void MarkTypeWithDebuggerDisplayAttribute (TypeDefinition type, CustomAttribute attribute)
		{
			if (_context.KeepMembersForDebugger) {

				string displayString = (string) attribute.ConstructorArguments[0].Value;

				Regex regex = new Regex ("{[^{}]+}", RegexOptions.Compiled);

				foreach (Match match in regex.Matches (displayString)) {
					// Remove '{' and '}'
					string realMatch = match.Value.Substring (1, match.Value.Length - 2);

					// Remove ",nq" suffix if present
					// (it asks the expression evaluator to remove the quotes when displaying the final value)
					if (Regex.IsMatch(realMatch, @".+,\s*nq")) {
						realMatch = realMatch.Substring (0, realMatch.LastIndexOf (','));
					}

					if (realMatch.EndsWith ("()")) {
						string methodName = realMatch.Substring (0, realMatch.Length - 2);
						MethodDefinition method = GetMethodWithNoParameters (type, methodName);
						if (method != null) {
							MarkMethod (method, new DependencyInfo (DependencyKind.MethodReferencedByAttribute, attribute));
							continue;
						}
					} else {
						FieldDefinition field = GetField (type, realMatch);
						if (field != null) {
							// we keep DDA fields without necessarily keeping the attribute. mark it as an entry.
							_context.MarkingHelpers.MarkEntryField (field, new EntryInfo (EntryKind.UnmarkedAttributeDependency, attribute, field));
							continue;
						}

						PropertyDefinition property = GetProperty (type, realMatch);
						if (property != null) {
							if (property.GetMethod != null) {
								MarkMethod (property.GetMethod, new DependencyInfo (DependencyKind.MethodReferencedByAttribute, attribute));

							}
							if (property.SetMethod != null) {
								MarkMethod (property.SetMethod, new DependencyInfo (DependencyKind.MethodReferencedByAttribute, attribute));
							}
							continue;
						}
					}

					// oh.. this is if we don't match any members explicitly.
					while (type != null) {
						_context.LogMessage("warning: non-understood DebuggerDisplayAttribute: " + attribute.ToString());
						MarkMethods (type, new DependencyInfo (DependencyKind.MethodKeptForNonUnderstoodAttribute, attribute));
						MarkFields (type, includeStatic: true, new DependencyInfo (DependencyKind.FieldReferencedByAttribute, attribute));
						// this seems like it will miss generic parameters.
						type = type.BaseType?.Resolve ();
					}
					return;
				}
			}
		}

		void MarkTypeWithDebuggerTypeProxyAttribute (TypeDefinition type, CustomAttribute attribute)
		{
			if (_context.KeepMembersForDebugger) {
				object constructorArgument = attribute.ConstructorArguments[0].Value;
				TypeReference proxyTypeReference = constructorArgument as TypeReference;
				if (proxyTypeReference == null) {
					if (constructorArgument is string proxyTypeReferenceString) {
						proxyTypeReference = type.Module.GetType (proxyTypeReferenceString, runtimeName: true);
					}
				}

				if (proxyTypeReference == null) {
					return;
				}

				MarkType (proxyTypeReference, new DependencyInfo (DependencyKind.TypeReferencedByAttribute, attribute));

				TypeDefinition proxyType = proxyTypeReference.Resolve ();
				if (proxyType != null) {
					MarkMethods (proxyType, new DependencyInfo (DependencyKind.MethodReferencedByAttribute, attribute));
					MarkFields (proxyType, includeStatic: true, new DependencyInfo (DependencyKind.FieldReferencedByAttribute, attribute));
				}
			}
		}

		static bool TryGetStringArgument (CustomAttribute attribute, out string argument)
		{
			argument = null;

			if (attribute.ConstructorArguments.Count < 1)
				return false;

			argument = attribute.ConstructorArguments [0].Value as string;

			return argument != null;
		}

		protected int MarkNamedMethod (TypeDefinition type, string method_name, DependencyInfo reason)
		{
			if (!type.HasMethods)
				return 0;

			int count = 0;
			foreach (MethodDefinition method in type.Methods) {
				if (method.Name != method_name)
					continue;

				MarkMethod (method, reason);
				count++;
			}

			return count;
		}

		void MarkSoapHeader (MethodDefinition method, CustomAttribute attribute)
		{
			if (!TryGetStringArgument (attribute, out string member_name))
				return;

			MarkNamedField (method.DeclaringType, member_name, new DependencyInfo (DependencyKind.FieldReferencedByAttribute, attribute));
			MarkNamedProperty (method.DeclaringType, member_name, new DependencyInfo (DependencyKind.MethodReferencedByAttribute, attribute));
		}

		// TODO: combine with MarkDependencyField.
		void MarkNamedField (TypeDefinition type, string field_name, DependencyInfo reason)
		{
			if (!type.HasFields)
				return;

			foreach (FieldDefinition field in type.Fields) {
				if (field.Name != field_name)
					continue;

				MarkField (field, reason);
			}
		}

		void MarkNamedProperty (TypeDefinition type, string property_name, DependencyInfo reason)
		{
			if (!type.HasProperties)
				return;

			foreach (PropertyDefinition property in type.Properties) {
				if (property.Name != property_name)
					continue;

				Tracer.Push (property);
				MarkMethod (property.GetMethod, reason);
				MarkMethod (property.SetMethod, reason);
				Tracer.Pop ();
			}
		}

		void MarkInterfaceImplementations (TypeDefinition type)
		{
			if (!type.HasInterfaces)
				return;

			foreach (var iface in type.Interfaces) {
				// Only mark interface implementations of interface types that have been marked.
				// This enables stripping of interfaces that are never used
				var resolvedInterfaceType = iface.InterfaceType.Resolve ();
				if (resolvedInterfaceType == null) {
					HandleUnresolvedType (iface.InterfaceType);
					continue;
				}
				
				if (ShouldMarkInterfaceImplementation (type, iface, resolvedInterfaceType))
					MarkInterfaceImplementation (iface, type);
			}
		}

		void MarkGenericParameterProvider (IGenericParameterProvider provider)
		{
			if (!provider.HasGenericParameters)
				return;

			foreach (GenericParameter parameter in provider.GenericParameters)
				MarkGenericParameter (parameter);
		}

		void MarkGenericParameter (GenericParameter parameter)
		{
			MarkCustomAttributes (parameter, new DependencyInfo (DependencyKind.GenericParameterCustomAttribute, parameter.Owner));
			if (!parameter.HasConstraints)
				return;

			foreach (var constraint in parameter.Constraints) {
				MarkCustomAttributes (constraint, new DependencyInfo (DependencyKind.GenericParameterConstraintCustomAttribute, parameter.Owner));
				MarkType (constraint.ConstraintType, new DependencyInfo (DependencyKind.GenericParameterConstraintType, parameter.Owner));
			}
		}

		bool IsVirtualNeededByTypeDueToPreservedScope (MethodDefinition method)
		{
			if (!method.IsVirtual)
				return false;

			var base_list = Annotations.GetBaseMethods (method);
			if (base_list == null)
				return false;

			foreach (MethodDefinition @base in base_list) {
				// Just because the type is marked does not mean we need interface methods.
				// if the type is never instantiated, interfaces will be removed
				if (@base.DeclaringType.IsInterface)
					continue;
				
				// If the type is marked, we need to keep overrides of abstract members defined in assemblies
				// that are copied.  However, if the base method is virtual, then we don't need to keep the override
				// until the type could be instantiated
				if (!@base.IsAbstract)
					continue;

				if (IgnoreScope (@base.DeclaringType.Scope))
					return true;

				if (IsVirtualNeededByTypeDueToPreservedScope (@base))
					return true;
			}

			return false;
		}
		
		bool IsVirtualNeededByInstantiatedTypeDueToPreservedScope (MethodDefinition method)
		{
			if (!method.IsVirtual)
				return false;

			var base_list = Annotations.GetBaseMethods (method);
			if (base_list == null)
				return false;

			foreach (MethodDefinition @base in base_list) {
				// this check happens, even if the base declaringtype is an interface.
				// meaning that if an instantiated type implements an interface
				// from a copy assembly, we keep the interface methods.
				// TODO: what about interface sweeping?
				if (IgnoreScope (@base.DeclaringType.Scope))
					return true;

				// what if the instantiated type derives from another type
				// that in turn derives from a type in a copy assembly?
				// all's fine if it's not an interface - the logic is the same except for interfaces.
				// but if A < B < Interface, this check here
				// will NOT keep interface implementations in A if they override
				// an interface. maybe this should be passed to
				// IsVirtualNeededByInstantiatedTypeDueToPreservedScope???
				if (IsVirtualNeededByTypeDueToPreservedScope (@base))
					return true;
			}

			return false;
		}

		// TODO:
		// clean this up. we shouldn't mark ISerializable-related ctor
		// if it doesn't implement ISerializable... should we?
		// conceptually this is part of the ISerializable interface.
		// for now, just mark as untracked dependency.
		static bool IsSpecialSerializationConstructor (MethodDefinition method)
		{
			if (!method.IsInstanceConstructor ())
				return false;

			var parameters = method.Parameters;
			if (parameters.Count != 2)
				return false;

			return parameters [0].ParameterType.Name == "SerializationInfo" &&
				parameters [1].ParameterType.Name == "StreamingContext";
		}

		protected void MarkMethodsIf (Collection<MethodDefinition> methods, Func<MethodDefinition, bool> predicate, DependencyInfo reason)
		{
			foreach (MethodDefinition method in methods)
				if (predicate (method))
					MarkMethod (method, reason);
		}

		protected MethodDefinition MarkMethodIf (Collection<MethodDefinition> methods, Func<MethodDefinition, bool> predicate, DependencyInfo reason)
		{
			foreach (MethodDefinition method in methods) {
				if (predicate (method)) {
					return MarkMethod (method, reason);
				}
			}

			return null;
		}

		protected bool MarkDefaultConstructor (TypeDefinition type, DependencyInfo reason)
		{
			if (type?.HasMethods != true)
				return false;

			return MarkMethodIf (type.Methods, MethodDefinitionExtensions.IsDefaultConstructor, reason) != null;
		}

		static bool IsNonEmptyStaticConstructor (MethodDefinition method)
		{
			if (!method.IsStaticConstructor ())
				return false;

			if (!method.HasBody || !method.IsIL)
				return true;

			if (method.Body.CodeSize != 1)
				return true;

			return method.Body.Instructions [0].OpCode.Code != Code.Ret;
		}

		static bool HasOnSerializeAttribute (MethodDefinition method)
		{
			if (!method.HasCustomAttributes)
				return false;
			foreach (var ca in method.CustomAttributes) {
				var cat = ca.AttributeType;
				if (cat.Namespace != "System.Runtime.Serialization")
					continue;
				switch (cat.Name) {
				case "OnSerializedAttribute":
				case "OnSerializingAttribute":
					return true;
				}
			}
			return false;
		}

		static bool HasOnSerializeOrDeserializeAttribute (MethodDefinition method)
		{
			if (!method.HasCustomAttributes)
				return false;
			foreach (var ca in method.CustomAttributes) {
				var cat = ca.AttributeType;
				if (cat.Namespace != "System.Runtime.Serialization")
					continue;
				switch (cat.Name) {
				case "OnDeserializedAttribute":
				case "OnDeserializingAttribute":
				case "OnSerializedAttribute":
				case "OnSerializingAttribute":
					return true;
				}
			}
			return false;
		}

		protected virtual bool AlwaysMarkTypeAsInstantiated (TypeDefinition td)
		{
			switch (td.Name) {
				// These types are created from native code which means we are unable to track when they are instantiated
				// Since these are such foundational types, let's take the easy route and just always assume an instance of one of these
				// could exist
				case "Delegate":
				case "MulticastDelegate":
				case "ValueType":
				case "Enum":
					return td.Namespace == "System";
			}

			return false;
		}

		void MarkEventSourceProviders (TypeDefinition td)
		{
			// marks all static fields of a nestedtype in an EventSource-derived type,
			// that is called
			// "Keywords", "Tasks", or "Opcodes";
			foreach (var nestedType in td.NestedTypes) {
				if (BCL.EventTracingForWindows.IsProviderName (nestedType.Name))
					MarkStaticFields (nestedType, new DependencyInfo (DependencyKind.EventSourceProviderField, td));
			}
		}

		protected virtual void MarkMulticastDelegate (TypeDefinition type)
		{
			MarkMethodCollection (type.Methods, new DependencyInfo (DependencyKind.MethodForSpecialType, type));
		}

		TypeDefinition ResolveFullyQualifiedTypeName (string name)
		{
			if (!TypeNameParser.TryParseTypeAssemblyQualifiedName (name, out string typeName, out string assemblyName))
				return null;

			foreach (var assemblyDefinition in _context.GetAssemblies ()) {
				if (assemblyName != null && assemblyDefinition.Name.Name != assemblyName)
					continue;

				var foundType = assemblyDefinition.MainModule.GetType (typeName);
				if (foundType == null)
					continue;

				return foundType;
			}

			return null;
		}

		protected (TypeReference, DependencyInfo) GetOriginalType (TypeReference type, DependencyInfo reason)
		{
			// why is this a while loop?
			while (type is TypeSpecification specification) {
				_context.MarkingHelpers.MarkTypeSpec (specification, reason);
				if (type is GenericInstanceType git) {
					// record an edge from whatever got here to the typeref. then this call will do from ref -> argument.
					MarkGenericArguments (git);
					if (git.ElementType is TypeSpecification) {
						throw new Exception("HUH?");
					}
				}

				if (type is IModifierType mod) {
					// similarly, the modified type is never marked.
					// we blame the reason that the modified type was marked,
					// for moth the modifier type and the type that was modified.
					MarkModifierType (mod);
				}


				// something needs to mark the fnptr.
				if (type is FunctionPointerType fnptr) {
					MarkParameters (fnptr);
					MarkType (fnptr.ReturnType, new DependencyInfo (DependencyKind.ReturnType, fnptr));
					break; // FunctionPointerType is the original type
				}

				// for T<F>, this is T<>.
				// for arrays, I'm guessing this is the array's element type, not the array type constructor:
				// S[] -> S. not S[] -> []`1

				// at this point, we will have an edge from the tyespec to the generic arguments
				// but we still need one from the originator to the typespec,
				// and from the typespec to the element type.
				// the element type will be marked in MarkType, so just pass along a new reason.
				// the originator -> typespec must be handled here.
				(type, reason) = (specification.ElementType, new DependencyInfo (DependencyKind.ElementType, specification));
			}

			return (type, reason);
		}

		void MarkParameters (FunctionPointerType fnptr)
		{
			if (!fnptr.HasParameters)
				return;

			for (int i = 0; i < fnptr.Parameters.Count; i++)
			{
				MarkType (fnptr.Parameters[i].ParameterType, new DependencyInfo (DependencyKind.ParameterType, fnptr));
			}
		}

		void MarkModifierType (IModifierType mod)
		{
			// marking the modifier type...
			MarkType (mod.ModifierType, new DependencyInfo (DependencyKind.ModifierType, mod));
		}

		void MarkGenericArguments (IGenericInstance instance)
		{
			foreach (TypeReference argument in instance.GenericArguments) {
				// generic inst should never be marked. so generic arg is not a valid reason.
				// how do we track that a generic argument of a base type is kept?
				// it could be anything - generic arg of declaring type
				// generic arg of return type
				// generic arg of parameter type
				// what do we do here!?
				// forget about generics.
				// could mark it as generic argument of the uninstantiated generic?
				// but then everything that uses a generic instantiation would lead to all possible parameters...
				// which isn't good.
				// the linker is smarter than that.
				// so, blame the immediate "callsite", not the virtual that everyone uses.
				// there IS a direct path.
				MarkType (argument, new DependencyInfo (DependencyKind.GenericArgumentType, instance));
			}
			// problem: the instance is a typeref, not a typedef necessarily.
			// maybe that's OK! just try it. :)

			MarkGenericArgumentConstructors (instance);
		}

		void MarkGenericArgumentConstructors (IGenericInstance instance)
		{
			var arguments = instance.GenericArguments;

			var generic_element = GetGenericProviderFromInstance (instance);
			if (generic_element == null)
				return;

			var parameters = generic_element.GenericParameters;

			if (arguments.Count != parameters.Count)
				return;

			for (int i = 0; i < arguments.Count; i++) {
				var argument = arguments [i];
				var parameter = parameters [i];

				if (!parameter.HasDefaultConstructorConstraint)
					continue;

				var argument_definition = argument.Resolve ();
				// this will have kind generic argument constructor
				MarkDefaultConstructor (argument_definition, new DependencyInfo (DependencyKind.DefaultCtorForNewConstrainedGenericArgument, instance));
			}
		}

		static IGenericParameterProvider GetGenericProviderFromInstance (IGenericInstance instance)
		{
			if (instance is GenericInstanceMethod method)
				return method.ElementMethod.Resolve ();

			if (instance is GenericInstanceType type)
				return type.ElementType.Resolve ();

			return null;
		}

		void ApplyPreserveInfo (TypeDefinition type)
		{
			ApplyPreserveMethods (type);

			if (!Annotations.TryGetPreserve (type, out TypePreserve preserve))
				return;

			switch (preserve) {
			case TypePreserve.All:
				MarkFields (type, true, new DependencyInfo (DependencyKind.FieldPreservedForType, type));
				// types are preserved by XML if they don't have any child nodes
				// PreserveAll can get here too.
				// preserveall on a type won't necessarily keep nested types.
				// preserveall on assembly will.
				MarkMethods (type, new DependencyInfo (DependencyKind.MethodPreservedForType, type));
				break;
			case TypePreserve.Fields:
				if (!MarkFields (type, true, new DependencyInfo (DependencyKind.FieldPreservedForType, type), true))
					_context.LogMessage ($"Type {type.FullName} has no fields to preserve");
				break;
			case TypePreserve.Methods:
				if (!MarkMethods (type, new DependencyInfo (DependencyKind.MethodPreservedForType, type)))
					_context.LogMessage ($"Type {type.FullName} has no methods to preserve");
				break;
			}
		}

		void ApplyPreserveMethods (TypeDefinition type)
		{
			var list = Annotations.GetPreservedMethods (type);
			if (list == null)
				return;

			// this doesn't happen. but if it does, just note that this was preserved for "type".
			MarkMethodCollection (list, new DependencyInfo (DependencyKind.PreservedMethod, type));
		}

		void ApplyPreserveMethods (MethodDefinition method)
		{
			var list = Annotations.GetPreservedMethods (method);
			if (list == null)
				return;

			// this simply doesn't happen. what to do here?
			MarkMethodCollection (list, new DependencyInfo (DependencyKind.PreservedMethod, method));
		}

		// if is enum, we include static fields of value type.
		protected bool MarkFields (TypeDefinition type, bool includeStatic, DependencyInfo reason, bool markBackingFieldsOnlyIfPropertyMarked = false)
		{
			if (!type.HasFields)
				return false;

			foreach (FieldDefinition field in type.Fields) {
				if (!includeStatic && field.IsStatic)
					continue;

				if (markBackingFieldsOnlyIfPropertyMarked && field.Name.EndsWith (">k__BackingField", StringComparison.Ordinal)) {
					// We can't reliably construct the expected property name from the backing field name for all compilers
					// because csc shortens the name of the backing field in some cases
					// For example:
					// Field Name = <IFoo<int>.Bar>k__BackingField
					// Property Name = IFoo<System.Int32>.Bar
					//
					// instead we will search the properties and find the one that makes use of the current backing field
					var propertyDefinition = SearchPropertiesForMatchingFieldDefinition (field);
					if (propertyDefinition != null && !Annotations.IsMarked (propertyDefinition))
						continue;
				}
				MarkField (field, reason);
			}

			return true;
		}

		static PropertyDefinition SearchPropertiesForMatchingFieldDefinition (FieldDefinition field)
		{
			foreach (var property in field.DeclaringType.Properties) {
				var instr = property.GetMethod?.Body?.Instructions;
				if (instr == null)
					continue;

				foreach (var ins in instr) {
					if (ins?.Operand == field)
						return property;
				}
			}

			return null;
		}

		protected void MarkStaticFields (TypeDefinition type, DependencyInfo reason)
		{
			if (!type.HasFields)
				return;

			// used to mark all static fields of a type who
			foreach (FieldDefinition field in type.Fields) {
				if (field.IsStatic)
					MarkField (field, reason);
			}
		}

		protected virtual bool MarkMethods (TypeDefinition type, DependencyInfo reason)
		{
			if (!type.HasMethods)
				return false;

			MarkMethodCollection (type.Methods, reason);
			return true;
		}

		void MarkMethodCollection (IList<MethodDefinition> methods, DependencyInfo reason)
		{
			foreach (MethodDefinition method in methods)
				MarkMethod (method, reason);
		}

		protected void MarkIndirectlyCalledMethod (MethodDefinition method, DependencyInfo reason)
		{
			MarkMethod (method, reason);
			Annotations.MarkIndirectlyCalledMethod (method);
		}

		protected virtual MethodDefinition MarkMethod (MethodReference reference, DependencyInfo reason)
		{
			if (reference.ToString().Contains("Testgenerics")) {
				Console.WriteLine("here");
			}
			if (reference.ToString() == "System.Void rr.GenericClass`1<rr.GenericArg>::.ctor()") {
				Console.WriteLine("hmm");
			}

			// if it's a generic method, this will mark generic arguments, and the method reference.
			// the reference will go to the ElementType, which I think is the method on the type, without the
			// method generic argument.
			// the reason becomes a ElementMethod dependency from the original reference to the element type
			// (which is the uninstantiated generic methodref, still possibly on a genericinst type).
			(reference, reason) = GetOriginalMethod (reference, reason);

			// but the method could still be on a generic type.

			if (reference.DeclaringType is ArrayType)
				return null;

			Tracer.Push (reference);

			if (reference.DeclaringType is GenericInstanceType) {
				// for a generic instance, the reference to the instantiation doesn't exist
				// as a definition. there's nothing to mark.
				// yet the generic instantiation will pull in other marked things.
				// and these need a reason.
				// both the generic type and the arguments will get marked.
				// the declaring type will exist.
				// so that's fine... problem is that the source method reference might not exist.
				// resolving to its definition removes the generic stuff.
				// so maybe we need to resolve it before doing this?
				// TODO: what if the source is null?
				// we'll get an error! :)
				// if we can't resolve the original method,
				// then there's no reason to blame it.
				// same as field logic.

				// if it's a methodref on a generic instance type, we mark the declaring type (a reference) as this methodref's
				// declaring type.
				// but that puts the methodref in the graph, no?

				// need to make sure the reference has a reason to be in the graph, even though it's not actually marked.
				_context.MarkingHelpers.MarkMethodOnGenericInstance (reference, reason);
				// this will put the methodref in the graph, as a source.
				// but it will never be there with a reason.
				MarkType (reference.DeclaringType, new DependencyInfo (DependencyKind.DeclaringTypeOfMethod, reference));
				// want to mark the resolved method definition as a dependency of the reference.
				reason = new DependencyInfo (DependencyKind.MethodOnGenericInstance, reference);
			}

			MethodDefinition method = reference.Resolve ();
			// ... wait, what if it's a methodimpl? does this include generic method parameters?

//			if (IgnoreScope (reference.DeclaringType.Scope))
//				return;

			try {
				if (method == null) {
					HandleUnresolvedMethod (reference);
					return null;
				}

				if (Annotations.GetAction (method) == MethodAction.Nothing)
					Annotations.SetAction (method, MethodAction.Parse);

				EnqueueMethod (method, reason);
			} finally {
				Tracer.Pop ();
			}
			Tracer.AddDependency (method);

			return method;
		}

		AssemblyDefinition ResolveAssembly (IMetadataScope scope)
		{
			AssemblyDefinition assembly = _context.Resolve (scope);
			if (!Annotations.IsProcessed (assembly)) {
				// oh, can get here for example when early processing one assembly,
				// when it references another that hasn't been marked yet.
			}
			MarkAssembly (assembly);
			return assembly;
		}

		protected (MethodReference, DependencyInfo) GetOriginalMethod (MethodReference method, DependencyInfo reason)
		{
			while (method is MethodSpecification specification) {
				if (method is GenericInstanceMethod gim) {
					// TODO: this needs to be updated!
					MarkGenericArguments (gim);
				}

				// MarkMethod is going to now blame the methodspec.
				// we need to add an edge from the originator of the methodspec to it.
				_context.MarkingHelpers.MarkMethodSpec (specification, reason);
				(method, reason) = (specification.ElementMethod, new DependencyInfo (DependencyKind.ElementMethod, specification));
				if (method is MethodSpecification) {
					throw new Exception("huh?");
				}
			}

			return (method, reason);
		}

		protected virtual void ProcessMethod (MethodDefinition method, DependencyInfo reason)
		{
			// note the method call, even if we have already processed it.

			if (method.ToString().Contains("Testgenerics")) {
				Console.WriteLine("here");
			}

			// problem:
			// some logic (what to mark it for, incoming edge) must be done for every caller.
			// some must run only once per definition.
			// we want to "mark" the method body as dangerous only once.
			// but we call Annotations.Mark on the method every time, currently.
			// what to do?
			// we mark for inclusion before.
			// once it's marked, it's in the graph.

			// TODO: replace this with a reason!
			// need to mark the method call EVEN if we have already processed the method!
			switch (reason.Kind) {
			case DependencyKind.DirectCall:
				Annotations.MarkMethodCall ((MethodDefinition)reason.Source, method);
				break;
			case DependencyKind.VirtualCall:
				Annotations.MarkVirtualMethodCall ((MethodDefinition)reason.Source, method);
				break;
			case DependencyKind.TriggersCctorThroughFieldAccess:
				Annotations.MarkTriggersStaticConstructorThroughFieldAccess ((MethodDefinition)reason.Source, method);
				break;
			case DependencyKind.TriggersCctorForCalledMethod:
				Annotations.MarkTriggersStaticConstructorForCalledMethod ((MethodDefinition)reason.Source, method);
				break;
			case DependencyKind.CctorForField:
				Annotations.MarkStaticConstructorForField ((FieldDefinition)reason.Source, method);
				break;
			case DependencyKind.EntryMethod:
				// don't track an entry reason. if we got here, there is already an entry reason.
				// just mark the NODE as an entry node, without a particular reason for being an entry node.
				// don't say UNKNOWN.
				// just ASSERT that the method already has an entry reason.
				// and mark it as an entry node in the annotations.
				Annotations.MarkEntryMethod (method);
				break;
			case DependencyKind.OverrideOnInstantiatedType:
				Annotations.MarkMethodOverrideOnInstantiatedType ((TypeDefinition)reason.Source, method);
				break;
			case DependencyKind.Override:
				Annotations.MarkOverride ((MethodDefinition)reason.Source, method);
				break;
			case DependencyKind.MethodAccessedViaReflection:
				Annotations.MarkMethodAccessedViaReflection ((MethodDefinition)reason.Source, method);
				break;
			case DependencyKind.MethodReferencedByAttribute:
			case DependencyKind.MethodKeptForNonUnderstoodAttribute:
			case DependencyKind.SerializationMethodForType:
			case DependencyKind.MethodPreservedForType:
			case DependencyKind.MethodForSpecialType:
			case DependencyKind.BaseMethod:
			case DependencyKind.MethodImplOverride:
			case DependencyKind.Ldvirtftn:
			case DependencyKind.Ldftn:
			case DependencyKind.Ldtoken:
			case DependencyKind.CctorForType:
			case DependencyKind.MethodForInstantiatedType:
			case DependencyKind.InteropMethodDependency:
			case DependencyKind.EventAccessedViaReflection:
			case DependencyKind.EventOfEventMethod: // todo: prevent this dependency kind!
			case DependencyKind.EventOfType:
			case DependencyKind.UnreachableBodyRequirement:
			case DependencyKind.AttributeConstructor:
			case DependencyKind.AttributeProperty:
			case DependencyKind.VirtualNeededDueToPreservedScope:
			case DependencyKind.PreserveDependency:
			case DependencyKind.MethodForType:
			case DependencyKind.ElementMethod:
			case DependencyKind.EventMethod:
			case DependencyKind.DefaultCtorForNewConstrainedGenericArgument:
			case DependencyKind.MethodOnGenericInstance: // marks the methoddef, blaming a methodref on a generic instance
				Annotations.MarkMethodWithReason (reason, method);
				break;
			default:
				throw new NotSupportedException ("don't yet support the reason kind " + reason.Kind);
			}

			Tracer.Push (method);
			// marktype
			switch (reason.Kind) {
			case DependencyKind.DirectCall:
			case DependencyKind.VirtualCall:
				MarkType (method.DeclaringType, new DependencyInfo (DependencyKind.DeclaringTypeOfCalledMethod, method));
				break;
			case DependencyKind.TriggersCctorThroughFieldAccess:
			case DependencyKind.TriggersCctorForCalledMethod:
			case DependencyKind.Override:
			case DependencyKind.OverrideOnInstantiatedType: // in this case, the declaring type would already have been marked anyway.
			case DependencyKind.MethodAccessedViaReflection: // this should behave similarly to the declaringtypeofcalledmethod which may trigger a cctor.
			case DependencyKind.EntryMethod:
			case DependencyKind.SerializationMethodForType:
			case DependencyKind.MethodReferencedByAttribute:
			case DependencyKind.MethodKeptForNonUnderstoodAttribute:
			case DependencyKind.MethodPreservedForType:
			case DependencyKind.BaseMethod:
			case DependencyKind.CctorForField:
			case DependencyKind.MethodForSpecialType:
			case DependencyKind.MethodImplOverride:
			case DependencyKind.Ldvirtftn:
			case DependencyKind.Ldftn:
			case DependencyKind.Ldtoken:
			case DependencyKind.CctorForType:
			case DependencyKind.MethodForInstantiatedType:
			case DependencyKind.InteropMethodDependency:
			case DependencyKind.EventAccessedViaReflection:
			case DependencyKind.EventOfEventMethod: // todo: prevent this dependency kind!
			case DependencyKind.EventOfType:
			case DependencyKind.UnreachableBodyRequirement:
			case DependencyKind.AttributeConstructor:
			case DependencyKind.AttributeProperty:
			case DependencyKind.VirtualNeededDueToPreservedScope:
			case DependencyKind.PreserveDependency:
			case DependencyKind.MethodForType:
			case DependencyKind.ElementMethod:
			case DependencyKind.EventMethod:
			case DependencyKind.DefaultCtorForNewConstrainedGenericArgument:
			case DependencyKind.MethodOnGenericInstance:
				MarkType (method.DeclaringType, new DependencyInfo (DependencyKind.DeclaringTypeOfMethod, method));
				break;
			default:
				throw new NotImplementedException (reason.Kind.ToString());
			}

			if (CheckProcessed (method)) {
				Tracer.Pop ();
				return;
			}


			Tracer.Push (method);

			// problem: if type is first marked for some other reason...
			// then we might not get here because of the CheckProcessed check.

			MarkCustomAttributes (method, new DependencyInfo (DependencyKind.CustomAttribute, method));
			MarkSecurityDeclarations (method, new DependencyInfo (DependencyKind.CustomAttribute, method));

			MarkGenericParameterProvider (method);

			if (method.IsInstanceConstructor ())
				MarkRequirementsForInstantiatedTypes (method.DeclaringType, new DependencyInfo (DependencyKind.ConstructedType, method));

			if (method.IsConstructor) {
				if (!Annotations.ProcessSatelliteAssemblies && KnownMembers.IsSatelliteAssemblyMarker (method))
					Annotations.ProcessSatelliteAssemblies = true;
			} else if (method.IsPropertyMethod ())
				MarkProperty (method.GetProperty (), new DependencyInfo (DependencyKind.PropertyOfPropertyMethod, method));
			else if (method.IsEventMethod ())
				MarkEvent (method.GetEvent (), new DependencyInfo (DependencyKind.EventOfEventMethod, method));

			if (method.HasParameters) {
				foreach (ParameterDefinition pd in method.Parameters) {
					MarkType (pd.ParameterType, new DependencyInfo (DependencyKind.ParameterType, method));
					// blame the same reason that the method was marked.
					MarkCustomAttributes (pd, new DependencyInfo (DependencyKind.ParameterAttribute, method));
					MarkMarshalSpec (pd, new DependencyInfo (DependencyKind.ParameterMarshalSpec, method));
				}
			}

			if (method.HasOverrides) {
				foreach (MethodReference ov in method.Overrides) {
					MarkMethod (ov, new DependencyInfo (DependencyKind.MethodImplOverride, method));
					MarkExplicitInterfaceImplementation (method, ov);
				}
			}

			MarkMethodSpecialCustomAttributes (method);

			if (method.IsVirtual)
				_virtual_methods.Add (method);

			MarkNewCodeDependencies (method);

			MarkBaseMethods (method);

			MarkType (method.ReturnType, new DependencyInfo (DependencyKind.ReturnType, method));
			MarkCustomAttributes (method.MethodReturnType, new DependencyInfo (DependencyKind.ReturnTypeAttribute, method));
			MarkMarshalSpec (method.MethodReturnType, new DependencyInfo (DependencyKind.ReturnTypeMarshalSpec, method));

			if (method.IsPInvokeImpl || method.IsInternalCall) {
				ProcessInteropMethod (method);
			}

			if (ShouldParseMethodBody (method))
				MarkMethodBody (method.Body);

			DoAdditionalMethodProcessing (method);

			ApplyPreserveMethods (method);
			Tracer.Pop ();
		}

		// Allow subclassers to mark additional things when marking a method
		protected virtual void DoAdditionalMethodProcessing (MethodDefinition method)
		{
		}

		protected virtual void MarkRequirementsForInstantiatedTypes (TypeDefinition type, DependencyInfo reason)
		{
			if (Annotations.IsInstantiated (type))
				return;

			// switch (reason) {
			// case DependencyKind.NewObj:
			// 	Annotations.MarkInstantiatedByCalledConstructor ((MethodDefinition)reason.source, type);
			// 	break;
			// 	Annotations.MarkInstantiatedUntracked (type);
			// 	break;
			// default:
			// 	throw new NotImplementedException (reason.kind);
			// }
			// actually, don't track whether a type is instantiated in the graph.
			// just track method -> method dependency from the ctor to the method.
//			Annotations.MarkInstantiated (type);
			switch (reason.Kind) {
			case DependencyKind.ConstructedType:
				// the type being marked will have a reason it was marked, and a reason it was instantiated.
				// record whichever path is shorter.
				// call to cctor -> instantiated -> overrides
				Annotations.MarkInstantiatedByConstructor ((MethodDefinition)reason.Source, type);
				// TODO: if we don't track instantiations separately, the cctor -> type dependency
				// will be the same for instantiations as it is for the declaringtype of the marked cctor.
				// maybe that's not a problem - but it can result in
				// overrides of instantiated types getting blamed on types, and then not citing the instantiation
				// reason but instead just citing the type's mark reason.
				// instantiated implies marked (is stronger than marked)
				// any instantiation will also be marked.
				// instantiations add extra dependencies on top of marked types.
				// we should show those extra dependencies as resulting from whatever
				// caused the type to actually be instantiated.
				// some types always get those dependencies, whether or not they are instantiated.
				// so for some types, instantiation means the same thing as just being marked.
				// we should not treat those cases specially, and instead show the instantiation reqs as
				// coming from the type being marked.
				// for types that aren't always instantiated:
				// some dependencies are:
				//   only when instantiated: 
				//   whenever marked:
				//   instantiated or marked.
				break;
			case DependencyKind.InstantiatedInterface: // same as below...
			case DependencyKind.InstantiatedValueType: // no edge necessary (marktype took care of it)
			case DependencyKind.InstantiatedFullyPreservedType: // same
			case DependencyKind.AlwaysInstantiatedType: // same
				// this is the only untracked dependency we use. this is because the dependency kinds above
				// just specify that certain types are always considered instantiated.
				// we will simply record their overrides as being on the types.
				// we don't track the instantiation sites.
				Annotations.MarkInstantiatedUntracked (type);
				break;
			default:
				throw new NotImplementedException (reason.Kind.ToString ());
			}

			MarkInterfaceImplementations (type);

			foreach (var method in GetRequiredMethodsForInstantiatedType (type))
				MarkMethod (method, new DependencyInfo (DependencyKind.MethodForInstantiatedType, type));

			DoAdditionalInstantiatedTypeProcessing (type);
		}

		/// <summary>
		/// Collect methods that must be marked once a type is determined to be instantiated.
		///
		/// This method is virtual in order to give derived mark steps an opportunity to modify the collection of methods that are needed 
		/// </summary>
		/// <param name="type"></param>
		/// <returns></returns>
		protected virtual IEnumerable<MethodDefinition> GetRequiredMethodsForInstantiatedType (TypeDefinition type)
		{
			foreach (var method in type.Methods) {
				if (method.IsFinalizer () || IsVirtualNeededByInstantiatedTypeDueToPreservedScope (method))
					yield return method;
			}
		}

		void MarkExplicitInterfaceImplementation (MethodDefinition method, MethodReference ov)
		{
			var resolvedOverride = ov.Resolve ();
			
			if (resolvedOverride == null) {
				HandleUnresolvedMethod (ov);
				return;
			}

			if (resolvedOverride.DeclaringType.IsInterface) {
				foreach (var ifaceImpl in method.DeclaringType.Interfaces) {
					var resolvedInterfaceType = ifaceImpl.InterfaceType.Resolve ();
					if (resolvedInterfaceType == null) {
						HandleUnresolvedType (ifaceImpl.InterfaceType);
						continue;
					}

					if (resolvedInterfaceType == resolvedOverride.DeclaringType) {
						MarkInterfaceImplementation (ifaceImpl, method.DeclaringType);
						return;
					}
				}
			}
		}

		void MarkNewCodeDependencies (MethodDefinition method)
		{
			switch (Annotations.GetAction (method)) {
			case MethodAction.ConvertToStub:
				if (!method.IsInstanceConstructor ())
					return;

				var baseType = method.DeclaringType.BaseType.Resolve ();
				// what if this is generic???
				if (!MarkDefaultConstructor (baseType, new DependencyInfo (DependencyKind.BaseDefaultCtorForStubbedMethod, method)))
					throw new NotSupportedException ($"Cannot stub constructor on '{method.DeclaringType}' when base type does not have default constructor");

				break;

			case MethodAction.ConvertToThrow:
						MarkAndCacheConvertToThrowExceptionCtor (new DependencyInfo (DependencyKind.UnreachableBodyRequirement, method));
				break;
			}
		}

		protected virtual void MarkAndCacheConvertToThrowExceptionCtor (DependencyInfo reason)
		{
			if (_context.MarkedKnownMembers.NotSupportedExceptionCtorString != null)
				return;

			var nse = BCL.FindPredefinedType ("System", "NotSupportedException", _context);
			if (nse == null)
				throw new NotSupportedException ("Missing predefined 'System.NotSupportedException' type");

			MarkType (nse, reason);

			var nseCtor = MarkMethodIf (nse.Methods, KnownMembers.IsNotSupportedExceptionCtorString, reason);
			_context.MarkedKnownMembers.NotSupportedExceptionCtorString = nseCtor ?? throw new MarkException ($"Could not find constructor on '{nse.FullName}'");

			var objectType = BCL.FindPredefinedType ("System", "Object", _context);
			if (objectType == null)
				throw new NotSupportedException ("Missing predefined 'System.Object' type");

			MarkType (objectType, reason);

			var objectCtor = MarkMethodIf (objectType.Methods, MethodDefinitionExtensions.IsDefaultConstructor, reason);
			_context.MarkedKnownMembers.ObjectCtor = objectCtor ?? throw new MarkException ($"Could not find constructor on '{objectType.FullName}'");
		}

		bool MarkDisablePrivateReflectionAttribute ()
		{
			if (_context.MarkedKnownMembers.DisablePrivateReflectionAttributeCtor != null)
				return false;

			var disablePrivateReflection = BCL.FindPredefinedType ("System.Runtime.CompilerServices", "DisablePrivateReflectionAttribute", _context);
			if (disablePrivateReflection == null)
				throw new NotSupportedException ("Missing predefined 'System.Runtime.CompilerServices.DisablePrivateReflectionAttribute' type");

			MarkType (disablePrivateReflection, new DependencyInfo (DependencyKind.LinkerInternal));

			var ctor = MarkMethodIf (disablePrivateReflection.Methods, MethodDefinitionExtensions.IsDefaultConstructor, new DependencyInfo (DependencyKind.DisablePrivateReflectionDependency, disablePrivateReflection));
			_context.MarkedKnownMembers.DisablePrivateReflectionAttributeCtor = ctor ?? throw new MarkException ($"Could not find constructor on '{disablePrivateReflection.FullName}'");
			return true;
		}

		void MarkBaseMethods (MethodDefinition method)
		{
			var base_methods = Annotations.GetBaseMethods (method);
			if (base_methods == null)
				return;

			foreach (MethodDefinition base_method in base_methods) {
				if (base_method.DeclaringType.IsInterface && !method.DeclaringType.IsInterface)
					continue;

				MarkMethod (base_method, new DependencyInfo (DependencyKind.BaseMethod, method));
				MarkBaseMethods (base_method);
			}
		}

		void ProcessInteropMethod(MethodDefinition method)
		{
			TypeDefinition returnTypeDefinition = method.ReturnType.Resolve ();

			const bool includeStaticFields = false;
			if (returnTypeDefinition != null && !returnTypeDefinition.IsImport) {
				MarkDefaultConstructor (returnTypeDefinition, new DependencyInfo (DependencyKind.InteropMethodDependency, method));
				MarkFields (returnTypeDefinition, includeStaticFields, new DependencyInfo (DependencyKind.InteropMethodDependency, method));
			}

			if (method.HasThis && !method.DeclaringType.IsImport) {
				MarkFields (method.DeclaringType, includeStaticFields, new DependencyInfo (DependencyKind.InteropMethodDependency, method));
			}

			foreach (ParameterDefinition pd in method.Parameters) {
				TypeReference paramTypeReference = pd.ParameterType;
				if (paramTypeReference is TypeSpecification) {
					paramTypeReference = (paramTypeReference as TypeSpecification).ElementType;
				}
				TypeDefinition paramTypeDefinition = paramTypeReference.Resolve ();
				if (paramTypeDefinition != null && !paramTypeDefinition.IsImport) {
					MarkFields (paramTypeDefinition, includeStaticFields, new DependencyInfo (DependencyKind.InteropMethodDependency, method));
					if (pd.ParameterType.IsByReference) {
						MarkDefaultConstructor (paramTypeDefinition, new DependencyInfo (DependencyKind.InteropMethodDependency, method));
					}
				}
			}
		}

		protected virtual bool ShouldParseMethodBody (MethodDefinition method)
		{
			if (!method.HasBody)
				return false;

			switch (Annotations.GetAction (method)) {
			case MethodAction.ForceParse:
				return true;
			case MethodAction.Parse:
				AssemblyDefinition assembly = ResolveAssembly (method.DeclaringType.Scope);
				switch (Annotations.GetAction (assembly)) {
				case AssemblyAction.Link:
				case AssemblyAction.Copy:
				case AssemblyAction.CopyUsed:
				case AssemblyAction.AddBypassNGen:
				case AssemblyAction.AddBypassNGenUsed:
					return true;
				default:
					return false;
				}
			default:
				return false;
			}
		}

		protected void MarkProperty (PropertyDefinition prop, DependencyInfo reason)
		{
			// put properties into the graph, even though we don't mark them separately.
			_context.MarkingHelpers.MarkProperty (prop, reason);
			// TODO: isn't it a bug that this doesn't keep property methods???
			MarkCustomAttributes (prop, new DependencyInfo (DependencyKind.CustomAttribute, prop));
			DoAdditionalPropertyProcessing (prop);
		}

		// TODO: why not handle propertie and events the same way?
		protected virtual void MarkEvent (EventDefinition evt, DependencyInfo reason)
		{
			_context.MarkingHelpers.MarkEvent (evt, reason);
			MarkCustomAttributes (evt, new DependencyInfo (DependencyKind.CustomAttribute, evt));
			MarkMethodIfNotNull (evt.AddMethod, new DependencyInfo (DependencyKind.EventMethod, evt));
			MarkMethodIfNotNull (evt.InvokeMethod, new DependencyInfo (DependencyKind.EventMethod, evt));
			MarkMethodIfNotNull (evt.RemoveMethod, new DependencyInfo (DependencyKind.EventMethod, evt));
			DoAdditionalEventProcessing (evt);
		}

		void MarkMethodIfNotNull (MethodReference method, DependencyInfo reason)
		{
			if (method == null)
				return;

			MarkMethod (method, reason);
		}

		// may be called multiple times. can exit early, if it's unreachable.
		// gets parsed if forceparse, or it's parse and link/copy, etc.
		// and the method is marked.
		protected virtual void MarkMethodBody (MethodBody body)
		{
			if (_context.IsOptimizationEnabled (CodeOptimizations.UnreachableBodies, body.Method) && IsUnreachableBody (body)) {
				MarkAndCacheConvertToThrowExceptionCtor (new DependencyInfo (DependencyKind.UnreachableBodyRequirement, body.Method));
				_unreachableBodies.Add (body);
				return;
			}

			foreach (VariableDefinition var in body.Variables)
				MarkType (var.VariableType, new DependencyInfo (DependencyKind.VariableType, body.Method));

			foreach (ExceptionHandler eh in body.ExceptionHandlers)
				if (eh.HandlerType == ExceptionHandlerType.Catch)
					MarkType (eh.CatchType, new DependencyInfo (DependencyKind.CatchType, body.Method));

			// we get here for MarkMethodBody whenever it's reachable.
			// with unreachablebodies, that means it's static or instantiated or not worth converting to throw
			foreach (Instruction instruction in body.Instructions)
				MarkInstruction (instruction, body.Method);

			// interfaces needed by body stack
			MarkInterfacesNeededByBodyStack (body);

			MarkReflectionLikeDependencies (body);

			PostMarkMethodBody (body);
		}

		bool IsUnreachableBody (MethodBody body)
		{
			return !body.Method.IsStatic
				&& !Annotations.IsInstantiated (body.Method.DeclaringType)
				&& MethodBodyScanner.IsWorthConvertingToThrow (body);
		}
		

		partial void PostMarkMethodBody (MethodBody body);

		void MarkInterfacesNeededByBodyStack (MethodBody body)
		{
			// If a type could be on the stack in the body and an interface it implements could be on the stack on the body
			// then we need to mark that interface implementation.  When this occurs it is not safe to remove the interface implementation from the type
			// even if the type is never instantiated
			var implementations = MethodBodyScanner.GetReferencedInterfaces (_context.Annotations, body);
			if (implementations == null)
				return;

			// this may not 
			foreach (var (implementation, type) in implementations)
				MarkInterfaceImplementation (implementation, type);
		}

		// the "reason" we mark an instruction is that we decided to parse the body, and it's reachable (if the unreachable bodies opt is enabled)
		protected virtual void MarkInstruction (Instruction instruction, MethodDefinition method)
		{
			switch (instruction.OpCode.OperandType) {
			case OperandType.InlineField:
				MarkField ((FieldReference) instruction.Operand, new DependencyInfo (DependencyKind.FieldAccess, method));
				break;
			case OperandType.InlineMethod:
				DependencyInfo reason;
				switch (instruction.OpCode.Code) {
				case Code.Jmp:
				case Code.Call:
				case Code.Newobj:
					reason = new DependencyInfo (DependencyKind.DirectCall, method);
					break;
				case Code.Callvirt:
					reason = new DependencyInfo (DependencyKind.VirtualCall, method);
					break;
				case Code.Ldvirtftn:
					reason = new DependencyInfo (DependencyKind.Ldvirtftn, method);
					break;
				case Code.Ldftn:
					reason = new DependencyInfo (DependencyKind.Ldftn, method);
					break;
				default:
					throw new Exception("what instruction is this?!");
				}
				MarkMethod ((MethodReference) instruction.Operand, reason);
				break;
			case OperandType.InlineTok:
				// only ldtoken takes an inlinetok.
				object token = instruction.Operand;
				if (instruction.OpCode.Code != Code.Ldtoken) {
					throw new Exception("unexpected instruction " + instruction.OpCode);
				}
				if (token is TypeReference typeReference)
					MarkType (typeReference, new DependencyInfo (DependencyKind.Ldtoken, method));
				else if (token is MethodReference methodReference)
					// TODO: is inlinetok guaranteed to be a call?
					// NO! it's a ldtoken.
					MarkMethod (methodReference, new DependencyInfo (DependencyKind.Ldtoken, method));
				else
					MarkField ((FieldReference) token, new DependencyInfo (DependencyKind.Ldtoken, method));
				break;
			case OperandType.InlineType:
				if (instruction.OpCode.Code == Code.Isinst) {
					MarkType ((TypeReference) instruction.Operand, new DependencyInfo (DependencyKind.IsInst, method));
				} else if (instruction.OpCode.Code == Code.Newarr) {
					MarkType ((TypeReference) instruction.Operand, new DependencyInfo (DependencyKind.NewArr, method));
				}
				break;
			default:
				break;
			}
		}

		protected virtual void HandleUnresolvedType (TypeReference reference)
		{
			if (!_context.IgnoreUnresolved) {
				throw new ResolutionException (reference);
			}
		}

		protected virtual void HandleUnresolvedMethod (MethodReference reference)
		{
			if (!_context.IgnoreUnresolved) {
				throw new ResolutionException (reference);
			}
		}

		protected virtual void HandleUnresolvedField (FieldReference reference)
		{
			if (!_context.IgnoreUnresolved) {
				throw new ResolutionException (reference);
			}
		}

		protected virtual bool ShouldMarkInterfaceImplementation (TypeDefinition type, InterfaceImplementation iface, TypeDefinition resolvedInterfaceType)
		{
			if (Annotations.IsMarked (iface))
				return false;

			if (Annotations.IsMarked (resolvedInterfaceType))
				return true;

			if (!_context.IsOptimizationEnabled (CodeOptimizations.UnusedInterfaces, type))
				return true;

			// It's hard to know if a com or windows runtime interface will be needed from managed code alone,
			// so as a precaution we will mark these interfaces once the type is instantiated
			if (resolvedInterfaceType.IsImport || resolvedInterfaceType.IsWindowsRuntime)
				return true;

			return IsFullyPreserved (type);
		}

		protected virtual void MarkInterfaceImplementation (InterfaceImplementation iface, TypeDefinition type)
		{
			// can get here from looking at interface impls on a type,
			// from looking at explicit overrides (methodimpls) on a method (signifying an interface implementation)
			// or even from a method body (where we mark interface implementations needed by the body stack)
			MarkCustomAttributes (iface, new DependencyInfo (DependencyKind.CustomAttribute, iface));
			MarkType (iface.InterfaceType, new DependencyInfo (DependencyKind.InterfaceImplementationInterfaceType, iface));
			// Annotations.Mark (iface); // TODO: this can be its own node!! need to track dependency from type & interface -> ifaciimpl
			Annotations.MarkInterfaceImplementation (iface, type);
		}

		bool HasManuallyTrackedDependency (MethodBody methodBody)
		{
			return PreserveDependencyLookupStep.HasPreserveDependencyAttribute (methodBody.Method);
		}

		//
		// Extension point for reflection logic handling customization
		//
		protected virtual bool ProcessReflectionDependency (MethodBody body, Instruction instruction)
		{
			return false;
		}

		//
		// Tries to mark additional dependencies used in reflection like calls (e.g. typeof (MyClass).GetField ("fname"))
		//
		protected virtual void MarkReflectionLikeDependencies (MethodBody body)
		{
			if (HasManuallyTrackedDependency (body))
				return;

			var instructions = body.Instructions;
			ReflectionPatternDetector detector = new ReflectionPatternDetector (this, body.Method);

			//
			// Starting at 1 because all patterns require at least 1 instruction backward lookup
			//
			for (var i = 1; i < instructions.Count; i++) {
				var instruction = instructions [i];

				if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
					continue;

				if (ProcessReflectionDependency (body, instruction))
					continue;

				if (!(instruction.Operand is MethodReference methodCalled))
					continue;

				var methodCalledDefinition = methodCalled.Resolve ();
				if (methodCalledDefinition == null)
					continue;

				ReflectionPatternContext reflectionContext = new ReflectionPatternContext (_context, body.Method, methodCalledDefinition, i);
				try {
					detector.Process (ref reflectionContext);
				}
				finally {
					reflectionContext.Dispose ();
				}
			}
		}

		/// <summary>
		/// Helper struct to pass around context information about reflection pattern
		/// as a single parameter (and have a way to extend this in the future if we need to easily).
		/// Also implements a simple validation mechanism to check that the code does report patter recognition
		/// results for all methods it works on.
		/// The promise of the pattern recorder is that for a given reflection method, it will either not talk
		/// about it ever, or it will always report recognized/unrecognized.
		/// </summary>
		struct ReflectionPatternContext : IDisposable
		{
			readonly LinkContext _context;
#if DEBUG
			bool _patternAnalysisAttempted;
			bool _patternReported;
#endif

			public MethodDefinition MethodCalling { get; private set; }
			public MethodDefinition MethodCalled { get; private set; }
			public int InstructionIndex { get; private set; }

			public ReflectionPatternContext (LinkContext context, MethodDefinition methodCalling, MethodDefinition methodCalled, int instructionIndex)
			{
				_context = context;
				MethodCalling = methodCalling;
				MethodCalled = methodCalled;
				InstructionIndex = instructionIndex;

#if DEBUG
				_patternAnalysisAttempted = false;
				_patternReported = false;
#endif
			}

			[Conditional("DEBUG")]
			public void AnalyzingPattern ()
			{
#if DEBUG
				_patternAnalysisAttempted = true;
#endif
			}

			public void RecordRecognizedPattern<T> (T accessedItem, Action mark)
				where T : IMemberDefinition
			{
#if DEBUG
				if (!_patternAnalysisAttempted)
					throw new InvalidOperationException ($"Internal error: To correctly report all patterns, when starting to analyze a pattern the AnalyzingPattern must be called first. {MethodCalling} -> {MethodCalled}");

				_patternReported = true;
#endif

				_context.Tracer.Push ($"Reflection-{accessedItem}");
				try {
					mark ();
					_context.ReflectionPatternRecorder.RecognizedReflectionAccessPattern (MethodCalling, MethodCalled, accessedItem);
				} finally {
					_context.Tracer.Pop ();
				}
			}

			public void RecordUnrecognizedPattern (string message)
			{
#if DEBUG
				if (!_patternAnalysisAttempted)
					throw new InvalidOperationException ($"Internal error: To correctly report all patterns, when starting to analyze a pattern the AnalyzingPattern must be called first. {MethodCalling} -> {MethodCalled}");

				_patternReported = true;
#endif

				_context.ReflectionPatternRecorder.UnrecognizedReflectionAccessPattern (MethodCalling, MethodCalled, message);

				// TODO: move this out to where the method is actually marked.
				// this marks the target as dangerous so it will show up
				// TODO: eventually, all the APIs that we even attempt to analyze will already be considered dangerous,
				// and we can get rid of this line.

				// _context.Annotations.MarkDangerousMethod (MethodCalled);
				// calling method should be marked dangerous, as it contains the dangerous callsite.

				// TODO: fix this bug.
				// marking it as dangerous is too late, because it has already been inserted into the graph.
				//_context.Annotations.MarkDangerousMethod (MethodCalling);
				// this sets up an edge to the dangerous reflection method.

				// marking ANYTHING as dangerous only shows up in the call graph
				// if it's marked dangerous BEFORE anything else references it.
				// otherwise the "dangerous" bit is not set.
				// and there's no way to set it in a collection.
				// options:
				// 1. only insert into the graph when we know.
				//    - if methods can be "dangerous", we must only insert them into the graph
				//      after we know whether they are dangerous.
				//      - if base APIs are "dangerous", only insert into graph after checking if it's a reflection API
				//      - if callsites are "dangerous", only insert a method into graph after processing its body
				//    - don't build graph until we know all of the properties.
				//      prevents building the graph incrementally
				// 2. allow mutating the graph in restricted ways as we go
				//    - 
				// _context.Annotations.MarkDangerousMethod (MethodCalling);
				// reflection data!
				// pass the dangerous data into this.
				// 

				// solution:
				// consider the "data" to be the union of all datas that reach this method.
				// where data INCLUDES a call chain suffix of length 1 (one callsite)
				// that means, that we track separately the possible data that can reach a dangerous method
				// for each direct callsite.
				// DATA = (callsite, possible values)
				// pass this data along to the analysis.
				// if any one is a dangerous value, ERROR about the GetType call for that callsite.
				// one unique error per dangerous datum.
				// it's a lattice of sets containing elements like (callsite, ppossible values)
				// not sure how the lattice factors over callsites and values
				// this says a dangerous data reached this callsite.
				// TODO: enhance unknown kind. for now, tracking everything as unknown.
				// eventually, should flow nicer errors to this.
				var reflectionData = new ReflectionData { kind = ReflectionDataKind.Unknown };

				_context.Annotations.MarkUnanalyzedReflectionCall (MethodCalling, MethodCalled, InstructionIndex, reflectionData);
			}

			public void Dispose ()
			{
#if DEBUG
				if (_patternAnalysisAttempted && !_patternReported)
					throw new InvalidOperationException ($"Internal error: A reflection pattern was analyzed, but no result was reported. {MethodCalling} -> {MethodCalled}");
#endif
			}
		}

		class ReflectionPatternDetector
		{
			readonly MarkStep _markStep;
			readonly MethodDefinition _methodCalling;
			readonly Collection<Instruction> _instructions;

			public ReflectionPatternDetector (MarkStep markStep, MethodDefinition callingMethod)
			{
				_markStep = markStep;
				_methodCalling = callingMethod;
				_instructions = _methodCalling.Body.Instructions;
			}

			public void Process (ref ReflectionPatternContext reflectionContext)
			{
				var methodCalled = reflectionContext.MethodCalled;
				var instructionIndex = reflectionContext.InstructionIndex;
				var methodCalledType = methodCalled.DeclaringType;

				switch (methodCalledType.Name) {
					//
					// System.Type
					//
					case "Type" when methodCalledType.Namespace == "System":

						// Some of the overloads are implemented by calling another overload of the same name.
						// These "internal" calls are not interesting to analyze, the outermost call is the one
						// which needs to be analyzed. The assumption is that all overloads have the same semantics.
						// (for example that all overload of GetConstructor if used require the specified type to have a .ctor).
						if (_methodCalling.DeclaringType == methodCalled.DeclaringType && _methodCalling.Name == methodCalled.Name)
							break;

						switch (methodCalled.Name) {
							//
							// GetConstructor (Type [])
							// GetConstructor (BindingFlags, Binder, Type [], ParameterModifier [])
							// GetConstructor (BindingFlags, Binder, CallingConventions, Type [], ParameterModifier [])
							//
							case "GetConstructor":
								if (!methodCalled.IsStatic)
									ProcessSystemTypeGetMemberLikeCall (ref reflectionContext, System.Reflection.MemberTypes.Constructor, instructionIndex - 1);

								break;

							//
							// GetMethod (string)
							// GetMethod (string, BindingFlags)
							// GetMethod (string, Type[])
							// GetMethod (string, Type[], ParameterModifier[])
							// GetMethod (string, BindingFlags, Binder, Type[], ParameterModifier[])
							// GetMethod (string, BindingFlags, Binder, CallingConventions, Type[], ParameterModifier[])
							//
							// TODO: .NET Core extensions
							// GetMethod (string, int, Type[])
							// GetMethod (string, int, Type[], ParameterModifier[]?)
							// GetMethod (string, int, BindingFlags, Binder?, Type[], ParameterModifier[]?)
							// GetMethod (string, int, BindingFlags, Binder?, CallingConventions, Type[], ParameterModifier[]?)
							//
							case "GetMethod":
								if (!methodCalled.IsStatic)
									ProcessSystemTypeGetMemberLikeCall (ref reflectionContext, System.Reflection.MemberTypes.Method, instructionIndex - 1);

								break;

							//
							// GetField (string)
							// GetField (string, BindingFlags)
							//
							case "GetField":
								if (!methodCalled.IsStatic)
									ProcessSystemTypeGetMemberLikeCall (ref reflectionContext, System.Reflection.MemberTypes.Field, instructionIndex - 1);

								break;

							//
							// GetEvent (string)
							// GetEvent (string, BindingFlags)
							//
							case "GetEvent":
								if (!methodCalled.IsStatic)
									ProcessSystemTypeGetMemberLikeCall (ref reflectionContext, System.Reflection.MemberTypes.Event, instructionIndex - 1);

								break;

							//
							// GetProperty (string)
							// GetProperty (string, BindingFlags)
							// GetProperty (string, Type)
							// GetProperty (string, Type[])
							// GetProperty (string, Type, Type[])
							// GetProperty (string, Type, Type[], ParameterModifier[])
							// GetProperty (string, BindingFlags, Binder, Type, Type[], ParameterModifier[])
							//
							case "GetProperty":
								if (!methodCalled.IsStatic)
									ProcessSystemTypeGetMemberLikeCall (ref reflectionContext, System.Reflection.MemberTypes.Property, instructionIndex - 1);

								break;

							//
							// GetType (string)
							// GetType (string, Boolean)
							// GetType (string, Boolean, Boolean)
							// GetType (string, Func<AssemblyName, Assembly>, Func<Assembly, String, Boolean, Type>)
							// GetType (string, Func<AssemblyName, Assembly>, Func<Assembly, String, Boolean, Type>, Boolean)
							// GetType (string, Func<AssemblyName, Assembly>, Func<Assembly, String, Boolean, Type>, Boolean, Boolean)
							//
							case "GetType":
								if (!methodCalled.IsStatic) {
									break;
								} else {
									reflectionContext.AnalyzingPattern ();
									
									var first_arg_instr = GetInstructionAtStackDepth (_instructions, instructionIndex - 1, methodCalled.Parameters.Count);
									if (first_arg_instr < 0) {
										reflectionContext.RecordUnrecognizedPattern ($"Reflection call '{methodCalled.FullName}' inside '{_methodCalling.FullName}' couldn't be decomposed");
										break;
									}

									//
									// The next value must be string constant (we don't handle anything else)
									//
									var first_arg = _instructions [first_arg_instr];
									if (first_arg.OpCode != OpCodes.Ldstr) {
										reflectionContext.RecordUnrecognizedPattern ($"Reflection call '{methodCalled.FullName}' inside '{_methodCalling.FullName}' was detected with argument which cannot be analyzed");
										break;
									}

									string typeName = (string)first_arg.Operand;
									TypeDefinition foundType = _markStep.ResolveFullyQualifiedTypeName (typeName);
									if (foundType == null) {
										reflectionContext.RecordUnrecognizedPattern ($"Reflection call '{methodCalled.FullName}' inside '{_methodCalling.FullName}' was detected with type name `{typeName}` which can't be resolved.");
										break;
									}

									var methodCalling = reflectionContext.MethodCalling;
									reflectionContext.RecordRecognizedPattern (foundType, () => _markStep.MarkType (foundType, new DependencyInfo (DependencyKind.TypeAccessedViaReflection, methodCalling)));
								}
								break;
						}

						break;

					//
					// System.Linq.Expressions.Expression
					//
					case "Expression" when methodCalledType.Namespace == "System.Linq.Expressions":
						Instruction second_argument;
						TypeDefinition declaringType;

						if (!methodCalled.IsStatic)
							break;

						switch (methodCalled.Name) {

							//
							// static Call (Type, String, Type[], Expression[])
							//
							case "Call": {
									reflectionContext.AnalyzingPattern ();

									var first_arg_instr = GetInstructionAtStackDepth (_instructions, instructionIndex - 1, 4);
									if (first_arg_instr < 0) {
										reflectionContext.RecordUnrecognizedPattern ($"Expression call '{methodCalled.FullName}' inside '{_methodCalling.FullName}' couldn't be decomposed");
										break;
									}

									var first_arg = _instructions [first_arg_instr];
									if (first_arg.OpCode == OpCodes.Ldtoken)
										first_arg_instr++;

									declaringType = FindReflectionTypeForLookup (_instructions, first_arg_instr);
									if (declaringType == null) {
										reflectionContext.RecordUnrecognizedPattern ($"Expression call '{methodCalled.FullName}' inside '{_methodCalling.FullName}' was detected with 1st argument which cannot be analyzed");
										break;
									}

									var second_arg_instr = GetInstructionAtStackDepth (_instructions, instructionIndex - 1, 3);
									second_argument = _instructions [second_arg_instr];
									if (second_argument.OpCode != OpCodes.Ldstr) {
										reflectionContext.RecordUnrecognizedPattern ($"Expression call '{methodCalled.FullName}' inside '{_methodCalling.FullName}' was detected with 2nd argument which cannot be analyzed");
										break;
									}

									var name = (string)second_argument.Operand;

									MarkMethodsFromReflectionCall (ref reflectionContext, declaringType, name, null, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
								}

								break;

							//
							// static Property(Expression, Type, String)
							// static Field (Expression, Type, String)
							//
							case "Property":
							case "Field": {
									reflectionContext.AnalyzingPattern ();

									var second_arg_instr = GetInstructionAtStackDepth (_instructions, instructionIndex - 1, 2);
									if (second_arg_instr < 0) {
										reflectionContext.RecordUnrecognizedPattern ($"Expression call '{methodCalled.FullName}' inside '{_methodCalling.FullName}' couldn't be decomposed");
										break;
									}

									var second_arg = _instructions [second_arg_instr];
									if (second_arg.OpCode == OpCodes.Ldtoken)
										second_arg_instr++;

									declaringType = FindReflectionTypeForLookup (_instructions, second_arg_instr);
									if (declaringType == null) {
										reflectionContext.RecordUnrecognizedPattern ($"Expression call '{methodCalled.FullName}' inside '{_methodCalling.FullName}' was detected with 2nd argument which cannot be analyzed");
										break;
									}

									var third_arg_inst = GetInstructionAtStackDepth (_instructions, instructionIndex - 1, 1);
									var third_argument = _instructions [third_arg_inst];
									if (third_argument.OpCode != OpCodes.Ldstr) {
										reflectionContext.RecordUnrecognizedPattern ($"Expression call '{methodCalled.FullName}' inside '{_methodCalling.FullName}' was detected with the 3rd argument which cannot be analyzed");
										break;
									}

									var name = (string)third_argument.Operand;

									//
									// The first argument can be any expression but we are looking only for simple null
									// which we can convert to static only field lookup
									//
									var first_arg_instr = GetInstructionAtStackDepth (_instructions, instructionIndex - 1, 3);
									bool staticOnly = false;

									if (first_arg_instr >= 0) {
										var first_arg = _instructions [first_arg_instr];
										if (first_arg.OpCode == OpCodes.Ldnull)
											staticOnly = true;
									}

									if (methodCalled.Name [0] == 'P')
										MarkPropertiesFromReflectionCall (ref reflectionContext, declaringType, name, staticOnly);
									else
										MarkFieldsFromReflectionCall (ref reflectionContext, declaringType, name, staticOnly);
								}

								break;

							//
							// static New (Type)
							//
							case "New": {
									reflectionContext.AnalyzingPattern ();

									var first_arg_instr = GetInstructionAtStackDepth (_instructions, instructionIndex - 1, 1);
									if (first_arg_instr < 0) {
										reflectionContext.RecordUnrecognizedPattern ($"Expression call '{methodCalled.FullName}' inside '{_methodCalling.FullName}' couldn't be decomposed");
										break;
									}

									var first_arg = _instructions [first_arg_instr];
									if (first_arg.OpCode == OpCodes.Ldtoken)
										first_arg_instr++;

									declaringType = FindReflectionTypeForLookup (_instructions, first_arg_instr);
									if (declaringType == null) {
										reflectionContext.RecordUnrecognizedPattern ($"Expression call '{methodCalled.FullName}' inside '{_methodCalling.FullName}' was detected with 1st argument which cannot be analyzed");
										break;
									}

									MarkMethodsFromReflectionCall (ref reflectionContext, declaringType, ".ctor", 0, BindingFlags.Instance, parametersCount: 0);
								}
								break;
						}

						break;

					//
					// System.Reflection.RuntimeReflectionExtensions
					//
					case "RuntimeReflectionExtensions" when methodCalledType.Namespace == "System.Reflection":
						switch (methodCalled.Name) {
							//
							// static GetRuntimeField (this Type type, string name)
							//
							case "GetRuntimeField":
								ProcessSystemTypeGetMemberLikeCall (ref reflectionContext, System.Reflection.MemberTypes.Field, instructionIndex - 1, thisExtension: true);
								break;

							//
							// static GetRuntimeMethod (this Type type, string name, Type[] parameters)
							//
							case "GetRuntimeMethod":
								ProcessSystemTypeGetMemberLikeCall (ref reflectionContext, System.Reflection.MemberTypes.Method, instructionIndex - 1, thisExtension: true);
								break;

							//
							// static GetRuntimeProperty (this Type type, string name)
							//
							case "GetRuntimeProperty":
								ProcessSystemTypeGetMemberLikeCall (ref reflectionContext, System.Reflection.MemberTypes.Property, instructionIndex - 1, thisExtension: true);
								break;

							//
							// static GetRuntimeEvent (this Type type, string name)
							//
							case "GetRuntimeEvent":
								ProcessSystemTypeGetMemberLikeCall (ref reflectionContext, System.Reflection.MemberTypes.Event, instructionIndex - 1, thisExtension: true);
								break;
						}

						break;

					//
					// System.AppDomain
					//
					case "AppDomain" when methodCalledType.Namespace == "System":
						//
						// CreateInstance (string assemblyName, string typeName)
						// CreateInstance (string assemblyName, string typeName, bool ignoreCase, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, object? []? args, System.Globalization.CultureInfo? culture, object? []? activationAttributes)
						// CreateInstance (string assemblyName, string typeName, object? []? activationAttributes)
						//
						// CreateInstanceAndUnwrap (string assemblyName, string typeName)
						// CreateInstanceAndUnwrap (string assemblyName, string typeName, bool ignoreCase, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, object? []? args, System.Globalization.CultureInfo? culture, object? []? activationAttributes)
						// CreateInstanceAndUnwrap (string assemblyName, string typeName, object? []? activationAttributes)
						//
						// CreateInstanceFrom (string assemblyFile, string typeName)
						// CreateInstanceFrom (string assemblyFile, string typeName, bool ignoreCase, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, object? []? args, System.Globalization.CultureInfo? culture, object? []? activationAttributes)
						// CreateInstanceFrom (string assemblyFile, string typeName, object? []? activationAttributes)
						//
						// CreateInstanceFromAndUnwrap (string assemblyFile, string typeName)
						// CreateInstanceFromAndUnwrap (string assemblyFile, string typeName, bool ignoreCase, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, object? []? args, System.Globalization.CultureInfo? culture, object? []? activationAttributes)
						// CreateInstanceFromAndUnwrap (string assemblyFile, string typeName, object? []? activationAttributes)
						//
						switch (methodCalled.Name) {
							case "CreateInstance":
							case "CreateInstanceAndUnwrap":
							case "CreateInstanceFrom":
							case "CreateInstanceFromAndUnwrap":
								ProcessActivatorCallWithStrings (ref reflectionContext, instructionIndex - 1, methodCalled.Parameters.Count < 4);
								break;
						}

						break;

					//
					// System.Reflection.Assembly
					//
					case "Assembly" when methodCalledType.Namespace == "System.Reflection":
						//
						// CreateInstance (string typeName)
						// CreateInstance (string typeName, bool ignoreCase)
						// CreateInstance (string typeName, bool ignoreCase, BindingFlags bindingAttr, Binder? binder, object []? args, CultureInfo? culture, object []? activationAttributes)
						//
						if (methodCalled.Name == "CreateInstance") {
							//
							// TODO: This could be supported for `this` only calls
							//
							reflectionContext.AnalyzingPattern ();
							reflectionContext.RecordUnrecognizedPattern ($"Activator call '{methodCalled.FullName}' inside '{_methodCalling.FullName}' is not yet supported");
							break;
						}

						break;

					//
					// System.Activator
					//
					case "Activator" when methodCalledType.Namespace == "System":
						if (!methodCalled.IsStatic)
							break;

						switch (methodCalled.Name) {
							//
							// static T CreateInstance<T> ()
							//
							case "CreateInstance" when methodCalled.ContainsGenericParameter:
								// Not sure it's worth implementing as we cannot expant T and simple cases can be rewritten
								reflectionContext.AnalyzingPattern ();
								reflectionContext.RecordUnrecognizedPattern ($"Activator call '{methodCalled.FullName}' inside '{_methodCalling.FullName}' is not supported");
								break;

							//
							// static CreateInstance (string assemblyName, string typeName)
							// static CreateInstance (string assemblyName, string typeName, bool ignoreCase, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, object?[]? args, System.Globalization.CultureInfo? culture, object?[]? activationAttributes)
							// static CreateInstance (string assemblyName, string typeName, object?[]? activationAttributes)
							//
							// static CreateInstance (System.Type type)
							// static CreateInstance (System.Type type, bool nonPublic)
							// static CreateInstance (System.Type type, params object?[]? args)
							// static CreateInstance (System.Type type, object?[]? args, object?[]? activationAttributes)
							// static CreateInstance (System.Type type, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, object?[]? args, System.Globalization.CultureInfo? culture)
							// static CreateInstance (System.Type type, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, object?[]? args, System.Globalization.CultureInfo? culture, object?[]? activationAttributes) { throw null; }
							//
							case "CreateInstance": {
									reflectionContext.AnalyzingPattern ();

									var parameters = methodCalled.Parameters;
									if (parameters.Count < 1)
										break;

									if (parameters [0].ParameterType.MetadataType == MetadataType.String) {
										ProcessActivatorCallWithStrings (ref reflectionContext, instructionIndex - 1, parameters.Count < 4);
										break;
									}

									var first_arg_instr = GetInstructionAtStackDepth (_instructions, instructionIndex - 1, methodCalled.Parameters.Count);
									if (first_arg_instr < 0) {
										reflectionContext.RecordUnrecognizedPattern ($"Activator call '{methodCalled.FullName}' inside '{_methodCalling.FullName}' couldn't be decomposed");
										break;
									}

									if (parameters [0].ParameterType.IsTypeOf ("System", "Type")) {
										declaringType = FindReflectionTypeForLookup (_instructions, first_arg_instr + 1);
										if (declaringType == null) {
											reflectionContext.RecordUnrecognizedPattern ($"Activator call '{methodCalled.FullName}' inside '{_methodCalling.FullName}' was detected with 1st argument expression which cannot be analyzed");
											break;
										}

										BindingFlags bindingFlags = BindingFlags.Instance;
										int? parametersCount = null;

										if (methodCalled.Parameters.Count == 1) {
											parametersCount = 0;
										} else {
											var second_arg_instr = GetInstructionAtStackDepth (_instructions, instructionIndex - 1, methodCalled.Parameters.Count - 1);
											second_argument = _instructions [second_arg_instr];
											switch (second_argument.OpCode.Code) {
												case Code.Ldc_I4_0 when parameters [1].ParameterType.MetadataType == MetadataType.Boolean:
													parametersCount = 0;
													bindingFlags |= BindingFlags.Public;
													break;
												case Code.Ldc_I4_1 when parameters [1].ParameterType.MetadataType == MetadataType.Boolean:
													parametersCount = 0;
													break;
												case Code.Ldc_I4_S when parameters [1].ParameterType.IsTypeOf ("System.Reflection", "BindingFlags"):
													bindingFlags = (BindingFlags)(sbyte)second_argument.Operand;
													break;
											}
										}

										MarkMethodsFromReflectionCall (ref reflectionContext, declaringType, ".ctor", 0, bindingFlags, parametersCount);
									}
									else {
										reflectionContext.RecordUnrecognizedPattern ($"Activator call '{methodCalled.FullName}' inside '{_methodCalling.FullName}' was detected with 1st argument expression which cannot be analyzed");
									}

								}

								break;

							//
							// static CreateInstanceFrom (string assemblyFile, string typeName)
							// static CreateInstanceFrom (string assemblyFile, string typeName, bool ignoreCase, System.Reflection.BindingFlags bindingAttr, System.Reflection.Binder? binder, object? []? args, System.Globalization.CultureInfo? culture, object? []? activationAttributes)
							// static CreateInstanceFrom (string assemblyFile, string typeName, object? []? activationAttributes)
							//
							case "CreateInstanceFrom":
								ProcessActivatorCallWithStrings (ref reflectionContext, instructionIndex - 1, methodCalled.Parameters.Count < 4);
								break;
						}

						break;
				}

			}

			//
			// Handles static method calls in form of Create (string assemblyFile, string typeName, ......)
			//
			void ProcessActivatorCallWithStrings (ref ReflectionPatternContext reflectionContext, int startIndex, bool defaultCtorOnly)
			{
				reflectionContext.AnalyzingPattern ();

				var parameters = reflectionContext.MethodCalled.Parameters;
				if (parameters.Count < 2) {
					reflectionContext.RecordUnrecognizedPattern ($"Activator call '{reflectionContext.MethodCalled.FullName}' inside '{_methodCalling.FullName}' is not supported");
					return;
				}

				if (parameters [0].ParameterType.MetadataType != MetadataType.String && parameters [1].ParameterType.MetadataType != MetadataType.String) {
					reflectionContext.RecordUnrecognizedPattern ($"Activator call '{reflectionContext.MethodCalled.FullName}' inside '{_methodCalling.FullName}' is not supported");
					return;
				}

				var first_arg_instr = GetInstructionAtStackDepth (_instructions, startIndex, reflectionContext.MethodCalled.Parameters.Count);
				if (first_arg_instr < 0) {
					reflectionContext.RecordUnrecognizedPattern ($"Activator call '{reflectionContext.MethodCalled.FullName}' inside '{_methodCalling.FullName}' couldn't be decomposed");
					return;
				}

				var first_arg = _instructions [first_arg_instr];
				if (first_arg.OpCode != OpCodes.Ldstr) {
					reflectionContext.RecordUnrecognizedPattern ($"Activator call '{reflectionContext.MethodCalled.FullName}' inside '{_methodCalling.FullName}' was detected with the 1st argument which cannot be analyzed");
					return;
				}

				var second_arg_instr = GetInstructionAtStackDepth (_instructions, startIndex, reflectionContext.MethodCalled.Parameters.Count - 1);
				if (second_arg_instr < 0) {
					reflectionContext.RecordUnrecognizedPattern ($"Activator call '{reflectionContext.MethodCalled.FullName}' inside '{_methodCalling.FullName}' couldn't be decomposed");
					return;
				}

				var second_arg = _instructions [second_arg_instr];
				if (second_arg.OpCode != OpCodes.Ldstr) {
					reflectionContext.RecordUnrecognizedPattern ($"Activator call '{reflectionContext.MethodCalled.FullName}' inside '{_methodCalling.FullName}' was detected with the 2nd argument which cannot be analyzed");
					return;
				}

				string assembly_name = (string)first_arg.Operand;
				if (!_markStep._context.Resolver.AssemblyCache.TryGetValue (assembly_name, out var assembly)) {
					reflectionContext.RecordUnrecognizedPattern ($"Activator call '{reflectionContext.MethodCalled.FullName}' inside '{_methodCalling.FullName}' references assembly '{assembly_name}' which could not be found");
					return;
				}

				string type_name = (string)second_arg.Operand;
				var declaringType = FindType (assembly, type_name);

				if (declaringType == null) {
					reflectionContext.RecordUnrecognizedPattern ($"Activator call '{reflectionContext.MethodCalled.FullName}' inside '{_methodCalling.FullName}' references type '{type_name}' which could not be found");
					return;
				}

				MarkMethodsFromReflectionCall (ref reflectionContext, declaringType, ".ctor", 0, null, defaultCtorOnly ? 0 : (int?)null);
			}

			//
			// Handles instance methods called over typeof (Foo) with string name as the first argument
			//
			void ProcessSystemTypeGetMemberLikeCall (ref ReflectionPatternContext reflectionContext, System.Reflection.MemberTypes memberTypes, int startIndex, bool thisExtension = false)
			{
				reflectionContext.AnalyzingPattern ();

				int first_instance_arg = reflectionContext.MethodCalled.Parameters.Count;
				if (thisExtension)
					--first_instance_arg;

				var first_arg_instr = GetInstructionAtStackDepth (_instructions, startIndex, first_instance_arg);
				if (first_arg_instr < 0) {
					reflectionContext.RecordUnrecognizedPattern ($"Reflection call '{reflectionContext.MethodCalled.FullName}' inside '{_methodCalling.FullName}' couldn't be decomposed");
					return;
				}

				var first_arg = _instructions [first_arg_instr];
				BindingFlags? bindingFlags = default;
				string name = default;

				if (memberTypes == System.Reflection.MemberTypes.Constructor) {
					if (first_arg.OpCode == OpCodes.Ldc_I4_S && reflectionContext.MethodCalled.Parameters.Count > 0 && reflectionContext.MethodCalled.Parameters [0].ParameterType.IsTypeOf ("System.Reflection", "BindingFlags")) {
						bindingFlags = (BindingFlags)(sbyte)first_arg.Operand;
					}
				} else {
					//
					// The next value must be string constant (we don't handle anything else)
					//
					if (first_arg.OpCode != OpCodes.Ldstr) {
						reflectionContext.RecordUnrecognizedPattern ($"Reflection call '{reflectionContext.MethodCalled.FullName}' inside '{_methodCalling.FullName}' was detected with argument which cannot be analyzed");
						return;
					}

					name = (string)first_arg.Operand;

					var pos_arg = _instructions [first_arg_instr + 1];
					if (pos_arg.OpCode == OpCodes.Ldc_I4_S && reflectionContext.MethodCalled.Parameters.Count > 1 && reflectionContext.MethodCalled.Parameters [1].ParameterType.IsTypeOf ("System.Reflection", "BindingFlags")) {
						bindingFlags = (BindingFlags)(sbyte)pos_arg.Operand;
					}
				}

				var declaringType = FindReflectionTypeForLookup (_instructions, first_arg_instr - 1);
				if (declaringType == null) {
					reflectionContext.RecordUnrecognizedPattern ($"Reflection call '{reflectionContext.MethodCalled.FullName}' inside '{_methodCalling.FullName}' does not use detectable instance type extraction");
					return;
				}

				switch (memberTypes) {
					case System.Reflection.MemberTypes.Constructor:
						MarkMethodsFromReflectionCall (ref reflectionContext, declaringType, ".ctor", 0, bindingFlags);
						break;
					case System.Reflection.MemberTypes.Method:
						MarkMethodsFromReflectionCall (ref reflectionContext, declaringType, name, 0, bindingFlags);
						break;
					case System.Reflection.MemberTypes.Field:
						MarkFieldsFromReflectionCall (ref reflectionContext, declaringType, name);
						break;
					case System.Reflection.MemberTypes.Property:
						MarkPropertiesFromReflectionCall (ref reflectionContext, declaringType, name);
						break;
					case System.Reflection.MemberTypes.Event:
						MarkEventsFromReflectionCall (ref reflectionContext, declaringType, name);
						break;
					default:
						Debug.Fail ("Unsupported member type");
						reflectionContext.RecordUnrecognizedPattern ($"Reflection call '{reflectionContext.MethodCalled.FullName}' inside '{_methodCalling.FullName}' is of unexpected member type.");
						break;
				}
			}

			//
			// arity == null for name match regardless of arity
			//
			void MarkMethodsFromReflectionCall (ref ReflectionPatternContext reflectionContext, TypeDefinition declaringType, string name, int? arity, BindingFlags? bindingFlags, int? parametersCount = null)
			{
				bool foundMatch = false;
				foreach (var method in declaringType.Methods) {
					var mname = method.Name;

					// Either exact match or generic method with any arity when unspecified
					if (mname != name && !(arity == null && mname.StartsWith (name, StringComparison.Ordinal) && mname.Length > name.Length + 2 && mname [name.Length + 1] == '`')) {
						continue;
					}

					if ((bindingFlags & (BindingFlags.Instance | BindingFlags.Static)) == BindingFlags.Static && !method.IsStatic)
						continue;

					if ((bindingFlags & (BindingFlags.Instance | BindingFlags.Static)) == BindingFlags.Instance && method.IsStatic)
						continue;

					if ((bindingFlags & (BindingFlags.Public | BindingFlags.NonPublic)) == BindingFlags.Public && !method.IsPublic)
						continue;

					if ((bindingFlags & (BindingFlags.Public | BindingFlags.NonPublic)) == BindingFlags.NonPublic && method.IsPublic)
						continue;

					if (parametersCount != null && parametersCount != method.Parameters.Count)
						continue;

					foundMatch = true;
					var methodCalling = reflectionContext.MethodCalling;
					System.Diagnostics.Debug.Assert (method.DeclaringType != null);
					reflectionContext.RecordRecognizedPattern (method, () => _markStep.MarkIndirectlyCalledMethod (method, new DependencyInfo (DependencyKind.MethodAccessedViaReflection, methodCalling)));
				}

				if (!foundMatch)
					reflectionContext.RecordUnrecognizedPattern ($"Reflection call '{reflectionContext.MethodCalled.FullName}' inside '{reflectionContext.MethodCalling.FullName}' could not resolve method `{name}` on type `{declaringType.FullName}`.");
			}

			void MarkPropertiesFromReflectionCall (ref ReflectionPatternContext reflectionContext, TypeDefinition declaringType, string name, bool staticOnly = false)
			{
				bool foundMatch = false;
				var methodCalling = reflectionContext.MethodCalling;
				foreach (var property in declaringType.Properties) {
					if (property.Name != name)
						continue;

					bool markedAny = false;

					// It is not easy to reliably detect in the IL code whether the getter or setter (or both) are used.
					// Be conservative and mark everything for the property.
					var getter = property.GetMethod;
					if (getter != null && (!staticOnly || staticOnly && getter.IsStatic)) {
						reflectionContext.RecordRecognizedPattern (getter, () => _markStep.MarkIndirectlyCalledMethod (getter, new DependencyInfo (DependencyKind.MethodAccessedViaReflection, methodCalling)));
						markedAny = true;
					}

					var setter = property.SetMethod;
					if (setter != null && (!staticOnly || staticOnly && setter.IsStatic)) {
						reflectionContext.RecordRecognizedPattern (setter, () => _markStep.MarkIndirectlyCalledMethod (setter, new DependencyInfo (DependencyKind.MethodAccessedViaReflection, methodCalling)));
						markedAny = true;
					}

					if (markedAny) {
						foundMatch = true;
						reflectionContext.RecordRecognizedPattern (property, () => _markStep.MarkProperty (property, new DependencyInfo (DependencyKind.PropertyAccessedViaReflection, methodCalling)));
					}
				}

				if (!foundMatch)
					reflectionContext.RecordUnrecognizedPattern ($"Reflection call '{reflectionContext.MethodCalled.FullName}' inside '{reflectionContext.MethodCalling.FullName}' could not resolve property `{name}` on type `{declaringType.FullName}`.");
			}

			void MarkFieldsFromReflectionCall (ref ReflectionPatternContext reflectionContext, TypeDefinition declaringType, string name, bool staticOnly = false)
			{
				bool foundMatch = false;
				var methodCalling = reflectionContext.MethodCalling;
				foreach (var field in declaringType.Fields) {
					if (field.Name != name)
						continue;

					if (staticOnly && !field.IsStatic)
						continue;

					foundMatch = true;
					reflectionContext.RecordRecognizedPattern (field, () => _markStep.MarkField (field, new DependencyInfo (DependencyKind.FieldAccessedViaReflection, methodCalling)));
					break;
				}

				if (!foundMatch)
					reflectionContext.RecordUnrecognizedPattern ($"Reflection call '{reflectionContext.MethodCalled.FullName}' inside '{reflectionContext.MethodCalling.FullName}' could not resolve field `{name}` on type `{declaringType.FullName}`.");
			}

			void MarkEventsFromReflectionCall (ref ReflectionPatternContext reflectionContext, TypeDefinition declaringType, string name)
			{
				bool foundMatch = false;
				var methodCalling = reflectionContext.MethodCalling;
				foreach (var eventInfo in declaringType.Events) {
					if (eventInfo.Name != name)
						continue;

					foundMatch = true;
					reflectionContext.RecordRecognizedPattern (eventInfo, () => _markStep.MarkEvent (eventInfo, new DependencyInfo (DependencyKind.EventAccessedViaReflection, methodCalling)));
				}

				if (!foundMatch)
					reflectionContext.RecordUnrecognizedPattern ($"Reflection call '{reflectionContext.MethodCalled.FullName}' inside '{reflectionContext.MethodCalling.FullName}' could not resolve event `{name}` on type `{declaringType.FullName}`.");
			}
		}

		static int GetInstructionAtStackDepth (Collection<Instruction> instructions, int startIndex, int stackSizeToBacktrace)
		{
			for (int i = startIndex; i >= 0; --i) {
				var instruction = instructions [i];

				switch (instruction.OpCode.StackBehaviourPop) {
				case StackBehaviour.Pop0:
					break;
				case StackBehaviour.Pop1:
				case StackBehaviour.Popi:
				case StackBehaviour.Popref:
					stackSizeToBacktrace++;
					break;
				case StackBehaviour.Pop1_pop1:
				case StackBehaviour.Popi_pop1:
				case StackBehaviour.Popi_popi:
				case StackBehaviour.Popi_popi8:
				case StackBehaviour.Popi_popr4:
				case StackBehaviour.Popi_popr8:
				case StackBehaviour.Popref_pop1:
				case StackBehaviour.Popref_popi:
					stackSizeToBacktrace += 2;
					break;
				case StackBehaviour.Popref_popi_popi:
				case StackBehaviour.Popref_popi_popi8:
				case StackBehaviour.Popref_popi_popr4:
				case StackBehaviour.Popref_popi_popr8:
				case StackBehaviour.Popref_popi_popref:
					stackSizeToBacktrace += 3;
					break;
				case StackBehaviour.Varpop:
					switch (instruction.OpCode.Code) {
					case Code.Call:
					case Code.Calli:
					case Code.Callvirt:
						if (instruction.Operand is MethodReference mr) {
							stackSizeToBacktrace += mr.Parameters.Count;
							if (mr.Resolve ()?.IsStatic == false)
								stackSizeToBacktrace++;
						}

						break;
					case Code.Newobj:
						if (instruction.Operand is MethodReference ctor) {
							stackSizeToBacktrace += ctor.Parameters.Count;
						}
						break;
					case Code.Ret:
						// TODO: Need method return type for correct stack size but this path should not be hit yet
						break;
					default:
						return -3;
					}
					break;
				}

				switch (instruction.OpCode.StackBehaviourPush) {
				case StackBehaviour.Push0:
					break;
				case StackBehaviour.Push1:
				case StackBehaviour.Pushi:
				case StackBehaviour.Pushi8:
				case StackBehaviour.Pushr4:
				case StackBehaviour.Pushr8:
				case StackBehaviour.Pushref:
					stackSizeToBacktrace--;
					break;
				case StackBehaviour.Push1_push1:
					stackSizeToBacktrace -= 2;
					break;
				case StackBehaviour.Varpush:
					//
					// Only call, calli, callvirt will hit this
					//
					if (instruction.Operand is MethodReference mr && mr.ReturnType.MetadataType != MetadataType.Void) {
						stackSizeToBacktrace--;
					}
					break;
				}

				if (stackSizeToBacktrace == 0)
					return i;

				if (stackSizeToBacktrace < 0)
					return -1;
			}

			return -2;
		}

		static TypeDefinition FindReflectionTypeForLookup (Collection<Instruction> instructions, int startIndex)
		{
			while (startIndex >= 1) {
				int storeIndex = -1;
				var instruction = instructions [startIndex];
				switch (instruction.OpCode.Code) {
				//
				// Pattern #1
				//
				// typeof (Foo).ReflectionCall ()
				//
				case Code.Call:
					if (!(instruction.Operand is MethodReference mr) || mr.Name != "GetTypeFromHandle")
						return null;

					var ldtoken = instructions [startIndex - 1];

					if (ldtoken.OpCode != OpCodes.Ldtoken)
						return null;

					return (ldtoken.Operand as TypeReference).Resolve ();

				//
				// Patern #2
				//
				// var temp = typeof (Foo);
				// temp.ReflectionCall ()
				//
				case Code.Ldloc_0:
					storeIndex = GetIndexOfInstruction (instructions, OpCodes.Stloc_0, startIndex - 1);
					startIndex = storeIndex - 1;
					break;
				case Code.Ldloc_1:
					storeIndex = GetIndexOfInstruction (instructions, OpCodes.Stloc_1, startIndex - 1);
					startIndex = storeIndex - 1;
					break;
				case Code.Ldloc_2:
					storeIndex = GetIndexOfInstruction (instructions, OpCodes.Stloc_2, startIndex - 1);
					startIndex = storeIndex - 1;
					break;
				case Code.Ldloc_3:
					storeIndex = GetIndexOfInstruction (instructions, OpCodes.Stloc_3, startIndex - 1);
					startIndex = storeIndex - 1;
					break;
				case Code.Ldloc_S:
					storeIndex = GetIndexOfInstruction (instructions, OpCodes.Stloc_S, startIndex - 1, l => (VariableReference)l.Operand == (VariableReference)instruction.Operand);
					startIndex = storeIndex - 1;
					break;
				case Code.Ldloc:
					storeIndex = GetIndexOfInstruction (instructions, OpCodes.Stloc, startIndex - 1, l => (VariableReference)l.Operand == (VariableReference)instruction.Operand);
					startIndex = storeIndex - 1;
					break;

				case Code.Nop:
					startIndex--;
					break;

				default:
					return null;
				}
			}

			return null;
		}

		static int GetIndexOfInstruction (Collection<Instruction> instructions, OpCode opcode, int startIndex, Predicate<Instruction> comparer = null)
		{
			while (startIndex >= 0) {
				var instr = instructions [startIndex];
				if (instr.OpCode == opcode && (comparer == null || comparer (instr)))
					return startIndex;

				startIndex--;
			}

			return -1;
		}

		protected class AttributeProviderPair {
			public AttributeProviderPair (CustomAttribute attribute, ICustomAttributeProvider provider)
			{
				Attribute = attribute;
				Provider = provider;
			}

			public CustomAttribute Attribute { get; private set; }
			public ICustomAttributeProvider Provider { get; private set; }
		}
	}

	// Make our own copy of the BindingFlags enum, so that we don't depend on System.Reflection.
	[Flags]
	enum BindingFlags
	{
		Default = 0,
		IgnoreCase = 1,
		DeclaredOnly = 2,
		Instance = 4,
		Static = 8,
		Public = 16,
		NonPublic = 32,
		FlattenHierarchy = 64,
		InvokeMethod = 256,
		CreateInstance = 512,
		GetField = 1024,
		SetField = 2048,
		GetProperty = 4096,
		SetProperty = 8192,
		PutDispProperty = 16384,
		PutRefDispProperty = 32768,
		ExactBinding = 65536,
		SuppressChangeType = 131072,
		OptionalParamBinding = 262144,
		IgnoreReturn = 16777216
	}
}
