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
	}
}
