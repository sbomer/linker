using Mono.Cecil;

namespace Mono.Linker
{
	public interface IMarker
	{
		void MarkType (TypeDefinition type, in DependencyInfo reason, IMemberDefinition sourceLocationMember);
		void MarkField (FieldDefinition field, in DependencyInfo reason);
		void MarkMethod (MethodDefinition method, in DependencyInfo reason, IMemberDefinition sourceLocationMember);
		void MarkEvent (EventDefinition @event, in DependencyInfo reason);
		void MarkProperty (PropertyDefinition property, in DependencyInfo reason);
	}
}
