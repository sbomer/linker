using Mono.Cecil;

namespace Mono.Linker
{
	public class Recorder
	{
		protected readonly LinkContext context;

		public Recorder (LinkContext context)
		{
			this.context = context;
		}

		// MarkEntireAssembly(a) & typeInAssembly(t, a) => MarkEntireType(t)
		public void MarkEntireTypeInAssembly (TypeDefinition type, AssemblyDefinition assembly)
		{
			context.Annotations.Mark (type);
		}

		// MarkEntireType(t) & nestedType(n, t) => MarkEntireType(t)
		public void MarkEntireTypeNested (TypeDefinition nested, TypeDefinition parent)
		{
			context.Annotations.Mark (nested);
		}

		// MarkUserDependency(t, "*") => MarkEntireType(t)
		public void MarkEntireTypeForUserDependency (TypeDefinition type, CustomAttribute ca)
		{
			context.Annotations.Mark (type);
		}

	}
}
