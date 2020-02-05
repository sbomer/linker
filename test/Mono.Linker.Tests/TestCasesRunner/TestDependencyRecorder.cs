using System.Collections.Generic;
using Mono.Cecil;

namespace Mono.Linker.Tests.TestCasesRunner
{
	public class TestDependencyRecorder : IDependencyRecorder
	{
		public struct Dependency
		{
			public string Source;
			public string Target;
			public bool Marked;
		}

		public List<Dependency> Dependencies = new List<Dependency> ();

		public void RecordDependency (object source, object target, bool marked)
		{
			Dependencies.Add (new Dependency () {
				Source = source.ToString (),
				Target = target.ToString (),
				Marked = marked
			});
		}

		public void RecordDirectCall (MethodDefinition soure, MethodDefinition target)
		{
			// do nothing, as this recorder doesn't treat direct calls specially.
		}

		public void RecordVirtualCall (MethodDefinition soure, MethodDefinition target)
		{
			// do nothing, as this recorder doesn't treat virtual calls specially.
		}

		public void RecordOverride (MethodDefinition soure, MethodDefinition target)
		{
			// do nothing, as this recorder doesn't treat overrides specially.
		}

		public void RecordCctorDependency (TypeDefinition source, MethodDefinition cctor)
		{
		}

		public void RecordTypeDependency (MethodDefinition source, TypeDefinition type)
		{
		}

		public void RecordCctorFieldAccessDependency (MethodDefinition source, MethodDefinition cctor)
		{
		}

		public void RecordEntry (MethodDefinition entry)
		{
		}
	}
}
