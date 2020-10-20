﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more infortion.

using Mono.Cecil;
using System.Collections.Generic;
namespace Mono.Linker
{
	public static class MethodReferenceExtensions
	{
		public static string GetDisplayName (this MethodReference method)
		{
			var sb = new System.Text.StringBuilder ();

			// Append parameters
			sb.Append ("(");
			if (method.HasParameters) {
				for (int i = 0; i < method.Parameters.Count - 1; i++)
					sb.Append (method.Parameters[i].ParameterType.GetDisplayNameWithoutNamespace ()).Append (',');

				sb.Append (method.Parameters[method.Parameters.Count - 1].ParameterType.GetDisplayNameWithoutNamespace ());
			}

			sb.Append (")");

			// Insert generic parameters
			if (method.HasGenericParameters) {
				TypeReferenceExtensions.PrependGenericParameters (method.GenericParameters, sb);
			}

			// Insert method name
			if (method.Name == ".ctor")
				sb.Insert (0, method.DeclaringType.Name);
			else
				sb.Insert (0, method.Name);

			// Insert declaring type name and namespace
			sb.Insert (0, '.').Insert (0, method.DeclaringType.GetDisplayName ());

			return sb.ToString ();
		}

		public static IEnumerable<MethodReference> GetInflatedOverrides (this MethodReference methodRef)
		{
			var methodDef = methodRef.Resolve ();
			if (methodDef == null)
				yield break;
			if (!methodDef.HasOverrides)
				yield break;
			foreach (var @override in methodDef.Overrides) {
				var overrideDef = @override.Resolve ();
				if (overrideDef == null)
					continue;

				TypeReference overrideDeclaringType = null;
				if (methodRef.DeclaringType is GenericInstanceType methodGenericDeclaringType) {
					overrideDeclaringType = TypeReferenceExtensions.InflateGenericType (methodGenericDeclaringType, @override.DeclaringType);
				} else {
					overrideDeclaringType = @override.DeclaringType;
				}

				if (overrideDeclaringType is GenericInstanceType genericInstanceType) {
					yield return TypeReferenceExtensions.MakeMethodReferenceForGenericInstanceType (genericInstanceType, overrideDef);
				} else {
					yield return overrideDef;
				}
			}
		}

		public static TypeReference GetReturnType (this MethodReference method)
		{
			if (method.DeclaringType is GenericInstanceType genericInstance)
				return TypeReferenceExtensions.InflateGenericType (genericInstance, method.ReturnType);

			return method.ReturnType;
		}

		public static TypeReference GetParameterType (this MethodReference method, int parameterIndex)
		{
			if (method.DeclaringType is GenericInstanceType genericInstance)
				return TypeReferenceExtensions.InflateGenericType (genericInstance, method.Parameters[parameterIndex].ParameterType);

			return method.Parameters[parameterIndex].ParameterType;
		}

		public static bool IsDeclaredOnType (this MethodReference method, string namespaceName, string typeName)
		{
			return method.DeclaringType.IsTypeOf (namespaceName, typeName);
		}

		public static bool HasParameterOfType (this MethodReference method, int parameterIndex, string namespaceName, string typeName)
		{
			return method.Parameters.Count > parameterIndex && method.Parameters[parameterIndex].ParameterType.IsTypeOf (namespaceName, typeName);
		}
	}
}
