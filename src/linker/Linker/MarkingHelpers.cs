using System;
using Mono.Cecil;

namespace Mono.Linker {

	// MarkModel
	public enum EntryKind {
		EmbeddedXml, // from embedded XML in an assembly
		RootAssembly, // from a root assembly
		AssemblyAction, // from an assembly action
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
	}
}
