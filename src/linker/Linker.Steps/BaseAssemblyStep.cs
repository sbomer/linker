// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Mono.Cecil;

namespace Mono.Linker.Steps
{

	public abstract class BaseAssemblyStep : IStep
	{
		private LinkContext context;

		protected readonly AssemblyDefinition assembly;

		public BaseAssemblyStep (AssemblyDefinition assembly)
		{
			this.assembly = assembly;
		}

		public LinkContext Context {
			get { return context; }
		}

		public AnnotationStore Annotations {
			get { return context.Annotations; }
		}

		public void Process (LinkContext context)
		{
			this.context = context;

			Process ();
		}

		protected virtual void Process ()
		{
		}
	}
}
