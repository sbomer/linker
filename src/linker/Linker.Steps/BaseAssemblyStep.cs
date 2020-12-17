// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Mono.Cecil;

namespace Mono.Linker.Steps
{

	public abstract class BaseAssemblyStep : IStep
	{
		protected readonly AssemblyDefinition _assembly;

		protected LinkContext Context { get; private set; }

		public AnnotationStore Annotations => Context?.Annotations;

		public BaseAssemblyStep (AssemblyDefinition assembly)
		{
			_assembly = assembly;
		}

		public void Process (LinkContext context)
		{
			Context = context;

			Process ();
		}

		protected virtual void Process ()
		{
		}
	}
}
