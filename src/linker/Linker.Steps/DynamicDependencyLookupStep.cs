// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using Mono.Cecil;
using System.Diagnostics.CodeAnalysis;

#nullable enable

namespace Mono.Linker.Steps
{
	public class DynamicDependencyLookupStep : LoadReferencesStep
	{
		protected override void ProcessAssembly (AssemblyDefinition assembly)
		{
			var module = assembly.MainModule;

			foreach (var type in module.Types) {
				ProcessType (type);
			}
		}

		void ProcessType (TypeDefinition type)
		{
			if (type.HasMethods) {
				foreach (var method in type.GetMethods ()) {
					var methodDefinition = method.Resolve ();
					if (methodDefinition?.HasCustomAttributes != true)
						continue;

					ProcessDynamicDependencyAttributes (methodDefinition);
				}
			}

			if (type.HasFields) {
				foreach (var field in type.Fields) {
					var fieldDefinition = field.Resolve ();
					if (fieldDefinition?.HasCustomAttributes != true)
						continue;

					ProcessDynamicDependencyAttributes (fieldDefinition);
				}
			}

			if (type.HasNestedTypes) {
				foreach (var nestedType in type.NestedTypes) {
					ProcessType (nestedType);
				}
			}
		}

		void ProcessDynamicDependencyAttributes (IMemberDefinition member)
		{
			Debug.Assert (member is MethodDefinition || member is FieldDefinition);

			bool hasDynamicDependencyAttributes = false;
			foreach (var ca in member.CustomAttributes) {
				if (LinkerAttributesInformation.IsAttribute<DynamicDependencyAttribute> (ca.AttributeType))
					hasDynamicDependencyAttributes = true;

				if (!IsPreserveDependencyAttribute (ca.AttributeType))
					continue;
#if FEATURE_ILLINK
				Context.LogMessage (MessageContainer.CreateWarningMessage (Context,
					$"Unsupported PreserveDependencyAttribute on '{member}'. Use DynamicDependencyAttribute instead.",
					2033, MessageOrigin.TryGetOrigin (member)));
#else
				if (ca.ConstructorArguments.Count != 3)
					continue;

				if (!(ca.ConstructorArguments[2].Value is string assemblyName))
					continue;

				var assembly = Context.Resolve (new AssemblyNameReference (assemblyName, new Version ()));
				if (assembly == null)
					continue;
				ProcessReferences (assembly);
#endif
			}

			// avoid allocating linker attributes for members that don't need them
			if (!hasDynamicDependencyAttributes)
				return;

			var dynamicDependencies = member switch
			{
				MethodDefinition method => Context.Annotations.GetLinkerAttributes<DynamicDependency> (method),
				FieldDefinition field => Context.Annotations.GetLinkerAttributes<DynamicDependency> (field),
				_ => throw new InternalErrorException ("Unexpected member type")
			};

			foreach (var dynamicDependency in dynamicDependencies) {
				if (dynamicDependency.AssemblyName == null)
					continue;

				var assembly = Context.Resolve (new AssemblyNameReference (dynamicDependency.AssemblyName, new Version ()));
				if (assembly == null) {
					Context.LogMessage (MessageContainer.CreateWarningMessage (Context,
						$"Unresolved assembly '{dynamicDependency.AssemblyName}' in DynamicDependencyAttribute on '{member}'",
						2035, MessageOrigin.TryGetOrigin (member)));
					continue;
				}

				ProcessReferences (assembly);
			}

		}

		public static bool IsPreserveDependencyAttribute (TypeReference tr)
		{
			return tr.Name == "PreserveDependencyAttribute" && tr.Namespace == "System.Runtime.CompilerServices";
		}
	}
}
