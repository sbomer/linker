using System;
using System.Collections.Generic;
using System.Text;
using Mono.Linker.Tests.Cases.Expectations.Assertions;
using Mono.Linker.Tests.Cases.Expectations.Metadata;

namespace Mono.Linker.Tests.Cases.Inheritance.VirtualMethods
{
#if NETCOREAPP
	[Define ("IL_ASSEMBLY_AVAILABLE")]
	[SetupCompileBefore ("library.dll", new[] { "Dependencies/BaseVirtualRemovedIfNotCalledDirectly.il" })]

	[KeptBaseOnTypeInAssembly ("library.dll", "Derived", "library.dll", "Base")]
	[KeptMemberInAssembly ("library.dll", "Derived", "M()")]
	[RemovedMemberInAssembly ("library.dll", "Base", "M()")]
	class BaseVirtualRemovedIfNotCalledDirectly
	{
		static void Main ()
		{
#if IL_ASSEMBLY_AVAILABLE
			Helper.CallDerivedMethodDirectly ();
#endif
		}
	}
#endif
}
