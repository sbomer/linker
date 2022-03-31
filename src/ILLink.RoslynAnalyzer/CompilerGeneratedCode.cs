// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using ILLink.Shared.TypeSystemProxy;

namespace ILLink.Shared
{
	// Currently this is implemented using heuristics
	// TODO: merge with CompilerGeneratedState
	public partial class CompilerGeneratedCode
	{
		internal static partial bool IsLocalFunction (MethodProxy method) {
			// TODO: lambdas
			return method.Method.ContainingSymbol is IMethodSymbol;
		}
		// {
		// 	return method.Name.Contains ("<");
		// }

	}
}
