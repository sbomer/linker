using Mono.Cecil;
using System.Diagnostics;

namespace Mono.Linker
{
	class EarlyMarker : IMarker
	{
		LinkContext Context { get; }
		AnnotationStore Annotations => Context.Annotations;

		public EarlyMarker (LinkContext context)
		{
			Context = context;
		}

		public void MarkType (TypeDefinition type, in DependencyInfo reason, IMemberDefinition sourceLocationMember)
		{
			Debug.Assert (sourceLocationMember == null);
			Annotations.Mark (type, reason);
		}

		public void MarkField (FieldDefinition field, in DependencyInfo reason)
		{
			Annotations.Mark (field, reason);
		}

		public void MarkMethod (MethodDefinition method, in DependencyInfo reason, IMemberDefinition sourceLocationMember)
		{
			Debug.Assert (sourceLocationMember == null);
			Annotations.Mark (method, reason);
		}

		public void MarkEvent (EventDefinition @event, in DependencyInfo reason)
		{
			Annotations.Mark (@event, reason);
		}

		public void MarkProperty (PropertyDefinition property, in DependencyInfo reason)
		{
			Annotations.Mark (property, reason);
		}
	}
}