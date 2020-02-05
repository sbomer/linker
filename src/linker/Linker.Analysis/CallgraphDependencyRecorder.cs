using Mono.Cecil;
using System.Collections.Generic;
using System.Diagnostics;

namespace Mono.Linker.Analysis
{
	class CallgraphDependencyRecorder : IDependencyRecorder
	{
		public List<(MethodDefinition source, MethodDefinition target)> Dependencies { get; } = 
			new List<(MethodDefinition source, MethodDefinition target)> ();

		public HashSet<(MethodDefinition source, MethodDefinition target)> DirectCalls { get; } =
			new HashSet<(MethodDefinition source, MethodDefinition target)> ();
		public HashSet<(MethodDefinition source, MethodDefinition target)> VirtualCalls { get; } =
			new HashSet<(MethodDefinition source, MethodDefinition target)> ();
		public HashSet<(MethodDefinition source, MethodDefinition target)> Overrides { get; } =
			new HashSet<(MethodDefinition source, MethodDefinition target)> ();

		public HashSet<(TypeDefinition source, MethodDefinition target)> CctorDependencies { get; } =
			new HashSet<(TypeDefinition source, MethodDefinition target)> ();

		public HashSet<(MethodDefinition source, TypeDefinition target)> TypeDependencies { get; } =
			new HashSet<(MethodDefinition source, TypeDefinition target)> ();

		public HashSet<(MethodDefinition source, MethodDefinition target)> CctorFieldAccessDependencies { get; } =
			new HashSet<(MethodDefinition source, MethodDefinition target)> ();

		public HashSet<MethodDefinition> EntryMethods { get; } =
			new HashSet<MethodDefinition> ();

		public void RecordDependency (object source, object target, bool marked)
		{
			if ((source is MethodDefinition sourceMD) && (target is MethodDefinition targetMD)) {
				Dependencies.Add ((sourceMD, targetMD));
			}
		}

		public void RecordDirectCall (MethodDefinition source, MethodDefinition target)
		{
			if (source == null || target == null) 
				return;
			DirectCalls.Add ((source, target));
		}

		public void RecordVirtualCall (MethodDefinition source, MethodDefinition target)
		{
			if (source == null || target == null) 
				return;
			VirtualCalls.Add  ((source, target));
		}

		public void RecordOverride (MethodDefinition source, MethodDefinition target)
		{
			if (source == null || target == null) 
				return;
			Overrides.Add ((source, target));
		}

		public void RecordCctorDependency (TypeDefinition source, MethodDefinition cctor)
		{
			if (cctor == null)
				return;
			Debug.Assert(cctor.IsStaticConstructor());
			CctorDependencies.Add((source, cctor));
			System.Console.WriteLine("cctor dependency: " + source + " -> " + cctor);
		}

		public void RecordTypeDependency (MethodDefinition source, TypeDefinition type)
		{
			if (type == null)
				return;
			TypeDependencies.Add((source, type));
		}

		public void RecordCctorFieldAccessDependency (MethodDefinition source, MethodDefinition cctor)
		{
			if (cctor == null)
				return;
			Debug.Assert(cctor.IsStaticConstructor());
			Debug.Assert(cctor.DeclaringType.IsBeforeFieldInit);
			CctorFieldAccessDependencies.Add((source, cctor));
			System.Console.WriteLine("cctor field access: " + source + " -> " + cctor);
		}

		public void RecordEntry (MethodDefinition entry)
		{
			if (entry == null)
				return;
			EntryMethods.Add(entry);
		}

	}
}
