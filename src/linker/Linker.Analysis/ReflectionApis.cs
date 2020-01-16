using System.Linq;
using Mono.Cecil;

namespace Mono.Linker.Analysis
{
	public class ReflectionApis
	{
		public static InterestingReason GetInterestingReasonForReflectionApi (MethodDefinition method)
		{
			switch (method.DeclaringType.FullName) {
				case "System.Activator":
					switch (method.Name) {
						case "CreateInstance":
							// CreateInstance(Type, ...) is dangerous because it requires existance of the specified Type and it having a .ctor.
							// But linker can't figure out which types to keep as those are determined at runtime dynamically.

							// CreateInstance<T>() is currently dangerous because linker can't figure out all instantiations. In the future
							// it should be possible for the linker to determine all of the generic instantiations of this method
							// and thus correctly analyze it.

							// CreateInstanceFrom - all cases load new files (new code) - see Assembly.LoadFrom for details.

							// NullableTypeInfo::WriteData does Activator.CreateInstance. which the linker doesn't peer into or warn about...
							// it doesn't output warnings for Activator.CreateInstance, or try to detect them at all from what I can tell.
							// return InterestingReason.CreateInstance;
							// this results in finding 89 stacktraces - one more found than just with LinkerUnanalyzed,
							// and 30 of them are CreateInstance (though 29 of them would have shown up as LinkerUnanalyzed) (only 57 are left as LinkerUnanalyzed)

							// the thing it does CreateInstance on is a member valueInfo.DataType.
							// valueInfo is set in the NullableTypeInfo ctor: valueInfo = TraceLoggingTypeInfo.GetInstance(typeArgs[0], recursionCheck)
							// where typeargs[] = type.GenericTypeArguments for a Type passed in to the ctor.
							// NullableTypeInfo ctor is only called from TraceLoggingTypeInfo::CreateDefaultTypeInfo - which gets a Type.
							// in one case, if IsGenericMatch(dataType, typeof(Nullable<>)), it constructs the NullableTypeInfo.
							// so, this is an implementation detail of TraceLoggingTypeInfo CreateDefaultTypeInfo
							// which ITSELF is only called from TraceLoggingTypeInfo.GetInstance(Type type)
							// ... which is called from more places. let's temporarily mark GetInstance as unsafe, then the ctor will not be reached.
							// then we can mark WriteData as safe because it can never be called (if the ctor is never called!)
							// we can mark the things it calls as safu - such as NullableTypeInfo
							// which virtual method causes WriteData to be kept?
							// it's for EventSource... let's look at it again later.
							// I thought maybe that this case with Write<Nullable<Foo>> is ok.
							// I just want to see what else calls Write like this.
							// however when I run console now, I get no output, because I've told it to only stop at public, while still ignoring virtuals.
							// which pretty much is like pretending everything is OK. need to continue along this path!

							// next step: try running this with an eventsource app.
							if (method.Parameters.Count == 1 && method.Parameters [0].ParameterType.FullName == "System.Type") {
								return InterestingReason.CreateInstance;
							}
							return InterestingReason.KnownReflection;
						case "CreateInstanceFrom":
							return InterestingReason.KnownReflection;
					}
					break;
				case "System.AppDomain":
					switch (method.Name) {
						case "get_BaseDirectory":
							// Some apps may have assumptions about where BaseDirectory points to
							// in single-file deployments it might not match the expectations.
							return InterestingReason.SingleFileUnfriendly;

						case "CreateInstance":
						case "CreateInstanceAndUnwrap":
							// CreateInstance(string assembly, string type)
							// we mark this as unsafe rather than the internal createinstance, because there might be some public
							// entry points that are actually safe - and then we would want to mark the internal one as safe.
							if ((method.Parameters.Count == 2 || method.Parameters.Count == 8 || method.Parameters.Count == 3) &&
								method.Parameters [0].ParameterType.FullName == "System.String" &&
								method.Parameters [1].ParameterType.FullName == "System.String") {
								return InterestingReason.CreateInstance;
							}
							return InterestingReason.KnownReflection;
						// TODO: apply also to CreateInstanceFrom.
						case "CreateInstanceFrom":
						case "CreateInstanceFromAndUnwrap":
						case "ExecuteAssembly":
						case "ExecuteAssemblyByName":
							// Some of these always throw on .NET Core and some call into Activator.CreateInstance/Activator.CreateInstanceFrom
							// but it's simpler/easier to understand to mark them explicitly anyway.
							// See the comments in Activator for more details.
							return InterestingReason.KnownReflection;

						case "GetAssemblies":
						case "ReflectionOnlyGetAssemblies":
							// TODO: These can be dangerous since in trimmed apps they might return smaller set than in untrimmed apps.
							// It's unclear if we should mark that as dangerous or not.
							return InterestingReason.ToInvestigate_EnumMetadata;

						case "Load":
							// Same as Assembly.Load (actually calls into that) - see comments for Assembly.Load for details.
							return InterestingReason.KnownReflection;
					}
					break;
				// case "System.RuntimeTypeHandle":
				// considered safe. typef(Foo) == typeof(T) or typeof(Foo) == o.GetType() used for perf optimizations
				// but isn't a problem.
				// case "System.ArgIterator":
				// should be safe
				case "System.Delegate":
					switch (method.Name) {
						case ".ctor":
						// This is protected, so it's highly unlikely to be used as it needs subclassing which pretty much nobody does
						// but still - technically the .ctor is dangerous as it takes the name of the method as a string.
						case "CreateDelegate":
							if (method.Parameters.Count >= 3 && method.Parameters [2].ParameterType.FullName == "System.String") {
								// The overloads which take method name as string are unsafe as they effectively express a dependency
								// on a method which linker can't analyze.
								return InterestingReason.KnownReflection;
							} else if (method.Parameters.Any (p => p.ParameterType.FullName == "System.Reflection.MethodInfo")) {
								// The overloads which take MethodInfo should be safe since
								// - The type of the delegate is passed in as Type, but creating an instance of it does not add additional constrains
								//   on it - I think????
								// - The method itself is safe since by this time a MethodInfo for it is available.
								return InterestingReason.ToInvestigate;
							} else {
								// Should never get here really, but to be safe mark it as dangerous.
								return InterestingReason.KnownReflection;
							}
					}
					break;
				case "System.Type":
					switch (method.Name) {
						case "FindInterfaces":
						case "FindMembers":
							// TODO: potentially dangerous as on trimmed apps these would return different set of interfaces/methods
							// than on un-trimmed apps.
							// Need to determine if we should mark these as unsafe.
							return InterestingReason.ToInvestigate_EnumMetadata;

						case "GetConstructor":
							// Effectively expresses a dependency on a .ctor on a type which linker can't always determine.
							return InterestingReason.KnownReflection;

						case "GetEvent":
						case "GetField":
						case "GetInterface":
						case "GetMember":
						case "GetMethod":
						case "GetMethodImpl":
						case "GetNestedType":
						case "GetProperty":
						case "GetPropertyImpl":
							// Effectively expresses a dependency on a member by string name - which can by dynamic and thus not analyzable.
							return InterestingReason.KnownReflection;

						case "GetConstructors":
						case "GetEvents":
						case "GetFields":
						case "GetInterfaces":
						case "GetMembers":
						case "GetMethods":
						case "GetNestedTypes":
						case "GetProperties":
							// In trimmed apps these may return different sets as compared to un-trimmed apps.
							// Unlike some of the other methods with similar behavior these are very likely to break apps
							// so marking them as dangerous for now.
							return InterestingReason.KnownReflection_EnumMetadata;

						case "GetInterfaceMap":
							// Basically means that the type should implement the specified interface type.
							// But linker in general trims interfaces from types if it can't find references to them.
							// As such this introduces a reference which the linker can't analyze.
							return InterestingReason.KnownReflection;

						case "GetType":
						case "ReflectionOnlyGetType":
							if (method.Parameters.Count > 0) {
								// Effectively expresses a dependncy on a type by a string name - not analyzable.
								return InterestingReason.KnownReflection;
							}

							// The GetType() overload is different (it's object.GetType) and it is not dangerous.
							break;

						// case "GetTypeArray": safe since it is basically equivalent to object.GetType.
						// case "GetTypeCode":
						// case "GetTypeCodeImpl":
						// safe as it doesn't introduce new dependency on the type

						case "GetTypeFromCLSID":
						case "GetTypeFromProgID":
							// TODO: Very likely dangerous as it introduces a dependency on a type specified by GUID
							return InterestingReason.ToInvestigate;

						case "GetTypeFromHandle":
							// This is most commonly used by C#'s "typeof(typename)"
							// The pattern in that case looks like "ldtoken thetype; call GetTypeFromHandle"
							// Linker recognizes any instruction with operand of type "token" and marks that token
							// so in this case it will call MarkType(thetype).
							// Marking type without anything else basically preserves the type, base class and so on
							// but typically no methods and such.
							// That is good enough for it to be turned into a Type instance.
							// All it means is that any other reflection API which takes Type and makes additional requirements on it
							// (like having a .ctor, or looking for a method) needs to be marked as dangerous - which it is.
							return InterestingReason.None;

						case "InvokeMember":
							// Effectively a reference to a member by string name.
							return InterestingReason.KnownReflection;

						case "MakeArrayType":
							// Probably safe, but worth some more investigation.
							return InterestingReason.ToInvestigate;

						case "MakeGenericMethodParameter":
						case "MakeGenericSignatureType":
							// TODO: No idea what this actually does - needs investigation.
							return InterestingReason.ToInvestigate;

						case "MakeGenericType":
							// Creates new instantiation of a generic type. In itself probably not dangerous (since all the types are available at this point)
							// but this will create lot of problems for linker to be able to determine generic instantiations.
							return InterestingReason.DynamicGenericInstantiation;
					}
					break;
				// case "System.UnitySerializationHolder": safe as it only ever holds DBNull value.
				case "System.Reflection.Assembly":
					switch (method.Name) {
						case "get_CodeBase":
						case "get_EscapedCodeBase":
						case "get_Location":
							// Location of assemblies when packaged as single-file are not file paths anymore, typically breaks apps.
							return InterestingReason.SingleFileUnfriendly;
						case "get_CustomAttributes":
						case "GetCustomAttributes":
							return InterestingReason.KeepUsedAttributeTypesOnlyUnsafe;

						case "get_DefinedTypes":
						case "get_ExportedTypes":
						case "get_Modules":
						case "GetExportedTypes":
						case "GetForwardedTypes":
						case "GetLoadedModules":
						case "GetModules":
						case "GetReferencedAssemblies":
						case "GetTypes":
							// TODO: Enumeration members - trimmed apps may report different set from un-trimmed apps
							// may cause breaks for certain apps. (Some of these are more likely to break than others).
							return InterestingReason.ToInvestigate_EnumMetadata;

						case "get_EntryPoint":
							// Need to validate if linker always keeps an entry point for an assembly.
							// If not, this is potentially dangerous.
							return InterestingReason.ToInvestigate;

						case "CreateInstance":
							// Effectively adds a reference to type's .ctor, but with the type specified as Type - so not analyzable by linker.

							// CreateInstance(string assembly, string type)
							// we mark this as unsafe rather than the internal createinstance, because there might be some public
							// entry points that are actually safe - and then we would want to mark the internal one as safe.
							if ((method.Parameters.Count == 2 || method.Parameters.Count == 8 || method.Parameters.Count == 3) &&
								method.Parameters [0].ParameterType.FullName == "System.String" &&
								method.Parameters [1].ParameterType.FullName == "System.String") {
								return InterestingReason.CreateInstance;
							}
							return InterestingReason.KnownReflection;

						case "GetFile":
						case "GetFiles":
						case "GetManifestResourceInfo":
						case "GetManifestResourceNames":
						case "GetManifestResourceStream":
							// Access the manifest.
							// TODO: Don't know what this is used for.
							// TODO: Investigate if linker actually trims manifest streams in some way, if not then this is technically safe.
							return InterestingReason.ToInvestigate;

						case "GetModule":
							// Can linker actually trim modules? Potentially dangerous.
							return InterestingReason.ToInvestigate;

						case "GetSatelliteAssembly":
							// Can linker trim satellites? Potentially dangerous.
							return InterestingReason.ToInvestigate;

						// case "GetObjectData": // safe because this is not supported on .NET Core (always throws)

						case "GetType":
							// Effectively adds a reference to a type by a string name - not analyzable.
							return InterestingReason.KnownReflection;

						case "IsDefined":
							return InterestingReason.KeepUsedAttributeTypesOnlyUnsafe;

						case "Load":
						case "LoadWithPartialName":
							// Load(string) and Load(AssemblyName) are effectively assembly references which the linker
							// doesn't see (as they are dynamic) - so they might need an assembly to be kept which linker could
							// otherwise trim. So for this reason they are dangerous.
							// It might be possible to recognize constants and handle those cases in the linker in the future.

							// Load(byte[]) and Load(byte[], byte[]) are effectively introducing new code into the app which the linker
							// didn't see. As such this code might have dependencies on things in the app which nothing else has dependencies on.
							// So linker might trim things this new code may need. For this reason usage of these APIs is dangerous.

							// LoadWithPartialName is basically just Load(string) - with a slightly different argument checks and error handling.
							return InterestingReason.KnownReflection;

						case "LoadFile":
						case "LoadFrom":
						case "UnsafeLoadFrom":
							// Loads new code from a file - similar to Load(byte[]) in its effect. It introduces new code into the app
							// which may have references to trimmed things - thus potentially breaking as linker can't analyze the code
							// added via these APIs.
							return InterestingReason.KnownReflection;

						case "LoadModule":
							// .NET Core doesn't implement this, but semantically this is dangerous (it's basically Load(byte []))
							return InterestingReason.KnownReflection;

						case "ReflectionOnlyLoad":
						case "ReflectionOnlyLoadFrom":
							// .NET Core doesn't implement this, but semantically this is dangerous (basically Load and LoadFrom)
							return InterestingReason.KnownReflection;
					}
					break;

				case "System.Reflection.Binder":
					// The Binder itself is a purely abstract class so in itself is safe. Derived classes might not be though.
					return InterestingReason.None;

				case "System.Reflection.ConstructorInfo":
					// Since linker doesn't trim method bodies only: If there's a way to get to the ConstructorInfo
					// then the constructor must be present and exist as a fully functional method.
					// As such it must be callable as well. So even ConstructorInfo.Invoke is safe then.
					return InterestingReason.None;

				case "System.Reflection.CustomAttributeData":
					switch (method.Name) {
						case "GetCustomAttributes":
							// Trimmed apps may have fewer attributes than untrimmed apps - as such the enumeration is potentially breaking (unsafe).
							return InterestingReason.KeepUsedAttributeTypesOnlyUnsafe;
						default:
							// Otherwise the CustomAttributeData is safe, mainly because linker preserves entire attributes always.
							return InterestingReason.None;
					}

				case "System.Reflection.CustomAttributeExtensions":
					// The way linker works with attributes is that:
					// It will keep attributes where the type of the attribute has been marked by usage elsewhere than the attribute usage itself.
					// This means that just adding the attribute to a method will not preserve the attribute. But asking for it explicitly by its type
					// will preserve the attribute.
					// The problem is that most of the attribute accessors in reflection APIs handle inheritance, so for example, I can ask for
					// attribute of type BaseAttribute and it will get me all attributes which derive from BaseAttribute.
					// This is not how linker works, linker will only perserve attributes which type is explicitly referenced. So in the above sample
					// in un-trimmed app I would get a BaseAttribute instance back (in reality some DerivedAttribute instance), but in trimmed app
					// I would not get anything back since the attribute would be trimmed.
					// If the linker is ran in this more aggresive mode, accessing pretty much any attribute through the reflection APIs is potentially
					// dangerous.
					return InterestingReason.KeepUsedAttributeTypesOnlyUnsafe;

				case "System.Reflection.EventInfo":
					switch (method.Name) {
						case "get_AddMethod":
						case "GetAddMethod":
						case "get_RaiseMethod":
						case "GetRaiseMethod":
						case "get_RemoveMethod":
						case "GetRemoveMethod":
							// Linker always keeps add, remove and invoke methods on an event.
							return InterestingReason.None;
						case "GetOtherMethods":
							return InterestingReason.KnownReflection;
					}
					break;

				// case "System.Reflection.ExceptionHandlingClause":
				// not unsafe on its own - you have to get a MethodBody first, which is the problem.
				// this is all unsafe since it's accessing methodbody?
				// switch (method.Name) {
				// case "get_CatchType":
				//     return InterestingReason.ToInvestigate;
				// }
				// break;

				case "System.Reflection.FieldInfo":
					switch (method.Name) {
						case "GetFieldFromHandle":
							// TODO: This is probably safe as the only way in IL to get a field handle is something like ldtoken
							// which the linker will recognize and mark the field accordingly.
							// May need additional investigation.
							return InterestingReason.ToInvestigate;
						case "GetOptionalCustomModifiers":
						case "GetRequiredCustomModifiers":
							// This is probably dangerous.
							// TODO: Not clear what exactly this accesses - and if linker has any effect on it.
							return InterestingReason.KnownReflection;
						case "GetValue":
						case "GetValueDirect":
						case "GetRawConstantValue":
						case "SetValue":
						case "SerValueDirect":
							// If the field exists (that is the app was able to get the FieldInfo instance)
							// then its value should be accessible.
							return InterestingReason.None;
					}
					break;

				// case "System.Reflection.InterfaceMapping" - in itself it's not dangerous, getting to it might be and that should be marked.

				// case "System.Reflection.LocalVariableInfo": - only accessible through method body

				// case "System.Reflection.ManifestResourceInfo": - should be safe

				case "System.Reflection.MemberInfo":
					switch (method.Name) {
						case "get_CustomAttributes":
						case "GetCustomAttributes":
						case "GetCustomAttributesData":
						case "IsDefined":
							return InterestingReason.KeepUsedAttributeTypesOnlyUnsafe;
						default:
							// Except for custom attributes the rest is safe. The dangerous part is getting to the MemberInfo itself
							// once it's available it should work just fine.
							return InterestingReason.None;
					}

				case "System.Reflection.MethodBase":
					switch (method.Name) {
						case "GetFieldFromHandle":
							// TODO: This is probably safe as the only way in IL to get a field handle is something like ldtoken
							// which the linker will recognize and mark the field accordingly.
							// May need additional investigation.
							return InterestingReason.ToInvestigate;

						case "GetMethodBody":
							// Linker doesn't trim method bodies, so if the app has access to MethodBase instance, then that method
							// will have its body present as well.
							// That said linker may rewrite the body of the method, so technically it is breaking.
							return InterestingReason.ToInvestigate_EnumMetadata;

						case "GetCurrentMethod":
						// The currently executing method must be present - by definition, so this is safe.
						case "Invoke":
						// Similarly for Invoke - if the app has MethodBase instance it can invoke it without a problem.
						default:
							return InterestingReason.None;
					}

				case "System.Reflection.MethodBody":
					// We guard the functionality of MethodBody via the access to it - which is MethodBase.GetMethodBody
					// as such the functionality of this class alone is safe.
					return InterestingReason.None;

				case "System.Reflection.MethodInfo":
					switch (method.Name) {
						case "get_ReturnTypeCustomAttributes":
							return InterestingReason.KeepUsedAttributeTypesOnlyUnsafe;
						case "CreateDelegate":
							// This might be safe
							// The only problem is the Type of the delegate to create. It must exist, otherwise the Type of it would not be available
							// but it's unclear if there are additional requirements on it which CreateDelegate has.
							return InterestingReason.ToInvestigate;
						case "MakeGenericMethod":
							return InterestingReason.DynamicGenericInstantiation;
						default:
							return InterestingReason.None;
					}

				case "System.Reflection.Module":
					switch (method.Name) {
						case "FindTypes":
							// TODO: potentially dangerous as on trimmed apps these would return different set of interfaces/methods
							// than on un-trimmed apps.
							// Need to determine if we should mark these as unsafe.
							return InterestingReason.ToInvestigate_EnumMetadata;

						case "get_CustomAttributes":
						case "GetCustomAttributes":
						case "GetCustomAttributesData":
						case "IsDefined":
							return InterestingReason.KeepUsedAttributeTypesOnlyUnsafe;

						case "GetField":
						case "GetMethod":
						case "GetMethodImpl":
						case "GetType":
							// Effectively expresses a dependency on a member by string name - which can by dynamic and thus not analyzable.
							return InterestingReason.KnownReflection;

						case "GetFields":
						case "GetMethods":
						case "GetTypes":
							// In trimmed apps these may return different sets as compared to un-trimmed apps.
							// Unlike some of the other methods with similar behavior these are very likely to break apps
							// so marking them as dangerous for now.
							return InterestingReason.KnownReflection_EnumMetadata;

						case "GetModuleHandleImpl":
						case "GetObjectData":
						case "GetPEKind":
							// These should be safe
							return InterestingReason.None;

						case "ResolveField":
						case "ResolveMember":
						case "ResolveMethod":
						case "ResolveSignature":
						case "ResolveString":
						case "ResolveType":
							// Potentially dangerous - if the metadata token comes from unknown source, the result of this operation is effectively unknown
							// to the linker.
							return InterestingReason.KnownReflection;
					}
					break;

				case "System.Reflection.ParameterInfo":
					switch (method.Name) {
						case "get_CustomAttributes":
						case "GetCustomAttributes":
						case "GetCustomAttributesData":
						case "IsDefined":
							return InterestingReason.KeepUsedAttributeTypesOnlyUnsafe;
						case "GetOptionalCustomModifiers":
						case "GetRequiredCustomModifiers":
							// This is probably dangerous.
							// TODO: Not clear what exactly this accesses - and if linker has any effect on it.
							return InterestingReason.KnownReflection;
						case "get_Member":
							// TODO: Not sure what this does
							return InterestingReason.ToInvestigate;
						case "GetRealObject":
							// Deserialization - not sure
							return InterestingReason.ToInvestigate;
						default:
							return InterestingReason.None;
					}

				case "System.Reflection.PropertyInfo":
					switch (method.Name) {
						case "get_GetMethod":
						case "GetAccessors":
						case "GetGetMethod":
						case "get_SetMethod":
						case "GetSetMethod":
						case "GetValue":
						case "SetValue":
							// These seem to be dangerous - linker seems to only keep get_/set_ methods if it sees direct reference to them
							// which would mean that accessing these through reflection only could break.
							return InterestingReason.ToInvestigate;

						case "GetOptionalCustomModifiers":
						case "GetRequiredCustomModifiers":
							// This is probably dangerous.
							// TODO: Not clear what exactly this accesses - and if linker has any effect on it.
							return InterestingReason.KnownReflection;

						case "GetConstantValue":
						case "GetRawConstantValue":
						default:
							// These should be safe
							return InterestingReason.None;
					}

				case "System.Reflection.ReflectionContext":
					// No idea what this is for - could not find any use cases either
					return InterestingReason.ToInvestigate;

				case "System.Reflection.ReflectionTypeLoadException":
					if (method.Name == "GetObjectData") {
						return InterestingReason.ToInvestigate;
					}
					break;

				case "System.Reflection.TypeDelegator":
					// Basically the same as Type and TypeInfo
					switch (method.Name) {
						case "GetConstructorImpl":
							// Effectively expresses a dependency on a .ctor on a type which linker can't always determine.
							return InterestingReason.KnownReflection;

						case "GetCustomAttributes":
						case "IsDefined":
							return InterestingReason.KeepUsedAttributeTypesOnlyUnsafe;

						case "GetEvent":
						case "GetField":
						case "GetInterface":
						case "GetMember":
						case "GetMethodImpl":
						case "GetNestedType":
						case "GetPropertyImpl":
							// Effectively expresses a dependency on a member by string name - which can by dynamic and thus not analyzable.
							return InterestingReason.KnownReflection;

						case "GetConstructors":
						case "GetEvents":
						case "GetFields":
						case "GetInterfaces":
						case "GetMembers":
						case "GetMethods":
						case "GetNestedTypes":
						case "GetProperties":
							// In trimmed apps these may return different sets as compared to un-trimmed apps.
							// Unlike some of the other methods with similar behavior these are very likely to break apps
							// so marking them as dangerous for now.
							return InterestingReason.KnownReflection_EnumMetadata;

						case "GetInterfaceMap":
							// Basically means that the type should implement the specified interface type.
							// But linker in general trims interfaces from types if it can't find references to them.
							// As such this introduces a reference which the linker can't analyze.
							return InterestingReason.KnownReflection;

						case "InvokeMember":
							// Effectively adds a reference to a member by name.
							return InterestingReason.KnownReflection;
					}
					break;

				case "System.Reflection.TypeInfo":
					switch (method.Name) {
						case "get_DeclaredConstructors":
						case "get_DeclaredEvents":
						case "get_DeclaredMembers":
						case "get_DeclaredMethods":
						case "get_DeclaredNestedTypes":
						case "get_DeclaredProperties":
						case "get_ImplementedInterfaces":
						case "GetDeclaredMethods":
							return InterestingReason.KnownReflection_EnumMetadata;
						case "GetDeclaredEvent":
						case "GetDeclaredField":
						case "GetDeclaredMethod":
						case "GetDeclaredNestedType":
						case "GetDeclaredProperty":
							return InterestingReason.KnownReflection;
					}
					break;
			}

			if (method.DeclaringType.FullName.StartsWith ("System.Reflection.Emit")) {
				if (method.DeclaringType.FullName == "System.Reflection.Emit.ModuleBuilder" &&
					method.Name == "GetMethodImpl") {
					return InterestingReason.GetMethod;
				}

				// TypeBuilder::DefineDefaultConstructor
				// if the base type is itself a TypeBuilder instantiation, gets the parent generic type definition,
				// instantiates it with the parameters of the parent (why can't it just use m_typeParent directly?)
				// and does GetConstructor
				// so generating code for a derived class at runtime, the parent type's ctor may need to be kept.
				// what's the entry point?
				// SetParent(Type? parent) public API. also called by an internal ctor.
				// if we mark that as unsafe, then DefineDefaultConstructor could be safe.
				// really, it's unsafe when using a base type that may have been linked away.
				// there may be a way to do it that doesn't just prevent use of DefineDefaultConstructor.
				// to keep this working, would need to ensure that base types for a TypeBuilder have ctors kept
				// if DefineDefaultConstructor is called.
				if (method.DeclaringType.FullName == "System.Reflection.Emit.TypeBuilder") {
					if (method.Name == "DefineDefaultConstructor") {
						return InterestingReason.DefineDefaultConstructor;
					}
				}

				return InterestingReason.ToInvestigate;
			}

			switch (method.DeclaringType.FullName) {
				case "System.Reflection.Metadata":
					if (method.Name == "TryGetRawMetadata") {
						return InterestingReason.KnownReflection;
					}
					break;
				case "System.Resources.ResourceManager":
					switch (method.Name) {
						case "get_FallbackLocation":
						case "GetSatelliteContractVersion":
							return InterestingReason.None;
						case "CommonAssemblyInit":
							// This is a private method which is called by .ctors - we mark the .ctors instead
							// as safe/unsafe as appropriate.
							return InterestingReason.None;
						case ".ctor":
							if (method.Parameters.Count == 1 &&
								method.Parameters [0].ParameterType.FullName == "System.Type") {
								// The Type is actually only used for two things:
								// - Get the Assembly of the type - which is then used as a fallback for satellite resource loading
								// - Get the FullName/Namespace of the type to scope the search for resource strings in the satellite assembly
								// Neither is a problem for linker, so in this case we just need to make sure the linker 
								return InterestingReason.None;
							}
							if (method.Parameters.Count == 2 &&
								method.Parameters [0].ParameterType.FullName == "System.String" &&
								method.Parameters [1].ParameterType.FullName == "System.Reflection.Assembly") {
								// The assembly is only used to lookup attributes and to load resources from
								// - Attribute lookup - only happens for hardcoded attribute types, and so even if linker
								//   is trimming attributes (optional) it will keep this one as the ResourceManager
								//   has a hard reference to the attribute type it's looking for.
								// - Loading resources from - linker doesn't trim resources and even if it would
								//   not finding them would be intentional in that case.
								return InterestingReason.None;
							}
							if (method.Parameters.Count == 3) {
								foreach (var p in method.Parameters) {
									if (p.ParameterType.FullName == "System.Type") {
										return InterestingReason.CustomResourceSet;
									}
								}
							}
							break;
						// OOPS: there's another public API we need to disable, because it calls the above.
						case "CreateFileBasedResourceManager":
							return InterestingReason.CustomResourceSet;
					}
					return InterestingReason.ToInvestigate;
			}

			if (method.DeclaringType.FullName.StartsWith ("System.Runtime.InteropServices")) {
				switch (method.DeclaringType.FullName) {
					case "System.Runtime.InteropServices.CoClassAttribute":
					case "System.Runtime.InteropServices.ComDefaultInterfaceAttribute":
					case "System.Runtime.InteropServices.ComEventInterfaceAttribute":
					case "System.Runtime.InteropServices.ComSourceInterfaceAttribute":
						// These contain type references in the attribute properties.
						// We need to make sure that linker can root types referenced like this.
						return InterestingReason.ToInvestigate;

					case "System.Runtime.InteropServices.ComAwareEventInfo":
						// ComAwareEventInfo add/remove handlers do
						// eventInfo.DeclaringType.GetCustomttributes(typeof(ComEventInterfaceAttribute))
						// could be understood by a simple linker heuristic
						if (method.Name == "GetDataForComInvocation") {
							return InterestingReason.ComGetAttributesSimple;
						}
						break;

					case "System.Runtime.InteropServices.ExternalException":
						// Just a typical exception class, doesn't do anything special.
						return InterestingReason.None;

					case "System.Runtime.InteropServices.GCHandle":
						return InterestingReason.None;

					case "System.Runtime.InteropServices.GuidAttribute":
					case "System.Runtime.InteropServices.ProgIdAttribute":
						// Indicates COM scenario
						return InterestingReason.ToInvestigate;

					case "System.Runtime.InteropServices.Marshal":
						switch (method.Name) {
							case "GetTypeFromCLSID":
							case "BindToMoniker":
							case "CreateAggregatedObject":
							case "CreateWrapperOfType":
							case "GetComInterfaceForObject":
							case "GetComObjectData":
							case "GetEndComSlot":
							case "GetStartComSlot":
							case "GetDispatchForObject":
							case "GetIUnknownForObject":
							case "GetObjectForIUnknown":
							case "GetTypedObjectForIUnknown":
							case "GetUniqueObjectForIUnknown":
							case "IsComObject":
							case "IsTypeVisibleFromCom":
							case "QueryInterface":
							case "ReleaseComObject":
							case "SetComObjectData":
								// indicates a COM scenario
								return InterestingReason.ToInvestigate;

							// case "GenerateGuidForType":
							// case "GenerateProgIdForType":
							//     // I think this is fine.

							case "GetDelegateForFunctionPointer":
							case "GetNativeVariantForObject":
							case "GetObjectForNativeVariant":
							case "GetObjectsForNativeVariants":
								return InterestingReason.ToInvestigate;
							case "OffsetOf":
								// Marshal.OffsetOf<T>(string) calls Marshal.OffsetOf(Type, string) to get the offset of a field
								// the latter is unsafe, but the former migth be ok? no... the linker may rewrite IL and cause field offsets to change.
								// depends what you do with the offset... it *could* be ok. it's on the author to understand that the linker
								// might change field offsets. this API won't just disappear at runtime.
								// the generic one might be analyzable.
								if (method.Parameters.Count == 2 &&
									method.Parameters [0].ParameterType.FullName == "System.Type" &&
									method.Parameters [1].ParameterType.FullName == "System.String") {
									return InterestingReason.OffsetOf;
								}
								// this one might be analyzable
								if (method.Parameters.Count == 1 && method.Parameters [0].ParameterType.FullName == "System.String") {
									return InterestingReason.OffsetOfSimple;
								}
								return InterestingReason.ToInvestigate;
							case "PtrToStructure":
							case "StructureToPtr":
							case "SizeOf":
								// Unclear - needs more investigation
								return InterestingReason.ToInvestigate;

							default:
								return InterestingReason.None;
						}

					case "System.Runtime.InteropServices.MemoryMarshal":
						// Lot of dangerous code in terms of type safety (for example allows reading types from flat array of bytes and so on)
						// but nothing linker unfriendly - everything is based on generics so the types used must be explicitly referenced
						// from calling code.
						return InterestingReason.None;

					case "System.Runtime.InteropServices.NativeLibrary":
						// This can bring new code into the process. It is native code, but what if it's something like C++/CLI
						// or something else which calls back to managed not through the PInvokes but through manual invocation
						// or through hosting API. In those cases this new code could depend on types/members which are trimmed
						// by the linker.
						return InterestingReason.ToInvestigate;

					case "System.Runtime.InteropServices.SafeBuffer":
						// Lot of potentially dangerous code in terms of type safety, but nothing linker related. All the code is based on generics
						// and so the types must be explicitly referenced by calling code.
						return InterestingReason.None;

					case "System.Runtime.InteropServices.SafeHandle":
						// Safe handle is about resource management, it has no way to import new functionality
						return InterestingReason.None;

				}

				return InterestingReason.None;
			}

			switch (method.DeclaringType.FullName) {
				case "System.Runtime.Loader.AssemblyDependencyResolver":
					// fine
					break;
				case "System.Runtime.Loader.AssemblyLoadContext":
					switch (method.Name) {
						case "GetAssemblyName":
						//case "Load":
						    // The Load method on ALC just returns null, and so is safe!
							// it's only unsafe overloads that might be a problem, and these would be considered
							// unsafe by virtue of calling somethig like LoadFromPath.
						case "LoadFromAssemblyName":
						case "LoadFromAssemblyPath":
						case "LoadFromInMemoryModule": // private
						case "LoadFromInMemoryModuleInternal": // private
						case "LoadFromPath": // private
						case "LoadFromStream": // private
											   // case "LoadTypeForWinRTTypeNameInContext": // private
											   // case "LoadTypeForWinRTTypeNameInContextInternal": // private
						case "LoadFromNativeImagePath":
						//case "LoadUnmanagedDll":
							// like Load, this one returns IntPtr.Zero. is actually safe!
						case "LoadUnmanagedDllFromPath":
							return InterestingReason.KnownReflection;
					}
					break;
			}

			if (method.DeclaringType.FullName.StartsWith ("System.Runtime.Serialization")) {
				// json serialization I guess? JsonFormatGeneratorStatigs::get_ExtensionDataProperty ends in GetProperty
				// should be detectable. they do things like typeof(ConcreteType).GetMethod("string")
				if (method.DeclaringType.FullName == "System.Runtime.Serialization.JsonFormatGeneratorStatics") {
					if (method.Name == "get_ExtensionDataProperty" ||
						method.Name == "get_GetCurrentMethod" ||
						method.Name == "get_TypeHandleProperty" ||
						method.Name == "get_MoveNextMethod" ||
						method.Name == "get_OnDeserializationMethod" ||
						method.Name == "get_SerializationExceptionCtor" || // this one does typeof(ConcreteType).GetConstructor()
						method.Name == "get_ExtensionDataObjectCtor") {
						return InterestingReason.JsonFormatGeneratorStatics;
					}
				}

				// XsdDataContractExporter::Export does datacontract stuff, ends up Serialization, createXmlFormatWriterDelegate
				// GenerateClassWriter, eventually end up in serialization CodeGenerator::.ctor, .cctor, which does GetProperty
				// CodeGenerator in System.Runtime.Serialization (in System.Private.DataContractSerialization)
				// does static property typeof(string).GetProperty("Length").GetMethod
				// should be afe.
				// but the static cctor does more things that look to be less safe.
				if (method.DeclaringType.FullName == "System.Runtime.Serialization.CodeGenerator") {
					if (method.Name == ".cctor") {
						return InterestingReason.CodeGeneratorStatic;
					}
					// needs investigation. some of the statics look like typeof(ConcreteType).GetMethod("string")
					// some have getters that take the type from elsewhere. I think the cctor itself is safe, and the getters are their own methods.
				}

				// similar to json, XmlFormatGeneratorStatics also have getters that would probably be detectable easily.
				if (method.DeclaringType.FullName == "System.Runtime.Serialization.XmlFormatGeneratorStatics") {
					if (method.Name == "get_ExtensionDataProperty" ||
						method.Name == "get_ExtensionDataSetExplicitMethodInfo" ||
						method.Name == "get_OnDeserializationMethod" ||
						// some of these use GetConstructor, taking binding flags that may need to be taken into account:
						// typeof(ExtensionDataObject).GetConstructor(Globals.ScanAllMembers, null, Array.Empty<Type>(), null));
						method.Name == "get_ExtensionDataObjectCtor" ||
						method.Name == "get_ExtensionDataSetExplicitMethodInfo" ||
						method.Name == "get_ExtensionDataProperty") {
						return InterestingReason.XmlFormatGeneratorStatics;
					}
				}

				// DataContractSet uses XmlFormatReaderGenerator, which does codegen.
				// trying to generate a load, (ldc(object)), it uses the static that points to Type.GetTypeFromHandle,
				// obtained using reflection. detectable. s_getTypeFromHandle = typeof(Type).GetMethod("GetTypeFromHandle");
				if (method.DeclaringType.FullName == "System.Runtime.Serialization.CodeGenerator") {
					if (method.Name == "get_GetTypeFromHandle" ||
						method.Name == "get_ObjectEquals") {
						return InterestingReason.CodeGeneratorGettersSimple;
					}
				}

				// serialization objectmanager DoValueTypeFixup tries to set value into a field of an object in a holder
				// look at parent for some field... if it's nullable, do GetField(nameof(value)). don't understand this.
				// I guess when deserializing an object graph, need to read in, build graph, then fix up pointers
				// objectholder has fixups. when fixing a pointer to boxed valuetype, DoValueTypeFixup does this.
				// GetField(nameof(value)) of the parent field's FieldType. I guess this is the field of a boxed valuetype?
				// for the nullable case. don't fully understand this.
				if (method.DeclaringType.FullName == "System.Runtime.Serialization.ObjectManager") {
					if (method.Name == "DoValueTypeFixup") {
						return InterestingReason.DeserializationValueTypeFixup;
					}
				}

				// xsd data contract schema exporter calls InvokeSchemaProviderMethod. this takes in a Type, does GetMethod(methodName)
				// methodName comes from XmlSchemaProviderAttribute on the type. would need to preserve MethodName.
				if (method.DeclaringType.FullName == "System.Runtime.Serialization.SchemaExporter") {
					if (method.Name == "InvokeSchemaProviderMethod") {
						return InterestingReason.XmlSchemaProviderAttributeMethodName;
					}
				}

				// exporting collection data contract, IsCollectionOrTryCreate
				// receives an itemType. gets its basetype, checks things, does GetConstructor, does MakeGenericType.GetMethod, etc...
				// BaseType is ok. globals TypeOf is ok.
				// GetGenericTypeDefinition is ok.
				// TypeOfKeyValue, etc... MakeGenericType is ok.
				// type.GetGenericArguments is ok.
				// does ICollection<T>... GetMethod(Globals.AddMethodName);
				// might be ok? unclear
				if (method.DeclaringType.FullName == "System.Runtime.Serialization.CollectionDataContract") {
					if (method.Name == "IsCollectionOrTryCreate") {
						return InterestingReason.CollectionDataContractRequiresAddMethodMaybeOk;
					}
				}

				// binaryformatter deserialize objectreader GetType wraps Type.GetType, calling into GetSimplyNamedTypeFromAssembly
				if (method.DeclaringType.FullName == "System.Runtime.Serialization.Formatters.Binary.ObjectReader") {
					if (method.Name == "GetSimplyNamedTypeFromAssembly") {
						return InterestingReason.BinaryFormatterGetType;
					}
				}

				// data contract serialization classdatacontract helper ctor does SetKeyValuePairAdapterFlags
				// which takes a type, and does GetMethod("GetKeyValuePair")
				if (method.DeclaringType.FullName == "System.Runtime.Serialization.ClassDataContract/ClassDataContractCriticalHelper") {
					if (method.Name == "SetKeyValuePairAdapterFlags") {
						return InterestingReason.DataContractSerializationReflection;
					}
				}

				// data contract exporter does xml serialization codegen BeginMethod
				if (method.DeclaringType.FullName == "System.Runtime.Serialization.CodeGenerator") {
					if (method.Name == "BeginMethod") {
						return InterestingReason.DataContractExporterCodeGenReflection;
					}
				}

				// exporting data contract uses XmlFormatReaderGenerator CriticalHelper, CreateObject
				if (method.DeclaringType.FullName == "System.Runtime.Serialization.XmlFormatReaderGenerator/CriticalHelper") {
					if (method.Name == "CreateObject") {
						return InterestingReason.XmlFormatReaderGeneratorSafe;
					}
				}

				// exporting datacontract, xmlformatreadergenerator helper does GetISerializableConstructor
				// does UnderlyingType.GetConstructor(Globals.ScanAllMembers
				if (method.DeclaringType.FullName == "System.Runtime.Serialization.ClassDataContract/ClassDataContractCriticalHelper") {
					if (method.Name == "GetISerializableConstructor") {
						return InterestingReason.DataContractSerializationReflection;
					}
				}

				// data contract helper uses understandable simple pattern to cache a method.
				if (method.DeclaringType.FullName == "System.Runtime.Serialization.PrimitiveDataContract") {
					if (method.Name == "get_XmlFormatReaderMethod") {
						return InterestingReason.DataContractSimple;
					}
				}

				// data contract serialization uses XmlFormatReaderGenerator, to wrap nullable object
				// does GetConstructor()). may be safe? if Nullable<T>::.ctor(T) is kept:
				// it does Globals.TypeOfNullable.MakeGenericType(innerType).GetConstructor(innerType)
				// where innertype is retrieved from parameter
				if (method.DeclaringType.FullName == "System.Runtime.Serialization.XmlFormatReaderGenerator/CriticalHelper") {
					if (method.Name == "WrapNullableObject") {
						return InterestingReason.XmlFormatReaderGeneratorMaybeSafe;
					}
				}

				if (method.DeclaringType.FullName == "System.Runtime.Serialization.SerializationException") {
					// The entire type is a simple exception - perfectly safe
					return InterestingReason.None;
				}

				if (method.DeclaringType.FullName == "System.Runtime.Serialization.SerializationInfo") {
					switch (method.Name) {
						case "get_AsyncDeserializationInProgress":
						case "get_DeserializationInProgress":
						case "ThrowIfDeserializationInProgress":
						case "StartDeserialization":
							// All of these are static and have no linker impact
							return InterestingReason.None;
					}
				}

				if (method.DeclaringType.FullName == "System.Runtime.Serialization.DeserializationTracker") {
					return InterestingReason.None;
				}

				return InterestingReason.SerializationBigHammer;
			}


			// ISerializable
			// case "System.Reflection.MethodInfo"
			// case "System.Reflection.PropertyInfo"
			// TODO: add hash check to check for typos above!

			return InterestingReason.None;
		}


		public bool IsKnownDangerousReflectionApiUNUSED (MethodDefinition method)
		{
			// Assembly.Load*
			if (method.DeclaringType.FullName == "System.Reflection.Assembly" &&
				(method.Name == "Load" ||
				 method.Name == "LoadFile" ||
				 method.Name == "LoadFrom" ||
				 method.Name == "LoadModule")) {
				foreach (var p in method.Parameters) {
					if (p.ParameterType.FullName == "System.String") {
						return true;
					}
					if (p.ParameterType.FullName == "System.Reflection.AssemblyName") {
						return true;
					}
				}
			}

			// ALC.
			if (method.DeclaringType.FullName == "System.Runtime.Loader.AssemblyLoadContext" &&
				(method.Name == "LoadFrom" ||
				 method.Name == "LoadFrom")) {
				foreach (var p in method.Parameters) {
					if (p.ParameterType.FullName == "System.String") {
						return true;
					}
					if (p.ParameterType.FullName == "System.Reflection.AssemblyName") {
						return true;
					}
				}
			}

			//
			if (method.DeclaringType.FullName == "System.Reflection.Assembly.Type" &&
				(method.Name == "GetProperty" ||
				 method.Name == "GetType" ||
				 method.Name == "InvokeMember" ||
				 method.Name == "GetMethods" ||
				 method.Name == "GetMethod" ||
				 method.Name == "GetMember" ||
				 method.Name == "GetMembers")) {
				foreach (var p in method.Parameters) {
					if (p.ParameterType.FullName == "System.Reflection.AssemblyName") {
						return true;
					}
				}
			}

			return false;
		}

		// if (method.DeclaringType.FullName.StartsWith("System.Reflection.Emit")) {
		//     if (method.DeclaringType.FullName.EndsWith("TypeNameBuilder")) {
		//         return InterestingReason.None;
		//     }
		// }
		// if (method.DeclaringType.FullName.StartsWith("System.Reflection")) {
		//     if (method.DeclaringType.FullName == "System.Reflection.RuntimeParameterInfo") {
		//         if (method.Name == ".ctor") {
		//             return InterestingReason.None;
		//         }
		//     }
		//     if (method.Name == "GetManifestResourceStream" ||
		//         method.Name == "TargetParameterCountException") {
		//         return InterestingReason.None;
		//     }
		//     foreach (var p in method.Parameters) {
		//         if (p.ParameterType.FullName == "System.String") {
		//             return InterestingReason.Reflection;
		//         }
		//         if (p.ParameterType.FullName == "System.Reflection.AssemblyName") {
		//             return InterestingReason.Reflection;
		//         }
		//     }
		// }
		// if (method.DeclaringType.FullName.StartsWith("System.Activator")) {
		//     foreach (var p in method.Parameters) {
		//         if (p.ParameterType.FullName == "System.String") {
		//             return InterestingReason.Reflection;
		//         }
		//     }
		// }
		// if (method.DeclaringType.FullName.StartsWith("System.Runtime.Loader.AssemblyLoadContext")) {
		//     if (method.Name.EndsWith(".ctor")) {
		//         return InterestingReason.None;
		//     }
		//     foreach (var p in method.Parameters) {
		//         if (p.ParameterType.FullName == "System.String") {
		//             return InterestingReason.Reflection;
		//         }
		//     }
		// }


		// // Linq.Expressions - 3800 or ~400 call stacks
		// if (method.DeclaringType.FullName.StartsWith("System.Reflection")) {
		//     // if (method.Name == "ToString") {
		//     //     return InterestingReason.None;
		//     // }
		//     // if (method.Name == "GetHashCode") {
		//     //     // a lot of ToString methods call System.Reflection.Assembly::GetHashCode
		//     //     // return InterestingReason.None;
		//     // }
		//     // if (method.Name == "get_FullName" ||
		//     //     method.Name == "get_Name" ||
		//     //     method.Name == "get_Message" ||
		//     //     method.Name == "get_DeclaringType" ||
		//     //     method.Name == "get_IsInvalid" ||
		//     //     method.Name == "AppendParameters" ||
		//     //     method.Name == "CreateTypeNameBuilder") {
		//     //     // get_TypeHandle?
		//     //     // GetElementType?
		//     //     return InterestingReason.Reflection;
		//     // }
		//     foreach (var p in method.Parameters) {
		//         if (p.ParameterType.Name == "Type") {
		//             return InterestingReason.Reflection;
		//         }
		//     }
		//     //return InterestingReason.Reflection;
		//     return InterestingReason.None;
		//     // return InterestingReason.Reflection;
		// }
		// if (method.DeclaringType.FullName.StartsWith("System.Activator")) {
		//     return InterestingReason.Reflection;
		// }
		// // if (method.IsPInvokeImpl) {
		// //     if (method.Name == "GetProcessorCount") {
		// //         return InterestingReason.None;
		// //     }
		// //     return InterestingReason.PInvoke;
		// // }

	}
}
