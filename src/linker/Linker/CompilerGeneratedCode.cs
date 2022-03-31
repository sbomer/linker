// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using ILLink.Shared.TypeSystemProxy;
using Mono.Linker;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace ILLink.Shared
{
	// Currently this is implemented using heuristics
	// TODO: merge with CompilerGeneratedState
	public partial class CompilerGeneratedCode
	{
		internal static partial bool IsLocalFunction (MethodProxy method) {
			// TODO: lambdas
			return method.Method.Name.Contains ('<');
		}

		static bool HasDisplayClassLocal (MethodBody body, TypeDefinition displayClass)
		{
			foreach (var localVar in body.Variables) {
				if (localVar.VariableType == displayClass)
					return true;
			}
			return false;
		}

		internal static bool IsHoistedParameter (FieldDefinition field) {
			// Debug.Assert (field.DeclaringType.Name.Contains("DisplayClass"));
			if (!field.DeclaringType.Name.Contains("DisplayClass"))
				return false;
			// TODO: detect parameters of state machines too!
			var displayClass = field.DeclaringType;
			var identifier = displayClass.Name[^3..];
			bool foundMethod = false;
			foreach (var method in displayClass.DeclaringType.Methods) {
				// Look for the method that uses this type as a local.
				if (method.Body == null)
					continue;

				if (!HasDisplayClassLocal (method.Body, displayClass))
					continue;

				foundMethod = true;

				// See if this field comes from one of the method's parameters.
				foreach (var param in method.Parameters) {
					if (param.Name.Equals(field.Name)) {
						// it's a match!
						return true;
						// TODO: match types too?
					}
				}
			}
			Debug.Assert (foundMethod);
			return false;
		}
	}
}
