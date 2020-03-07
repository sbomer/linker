using System;
using Mono.Cecil;

namespace Mono.Linker {

	// MarkModel
	public enum EntryKind {
		XmlDescriptor, // from embedded XML in an assembly
		RootAssembly, // from a root assembly
		AssemblyAction, // from an assembly action
		AssemblyOrModuleCustomAttribute, // currently, linker marks custom attributes in assemblies
			// pretty aggressively, but may not keep the assembly anyway.
		//ModuleCustomAttribute, // similar.  it marks ca's in modules up-front.
			// and we don't know at that point if the module will be kept.
		UnmarkedAttributeDependency, // DebuggerDisplayAttribute targets may be kept without the attribute being kept.
		// mark the target as an entry.
	}
	public struct EntryInfo {
		public EntryKind kind;
		public object source;
		public object entry;
	}

	public class MarkingHelpers {
		protected readonly LinkContext _context;

		public MarkingHelpers (LinkContext context)
		{
			_context = context;
		}


		public void MarkEntryMethod (MethodDefinition method, EntryInfo info)
		{
			// called for xml/roots
			_context.Annotations.Recorder.RecordEntryMethod (method, info);
			_context.Annotations.Mark (method);
		}

		public void MarkEntryType (TypeDefinition type, EntryInfo info)
		{
			if (info.source == null) {
				throw new Exception("null info!");
			}
			// called for copy/save, or for xml/roots.
			_context.Annotations.Recorder.RecordEntryType (type, info);
			_context.Annotations.Mark (type);
		}

		public void MarkEntryField (FieldDefinition field, EntryInfo info)
		{
			_context.Annotations.Recorder.RecordEntryField (field, info);
			_context.Annotations.Mark (field);
		}

		public void MarkExportedType (ExportedType type, ModuleDefinition module)
		{
			_context.Annotations.Mark (type);
			if (_context.KeepTypeForwarderOnlyAssemblies)
				_context.Annotations.Mark (module);
		}

		public void MarkEntryCustomAttribute (CustomAttribute ca, EntryInfo info)
		{
			// this helper is different - it's called from Annotations, which already does Mark().
			// _context.Annotations.Mark ()
			_context.Annotations.Recorder.RecordAssemblyCustomAttribute (ca, info);
		}

		public void MarkMethodSpec (MethodSpecification spec, DependencyInfo reason) {
			_context.Annotations.Recorder.RecordMethodSpecWithReason (reason, spec);
		}

		// TODO: get rid of these unnecessary helpers
		public void MarkTypeSpec (TypeSpecification spec, DependencyInfo reason) {
			// this is not marked, but we still put it in the graph.
			_context.Annotations.Recorder.RecordTypeSpecWithReason (reason, spec);
		}

		public void MarkFieldOnGenericInstance (FieldReference field, DependencyInfo reason) {
			_context.Annotations.Recorder.RecordFieldOnGenericInstance (reason, field);
		}

		public void MarkProperty (PropertyDefinition property, DependencyInfo reason) {
			_context.Annotations.Recorder.RecordPropertyWithReason (reason, property);
		}

		public void MarkEvent (EventDefinition evt, DependencyInfo reason) {
			_context.Annotations.Recorder.RecordEventWithReason (reason, evt);
		}

	}
}
