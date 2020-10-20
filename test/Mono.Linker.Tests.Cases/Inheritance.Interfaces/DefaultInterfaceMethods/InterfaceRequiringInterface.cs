using System;
using System.Collections.Generic;
using System.Text;
using Mono.Linker.Tests.Cases.Expectations.Assertions;
using Mono.Linker.Tests.Cases.Expectations.Metadata;

namespace Mono.Linker.Tests.Cases.Inheritance.Interfaces.DefaultInterfaceMethods
{
#if NETCOREAPP
	[Define ("IL_ASSEMBLY_AVAILABLE")]
	[SetupCompileBefore ("library.dll", new[] { "Dependencies/InterfaceRequiringInterface.il" })]
//	[KeptInterfaceOnTypeInAssembly ("library.dll", "IFoo", "library.dll", "IFooDefault")]
//	[KeptInterfaceOnTypeInAssembly ( "library.dll", "Foo", "library.dll", "IFoo")]
//	[KeptMemberInAssembly ("library.dll", "Foo", "library.dll", "Method")]
//	[KeptMemberInAssembly ("library.dll", "Foo", "library.dll", "Default")]
//	[KeptTypeInAssembly ("library.dll", "Foo")]
//	[KeptTypeInAssembly ("library.dll", "IFoo")]
	[KeptMemberInAssembly ("library.dll", "Foo", "Default()")]
	[KeptInterfaceOnTypeInAssembly ("library.dll", "Foo", "library.dll", "IFoo")]
	[KeptInterfaceOnTypeInAssembly ("library.dll", "IFoo", "library.dll", "IFooDefault")]
	[KeptMemberInAssembly ("library.dll", "IFooDefault", "Default()")]
	class InterfaceRequiringInterface
	{
		static void Main ()
		{
#if IL_ASSEMBLY_AVAILABLE
			(new Foo () as IFoo).Default ();
#endif
		}
	}
#endif
}
