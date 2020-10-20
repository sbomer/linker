using System;
using Mono.Cecil;
using System.Collections.Generic;
using System.Diagnostics;

namespace Mono.Linker
{
	public class TypeMapInfo
	{

		// When a type is instantiated, get its base/implemented methods. Any that are marked
		// cause the override to be marked immediately. Any that are not are used to track deferred
		// marking of the override.


		// Maps a method to its immediate base. Maps the boase method to null.
		// Like traversing a linked list.
		Dictionary<MethodDefinition, MethodDefinition> immediateBaseMethods;

		// Maps all derived methods to the highest base, and base to null.
		// Like getting the last node in a linked list, EXCEPT for the last
		// node, which returns null instead of itself.
		Dictionary<MethodDefinition, MethodDefinition> baseMethods;

		// Maps an interface-implementing method to an interface method
		// Dictionary<MethodDefinition, MethodDefinition> interfaceMethods;

		// Cache the override info!
		Dictionary<TypeDefinition, Dictionary<TypeReference, IEnumerable<OverrideInformation>>> interfaceOverrides = new Dictionary<TypeDefinition, Dictionary<TypeReference, IEnumerable<OverrideInformation>>> ();

		//
		// New data structures
		//

		// Interface impls that are satisfied by a method
		// Method -> InterfaceImpl -> Base method
		// Method -> Interface -> null means that we already checked, and it's not satisfied by this interface
		// the (type, interface) impl isn't necessarily on the base type of the method.
		// it could be a virtual method on a base type satisfying an interface impl on a derived type
		Dictionary<MethodDefinition, Dictionary<TypeReference, Dictionary<TypeDefinition, MethodDefinition?>>> methodInterfaces = new Dictionary<MethodDefinition, Dictionary<TypeReference, Dictionary<TypeDefinition, MethodDefinition?>>> ();
		void TrackMethodInterface (MethodDefinition method, TypeDefinition type, TypeReference interfaceType, MethodDefinition? interfaceMethod) {
			// DEBUG.ASsert (method.DeclaringType is the impl type!)
			if (!methodInterfaces.TryGetValue (method, out Dictionary<TypeReference, Dictionary<TypeDefinition, MethodDefinition?>> implInterfaceMethods)) {
				implInterfaceMethods = new Dictionary<TypeReference, Dictionary<TypeDefinition, MethodDefinition?>> ();
				methodInterfaces.Add(method, implInterfaceMethods);
			}
			Dictionary<TypeDefinition, MethodDefinition?> typeInterfaceMethods = null;
			foreach (var (candidateInterfaceType, candidateTypeInterfaceMethods) in implInterfaceMethods) {
				if (TypeMatch (candidateInterfaceType, interfaceType)) {
					typeInterfaceMethods = candidateTypeInterfaceMethods;
					break;
					// we already got the result of base methods for the given interface, for some types.
				}
			}

			if (typeInterfaceMethods == null) {
				typeInterfaceMethods = new Dictionary<TypeDefinition, MethodDefinition?> ();
				implInterfaceMethods.Add (interfaceType, typeInterfaceMethods);
			}

			typeInterfaceMethods.Add (type, interfaceMethod); // should throw if there's already one.
		}

		// Maps an impl to the all of the base -> overrides for it
		// Should only be called for an interfaceMethod actually on the impl!
		Dictionary<TypeDefinition, Dictionary<TypeReference, Dictionary<MethodDefinition, MethodDefinition>>> interfaceMethods = new Dictionary<TypeDefinition, Dictionary<TypeReference, Dictionary<MethodDefinition, MethodDefinition>>> ();
		void TrackInterfaceMethod (TypeDefinition type, TypeReference interfaceType, MethodDefinition interfaceMethod, MethodDefinition method) {
			// Debug.Assert (impl.InterfaceType.HasMethod (interfaceMethod));

			if (!interfaceMethods.TryGetValue (type, out Dictionary<TypeReference, Dictionary<MethodDefinition, MethodDefinition>> interfaceMethodOverrides)) {
				interfaceMethodOverrides = new Dictionary<TypeReference, Dictionary<MethodDefinition, MethodDefinition>> ();
				interfaceMethods.Add (type, interfaceMethodOverrides);
			}

			foreach (var (candidateInterfaceType, candidateMethodOverrides) in interfaceMethodOverrides) {
				if (TypeMatch (candidateInterfaceType, interfaceType)) {
					candidateMethodOverrides.Add (interfaceMethod, method); // should throw if there are duplicates.
					return;
				}
			}

			// if no interface type was being tracked, add it:
			var methodOverrides = new Dictionary<MethodDefinition, MethodDefinition> ();
			methodOverrides.Add (interfaceMethod, method);
			interfaceMethodOverrides.Add (interfaceType, methodOverrides);
		}

		// Maps an interface method to all overrides (for any interface implementation)
		// This one isn't always up-to-date - we could add more overrides.
		Dictionary<MethodDefinition, HashSet<(TypeDefinition, MethodDefinition)>> interfaceMethodOverrides = new Dictionary<MethodDefinition, HashSet<(TypeDefinition, MethodDefinition)>> ();
		void TrackInterfaceMethodOverride (MethodDefinition interfaceMethod, TypeDefinition implementingType, MethodDefinition method) {
			if (!interfaceMethodOverrides.TryGetValue (interfaceMethod, out HashSet<(TypeDefinition, MethodDefinition)> methodOverrides)) {
				methodOverrides = new HashSet<(TypeDefinition, MethodDefinition)> ();
				interfaceMethodOverrides.Add (interfaceMethod, methodOverrides);
			}
			methodOverrides.Add ((implementingType, method)); // This shouldn't have duplicates either, actually... but the list is incomplete.
		}

		protected readonly Dictionary<MethodDefinition, List<OverrideInformation>> override_methods = new Dictionary<MethodDefinition, List<OverrideInformation>> ();

		// public IEnumerable<OverrideInformation> GetBaseOverrides (MethodDefinition method)
		// {
		// 	override_methods.TryGetValue (method, out List<OverrideInformation> overrides);
		// 	return overrides;
		// }
		public IEnumerable<OverrideInformation> GetBaseOverrides (MethodDefinition method) {
			if (override_methods.TryGetValue (method, out List<OverrideInformation> overrides)) {
				foreach (var @override in overrides)
					yield return @override;
			}

			foreach (var @override in GetInterfaceOverrides (method))
				yield return @override;
		}

		public IEnumerable<OverrideInformation> GetInterfaceOverrides (MethodDefinition interfaceMethod) {
			Debug.Assert (interfaceMethod != null);
			if (!interfaceMethodOverrides.TryGetValue (interfaceMethod, out HashSet<(TypeDefinition, MethodDefinition)> methodOverrides))
				yield break;

			// TODO: name Item1/Item2
			foreach (var methodOverride in methodOverrides)
				yield return new OverrideInformation (interfaceMethod, methodOverride.Item2);
		}


		// Something that matches the semantics before:
		// GetBaseMethods gets a list of:
		// - immediate base method
		// - resolved interface method (but not default interface implementations)
		// GetBaseOverrides, similar

		// validation needs to walk up base methods, including interfaces.

		protected readonly Dictionary<MethodDefinition, List<MethodDefinition>> base_methods = new Dictionary<MethodDefinition, List<MethodDefinition>> ();

		public void AddBaseMethod (MethodDefinition method, MethodDefinition @base)
		{
			// if (@base?.ToString ().Contains ("DefaultInterface") == true || method?.ToString ().Contains ("DefaultInterface") == true) {
			// 	Console.WriteLine ("AddBaseMethod");
			// 	Console.WriteLine ($"\tmethod: {method}");
			// 	Console.WriteLine ($"\tbase: {@base}");
			// }
			// // var methods = GetBaseMethods (method);
			// if (!base_methods.TryGetValue (method, out List<MethodDefinition> bases)) {
			// 	bases = new List<MethodDefinition> ();
			// 	base_methods[method] = bases;
			// }
// 
			// bases.Add (@base);
		}

		// Sometimes, base methods should include interfaces, sometimes not.
		// We populate the list for the first time, usually when marking base methods
		// up the base hierarchy.
		// In other cases, it's expected to include interfaces.
		// For interfaces, it depends on how the types are used. We won't get all
		// base methods every time. Or should we? Maybe we should...



		// Exists just for back-compat.
		// Includes:
		// - immediate base
		// - interface base methods
		// - NOT default interface methods
		public IEnumerable<MethodDefinition> GetBaseMethods (MethodDefinition method) {
			// But this also needs to work lazily. If we haven't yet scanned, we need to do so.
			// Debug.Assert (method != null);
			// if (base_methods.TryGetValue (method, out List<MethodDefinition> bases)) {
			// 	return bases;
			// }

			// not cached - we need to get the base methods.
			// including interfaces... but those are added lazily.
			// fortunately,
			if (method.IsVirtual) {
				var baseMethod = ImmediateBaseMethod (method);
				if (baseMethod != null) {
					//AnnotateMethods (baseMethod, method);
					yield return baseMethod;
				}
			}


			if (method.HasOverrides) {
				foreach (MethodReference override_ref in method.Overrides) {
					MethodDefinition @override = override_ref.Resolve ();
					if (@override == null)
						continue;

					//AnnotateMethods (@override, method);
					yield return @override;
				}
			}

			// TODO: can we avoid this?
			// GetImplementations (method.DeclaringType);

			// Instead of processing ALL impls on the declaring type...
			// we only want to process some?
			// Doesn't exist until marked?
			// Ignore whether it's marked. Just ensure we can get all of them for now.
			// Mark tracking is separate!
			// get interfaceMethods which we will use to cache.
			if (!methodInterfaces.TryGetValue (method, out Dictionary<TypeReference, Dictionary<TypeDefinition, MethodDefinition?>> implInterfaceMethods)) {
				implInterfaceMethods = new Dictionary<TypeReference, Dictionary<TypeDefinition, MethodDefinition?>> ();
				methodInterfaces.Add (method, implInterfaceMethods);
			}

			// TODO: a way to avoid iterating over all impls, instead over just the relevant ones?
			// If we can be sure the dictionary is filled in, we should be able to.

			// TODO: Vitek expects this to return interfaces implemented on derived types... :(
			// let's give a partial result based on what has been mapped in interfaces so far.
			// First, go through interfaces on this type, using the cache if possible.
			var leftOverInterfaceMethods = new Dictionary<TypeReference, Dictionary<TypeDefinition, MethodDefinition?>> (implInterfaceMethods);


			// this sees if there are any interface methods overridden by the current method
			// TODO: how is this different than just getting interfaces? should be able to just get interfaces normally, I think.
			foreach (var interfaceImpl in method.DeclaringType.GetInflatedInterfaces ()) {
				Dictionary<TypeDefinition, MethodDefinition?> typeInterfaceMethods = null;
				foreach (var (candidateInterfaceType, candidateTypeInterfaceMethods) in implInterfaceMethods) {
					if (TypeMatch (candidateInterfaceType, interfaceImpl.InflatedInterface)) {
						typeInterfaceMethods = candidateTypeInterfaceMethods;
						break;
					}
				}
				if (typeInterfaceMethods == null) {
					typeInterfaceMethods = new Dictionary<TypeDefinition, MethodDefinition?> ();
					implInterfaceMethods.Add (interfaceImpl.InflatedInterface, typeInterfaceMethods);
				}

				if (!typeInterfaceMethods.TryGetValue (method.DeclaringType, out MethodDefinition? interfaceMethod)) {
					// None cached for this impl. We need to get the result for this impl.
	//				var overrideInfo = GetOverrideInfoCached (method.DeclaringType, interfaceImpl);
	//				interfaceMethods.Add (interfaceImpl.OriginalImpl, overrideInfo.)
					interfaceMethod = TryMatchMethod (interfaceImpl.InflatedInterface, method);
					// TODO: see if modifiers match too.
					// Track the result (found or not!)
					typeInterfaceMethods.Add (method.DeclaringType, interfaceMethod);

					// Note: this should't re-cache if it was found in the cache.
					if (interfaceMethod != null) {
						TrackInterfaceMethod (method.DeclaringType, interfaceImpl.InflatedInterface, interfaceMethod, method);
						TrackInterfaceMethodOverride (interfaceMethod, interfaceImpl.InflatedInterface.Resolve (), method);
					}
				} else {
					// cached the result for this type, interface.
					leftOverInterfaceMethods.Remove (interfaceImpl.InflatedInterface);
				}
				if (interfaceMethod != null)
					yield return interfaceMethod;
			}

			// Next, we'll yield any cached ones that weren't already yielded (from derived types)
			foreach (var (leftOverInterfaceType_, typeMethods) in leftOverInterfaceMethods) {
				foreach (var (leftOverType_, leftOverMethod) in typeMethods) {
					// TODO: assert that it's from an impl on a derived type?
					// TODO: the cache could have multiple base methods for the same interface, but different types.
					// we might end up returning one for this type, and ALSO the SAME interface method for a left-over base one for a different type.
					// maybe we should track leftovers by methoddefinition instead?
					yield return leftOverMethod;
				}
			}

			// this will annotate any overrides too.

			// TODO: if GetImplementations is called before GetBaseMethods,
			// we will add base methods to the cache, and then this method
			// will ONLY return implementations! problem!

			// if we did all the processing and there are no base methods,
			// remember this!
			// if (!base_methods.TryGetValue (method, out bases)) {
			// 	base_methods [method] = bases = null;
			// }
// 
			// return bases;
		}

		// public IEnumerable<MethodDefinition> GetBaseMethods (MethodDefinition method) {
		// 	Debug.Assert (method != null);
		// 	while ((method = ImmediateBaseMethod (method)) != null)
		// 		yield return method;
		// }

		// Tracks OVERRIDE OR BASE! Similar to AnnotateMethods from TypeMapStep.
		// Now repurposing to track only base overrides.
		public void AddOverride (MethodDefinition @base, MethodDefinition @override, InterfaceImplementation matchingInterfaceImplementation = null)
		{
			Debug.Assert (@base != null);
			Debug.Assert (@override != null);
			if (@base?.ToString ().Contains ("DefaultInterface") == true || @override?.ToString ().Contains ("DefaultInterface") == true) {
				Console.WriteLine ("AddOverride:");
				Console.WriteLine ($"\tbase: {@base}");
				Console.WriteLine ($"\toverride: {@override}");
				Console.WriteLine ($"\timpl: {matchingInterfaceImplementation?.InterfaceType}");
			}
			if (!override_methods.TryGetValue (@base, out List<OverrideInformation> methods)) {
				methods = new List<OverrideInformation> ();
				override_methods.Add (@base, methods);
			}

			methods.Add (new OverrideInformation (@base, @override, matchingInterfaceImplementation));
		}

		public TypeMapInfo ()
		{
			immediateBaseMethods = new Dictionary<MethodDefinition, MethodDefinition> ();
			baseMethods = new Dictionary<MethodDefinition, MethodDefinition> ();
		}

		public IEnumerable<OverrideInformation> GetOverridesAndImplementations (TypeDefinition type) {
			foreach (var overrideInfo in GetBaseOverrides (type)) {
				Debug.Assert (overrideInfo.Override.DeclaringType == type);
				yield return overrideInfo;
			}

			foreach (var overrideInfo in GetImplementations (type)) {
				// Debug.Assert (overrideInfo.Override.DeclaringType == type);
				// not true for default interface method.
				// not true for virtual resolved on a base type.
				yield return overrideInfo;
			}
		}

		public IEnumerable<OverrideInformation> GetBaseOverrides (TypeDefinition type) {
			// Returns virtual overrides. Doesn't include overrides inherited from a base type,
			// because we expect those will be tracked should the base type be instantiated.
			if (!type.HasMethods)
				yield break;

			foreach (var method in type.Methods) {
				if (!method.IsVirtual)
					continue;

				// TODO: MapVirtualInterfaceMethod?
				var baseMethod = ImmediateBaseMethod (method);
				if (baseMethod != null)
					yield return new OverrideInformation (baseMethod, method);

				if (method.HasOverrides) {
					// TODO: what if the override is a generic instantiation?
					foreach (MethodReference override_ref in method.Overrides) {
						MethodDefinition @override = override_ref.Resolve ();
						if (@override == null) // TODO: unresolved?
							continue;

						yield return new OverrideInformation (@override, method);
					}
				}
			}
		}

		void AnnotateMethods (MethodDefinition @base, MethodDefinition @override, InterfaceImplementation matchingInterfaceImplementation = null)
		{
			// AddBaseMethod (@override, @base);
			// AddOverride (@base, @override, matchingInterfaceImplementation);
		}


		public IEnumerable<OverrideInformation> GetOverrideInfoCached (TypeDefinition type, TypeReference interfaceType)
		{
			// TODO: is this cache redundant with the others?
			// Yes, I think it is. remove it.

			// see if the cache already has value.
			if (interfaceOverrides.TryGetValue (type, out Dictionary<TypeReference, IEnumerable<OverrideInformation>> interfaceOverrideInfos)) {
				foreach (var (candidateInterfaceType, candidateOverrideInfos) in interfaceOverrideInfos) {
					if (TypeMatch (candidateInterfaceType, interfaceType)) {
						// cached! just return the iterator.
						return candidateOverrideInfos;
					}
				}
			} else {
				interfaceOverrideInfos = new Dictionary<TypeReference, IEnumerable<OverrideInformation>> ();
			}

			// Not cached.
			var overrideInfos = GetOverrideInfo (type, interfaceType);

			// Cache the iterator
			interfaceOverrideInfos.Add (interfaceType, overrideInfos);
			// And return it.
			return overrideInfos;
		}

		public IEnumerable<OverrideInformation> GetOverrideInfo (TypeDefinition type, TypeReference interfaceType)
		{
			// not sure if this assert is correct. adding it anyway.
			Debug.Assert (!type.IsInterface);

			// get or create methodOverrides which we will cache in this method.
			Dictionary<MethodDefinition, MethodDefinition> methodOverrides = null;
			if (!interfaceMethods.TryGetValue (type, out Dictionary<TypeReference, Dictionary<MethodDefinition, MethodDefinition>> interfaceMethodOverrides)) {
				interfaceMethodOverrides = new Dictionary<TypeReference, Dictionary<MethodDefinition, MethodDefinition>> ();
				interfaceMethods.Add (type, interfaceMethodOverrides);
			}


			methodOverrides = new Dictionary<MethodDefinition, MethodDefinition> ();
			foreach (var (candidateInterfaceType, candidateMethodOverrides) in interfaceMethodOverrides) {
				if (TypeMapInfo.TypeMatch (candidateInterfaceType, interfaceType)) {
					methodOverrides = candidateMethodOverrides;
					break;
				}
			}
			if (methodOverrides == null) {
				methodOverrides = new Dictionary<MethodDefinition, MethodDefinition> ();
				interfaceMethodOverrides.Add (interfaceType, methodOverrides);
			}


			// TODO: can we tell when all overrides for this impl have been processed?
			// then iterate over methodOverrides, instead of interfaceMethods?

			foreach (var interfaceMethod in interfaceType.GetMethods ()) {
				var resolvedInterfaceMethod = interfaceMethod.Resolve ();
				if (resolvedInterfaceMethod == null)
					continue;

				if (!resolvedInterfaceMethod.IsVirtual
					|| resolvedInterfaceMethod.IsFinal
					|| !resolvedInterfaceMethod.IsNewSlot)
					continue;

				// TODO: shouldn't we also look at methods that could match variantly?
				// like I<Derived, Base>.M(Base) -> Derived should match I<Base, Derived>.M(Derived) -> Base

				// TODO: this method could return the wrong answwer if the type has an explicit interface
				// implementation. we end up marking extra things as a result. we should instead look at overrides
				// here. they're guaranteed to be loaded already. might be able to look at cache.

				// Instead of TryMatch here, let's see if we already tracked this interface method on the impl.
				
				if (methodOverrides.TryGetValue (resolvedInterfaceMethod, out MethodDefinition method)) {
					// already found a match for this impl!
					yield return new OverrideInformation (resolvedInterfaceMethod, method);
					continue;
				}

				// we've tracked something for this impl, but not the requested method.
				// we need to scan all of them now. we will be creating an entry for the method, so let's

				// TODO: we call TrackInterfaceMethodOverride here, and in GetBaseMethods for a method.
				// ensure we don't do it twice? or maybe we can cache it?

				// this doesn't look for .override or explicit impls.
				var exactMatchOnType = TryMatchMethod (type, interfaceMethod);
				if (exactMatchOnType != null) {
					//AnnotateMethods (resolvedInterfaceMethod, exactMatchOnType);
					TrackMethodInterface (exactMatchOnType, type, interfaceType, resolvedInterfaceMethod);
					// TrackInterfaceMethod (interfaceImplementation.OriginalImpl, resolvedInterfaceMethod, exactMatchOnType);
					// instead of TrackInterfaceMethod, more optimized:
					methodOverrides.Add (resolvedInterfaceMethod, exactMatchOnType);
					TrackInterfaceMethodOverride (resolvedInterfaceMethod, type, exactMatchOnType);
					yield return new OverrideInformation (resolvedInterfaceMethod, exactMatchOnType);
					continue;
				}

				// TODO: what of instantiated base method?
				// probably should resolve it.
				var @base = GetBaseMethodInTypeHierarchy (type, interfaceMethod);
				if (@base != null) {
					TrackMethodInterface (@base, type, interfaceType, resolvedInterfaceMethod);
					// Don't want method interfaces to include those implemented on derived types of the method's declaring type
					// oh... actually, we do. Vitek does.

					//TrackInterfaceMethod (interfaceImplementation.OriginalImpl, resolvedInterfaceMethod, @base);
					// instead of TrackInterfaceMethod, more optimized:
					methodOverrides.Add (resolvedInterfaceMethod, @base);
					TrackInterfaceMethodOverride (resolvedInterfaceMethod, type, @base);
					// TODO: track original impl? but... is this actually necessary?
					// why would it be any different from the above?
					//AnnotateMethods (resolvedInterfaceMethod, @base);
					yield return new OverrideInformation (resolvedInterfaceMethod, @base);
					continue;
				}

				// Only if a virtual method isn't found do we look for default interface implementations.
				bool found = false;
				foreach (var (defaultImpl, defaultImplMethod) in GetDefaultInterfaceImplementations (type, interfaceMethod)) {
					found = true;
					// defaultImpl is on the type, I think.
					// Michal tracks default impl as:
					// resolved Itf method, type, instantiated impl
					// and when processing virtual method, get all default impls for it.
					// mark interface implementations for it.

					// yield return new OverrideInformation ();
					// yield return new OverrideInformation ()
					// TODO: track the default interface impl. what should we do with it?

					// TryMatchMethod fails, because the names aren't the same.
					// the explicit implementation is named fully like Namespace.IFoo<T>.Method, while the interface method it overrides
					// only is name "Method"
					Debug.Assert (defaultImplMethod != null);
					var defaultImplMethodDef = defaultImplMethod.Resolve ();
					if (defaultImplMethodDef == null)
						continue;

					// don't TrackMethodInterface (that one is never used to get default interface methods)
					TrackInterfaceMethodOverride (resolvedInterfaceMethod, type, defaultImplMethodDef);
					methodOverrides.Add (resolvedInterfaceMethod, defaultImplMethodDef);

					yield return new OverrideInformation (resolvedInterfaceMethod, defaultImplMethodDef);
					continue;
				}

				if (!found) {
					//
				}
			}
		}

		public IEnumerable<OverrideInformation> GetImplementation (TypeDefinition type, TypeReference interfaceType) {
			Debug.Assert (type.HasInterfaces);

			foreach (var overrideInfo in GetOverrideInfoCached (type, interfaceType))
				yield return overrideInfo;
		}

		// TODO: rename. GetInterfaceImplementationMethods?
		// How to prevent duplicate work?
		// We should cache the results of getting implementations on a type.
		public IEnumerable<OverrideInformation> GetImplementations (TypeDefinition type) {
			if (!type.HasInterfaces)
				yield break;

			// only looks at direct interface implementations. shouldn't we get interfaces recursively?
			// how to avoid duplication in case they're already collapsed for us?
			// TODO: can we just get rid of GetInflatedInterfaces?
			foreach (var interfaceImpl in type.GetInflatedInterfaces ()) {
				foreach (var overrideInfo in GetOverrideInfoCached (type, interfaceImpl.InflatedInterface))
					yield return overrideInfo;
//				var interfaceType = @interface.InterfaceType;
//				var iface = interfaceType.Resolve ();
//				if (iface = null)
//					continue;
//
//				if (iface.HasMethods) {
//					foreach (var interfaceMethod in iface.Methods) {
//						// get the implementation of this interface method on the type.
//						if (TryMatchMethod (type, interfaceMethod) != null)
//							continue;
//					}
//				}
			}
		}

		// What's the most specific default implementation?
		// Solve that later. For now, just get all of them.
		public static bool IsRelevant (IMetadataTokenProvider tok)
		{
			return tok?.ToString ().Contains ("Tests.Cases") == true;
		}

		static IEnumerable<(InterfaceImplementation, MethodReference)> GetDefaultInterfaceImplementations (TypeDefinition type, MethodReference interfaceMethod)
		{
			// If type implements IFoo<B> with M(B), and a different interface provides
			// M(B), then it should match.
			// The default interface implementation for IFoo<B>.M(B) comes from IM(B).
			// So this should look for a matching impl...
			// But if
			if (IsRelevant (type)) {
				Console.WriteLine("hey");
			}
			var resolvedInterfaceMethod = interfaceMethod.Resolve ();
			if (resolvedInterfaceMethod == null)
				yield break;

			// OH - they're already inflated!
			foreach (var interfaceImpl in type.Interfaces) {
				var potentialImplInterface = interfaceImpl.InterfaceType.Resolve ();
				if (potentialImplInterface == null)
					continue;

				foreach (var potentialImplMethod in interfaceImpl.InterfaceType.GetMethods ()) {
					// TODO: how to check if it's the same one?

					var resolvedPotentialImplMethod = potentialImplMethod.Resolve ();
					if (resolvedPotentialImplMethod == null)
						continue;

					bool foundImpl = false;
					// TODO: can we use  virtual/final/newslot here?

					// this probably will never match - because names don't match.
					// TODO: check whether names match. requires fixing MethodMatch
					// to work correctly in presence of generics, explicit overrides.
					// probably shouldn't make assumptions about the .override directive.
					// could be implementing just one specific version of the interface method.
					if (MethodMatch (potentialImplMethod, interfaceMethod) &&
						!resolvedPotentialImplMethod.IsAbstract) {
							// TODO: is this the correct impl?
						yield return (interfaceImpl, potentialImplMethod);
					}

					if (!resolvedPotentialImplMethod.HasOverrides)
						continue;

					// TODO: fix foundImpl logic!

					// get overrides, and inflate them according to the typeref.
					// THEN check if they match the original!
					foreach (var @override in potentialImplMethod.GetInflatedOverrides ()) {
						// if this method reference matches the
						// signature we are looking for, it could provide it.
						// 

						// TODO: we could keep less by looking at instantiations, instead of resolving.
						// just resolve for now.
						// This will keep potentially-irrelevant implementations.
						// TODO: since we stop after we found one, we could find the wrong method
						// and actually have a bug!

						if (MethodMatch (@override, interfaceMethod)) {
							yield return (interfaceImpl, potentialImplMethod);
							foundImpl = true;
							break;
						}

						//    // cases:
						//    // override can be an instantiated generic
						//    // can be an open generic (instantiated with type generic params)
						//    // a methodref can refer to a method on an instantiated generic type
						//    // how to get that type?
						//    var @overrideDeclaringType = @override.GetInflatedDeclaringType ();

						//    var resolvedOverride = @override.Resolve ();
						//    if (resolvedOverride == null)
						//    	continue;

						//    // can override be of an inflated method? yes.
						//    // check the interface method against
						//    // the override, inflated same as its declaring type.
						//    var overrideDeclaringType = InflateGenericType (interfaceImpl.InflatedInterface, resolvedOverride.DeclaringType);
						//    var inflatedOverride = TryMatchMethod (overrideDeclaringType, )
						//    foreach (var overrideBase in overrideDeclaringType.GetMethods ()) {
						//    	// find a matching inflated override method
						//    	if (MethodsMat)
						//    }
						//    
						//    // instantiate the override, see if it matches the instantiated interface method we are looking for.
						//    // find the inflated override.

						//    	// what if they resolve to the same, but are completely different instantiations?
						//    	// need to check the instantiation.

						//    	// TODO: is this the correct impl?
						//    	yield return interfaceImpl.OriginalImpl;
						//    	foundImpl = true;
						//    	break;
						//    }
					}

					if (foundImpl)
						break;

					if (!foundImpl) {
						// TODO
						// recursively scan for default impls. don't we also need to do this when mapping interface methods?
					}
				}
			}
		}

		public MethodDefinition ImmediateBaseMethod (MethodDefinition method)
		{
			Debug.Assert (method != null);
			Debug.Assert (method.IsVirtual);

			if (!immediateBaseMethods.TryGetValue (method, out MethodDefinition immediateBase)) {
				immediateBase = GetBaseMethodInTypeHierarchy (method.DeclaringType, method);
				immediateBaseMethods [method] = immediateBase;
				if (immediateBase != null) {
					// also cache in the opposite directoin.
					AddOverride (immediateBase, method);
					// AnnotateMethods (immediateBase, method);
				}
			}

			return immediateBase;
		}

		public MethodDefinition BaseMethod (MethodDefinition method)
		{
			Debug.Assert (method != null);
			Debug.Assert (method.IsVirtual);

			if (!baseMethods.TryGetValue (method, out MethodDefinition baseMethod)) {
				baseMethod = ImmediateBaseMethod (method);

				if (baseMethod != null) {
					while (true) {
						var nextBase = ImmediateBaseMethod (baseMethod);
						if (nextBase == null)
							break;
						baseMethod = nextBase;
					}
				}

				baseMethods [method] = baseMethod;
			}

			return baseMethod;
		}

		internal static MethodDefinition GetBaseMethodInTypeHierarchy (TypeDefinition type, MethodReference method)
		{
			TypeReference @base = type.GetInflatedBaseType ();
			while (@base != null) {
				MethodDefinition base_method = TryMatchMethod (@base, method);
				if (base_method != null) // TODO: handle unresolved?
					return base_method;

				@base = @base.GetInflatedBaseType ();
			}

			return null;
		}

		internal static MethodDefinition TryMatchMethod (TypeReference type, MethodReference method)
		{
			foreach (var candidate in type.GetMethods ()) {
				if (MethodMatch (candidate, method))
					return candidate.Resolve ();
			}

			return null;
		}

		static bool MethodMatch (MethodReference candidate, MethodReference method)
		{
			var candidateDef = candidate.Resolve ();

			if (!candidateDef.IsVirtual)
				return false;

			if (candidate.HasParameters != method.HasParameters)
				return false;

			if (candidate.Name != method.Name)
				return false;

			if (candidate.HasGenericParameters != method.HasGenericParameters)
				return false;

			// we need to track what the generic parameter represent - as we cannot allow it to
			// differ between the return type or any parameter
			if (!TypeMatch (candidate.GetReturnType (), method.GetReturnType ()))
				return false;

			if (!candidate.HasParameters)
				return true;

			var cp = candidate.Parameters;
			var mp = method.Parameters;
			if (cp.Count != mp.Count)
				return false;

			if (candidate.GenericParameters.Count != method.GenericParameters.Count)
				return false;

			for (int i = 0; i < cp.Count; i++) {
				if (!TypeMatch (candidate.GetParameterType (i), method.GetParameterType (i)))
					return false;
			}

			return true;
		}

		static bool TypeMatch (IModifierType a, IModifierType b)
		{
			if (!TypeMatch (a.ModifierType, b.ModifierType))
				return false;

			return TypeMatch (a.ElementType, b.ElementType);
		}

		static bool TypeMatch (TypeSpecification a, TypeSpecification b)
		{
			if (a is GenericInstanceType gita)
				return TypeMatch (gita, (GenericInstanceType) b);

			if (a is IModifierType mta)
				return TypeMatch (mta, (IModifierType) b);

			return TypeMatch (a.ElementType, b.ElementType);
		}

		static bool TypeMatch (GenericInstanceType a, GenericInstanceType b)
		{
			if (!TypeMatch (a.ElementType, b.ElementType))
				return false;

			if (a.HasGenericArguments != b.HasGenericArguments)
				return false;

			if (!a.HasGenericArguments)
				return true;

			var gaa = a.GenericArguments;
			var gab = b.GenericArguments;
			if (gaa.Count != gab.Count)
				return false;

			for (int i = 0; i < gaa.Count; i++) {
				if (!TypeMatch (gaa[i], gab[i]))
					return false;
			}

			return true;
		}

		static bool TypeMatch (GenericParameter a, GenericParameter b)
		{
			if (a.Position != b.Position)
				return false;

			if (a.Type != b.Type)
				return false;

			return true;
		}

		public static bool TypeMatch (TypeReference a, TypeReference b)
		{
			if (a is TypeSpecification || b is TypeSpecification) {
				if (a.GetType () != b.GetType ())
					return false;

				return TypeMatch ((TypeSpecification) a, (TypeSpecification) b);
			}

			if (a is GenericParameter genericParameterA && b is GenericParameter genericParameterB)
				return TypeMatch (genericParameterA, genericParameterB);

			return a.FullName == b.FullName;
		}
	}
}