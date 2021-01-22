﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Mono.Cecil;

namespace Mono.Linker.Steps
{

	public interface IMarkAssemblyStep
	{
		void Initialize (LinkContext context);
		void ProcessAssembly (AssemblyDefinition assembly);
	}
}