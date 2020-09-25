using System;
using Mono.Linker.Tests.Cases.Expectations.Assertions;
using Mono.Linker.Tests.Cases.Expectations.Metadata;
using Mono.Linker.Tests.Cases.Inheritance.Interfaces.Dependencies;

namespace Mono.Linker.Tests.Cases.Inheritance.Interfaces
{
	[SetupCompileBefore ("interface.dll", new[] { "Dependencies/InterfaceV1.cs" })]
	// After compiling the test case we then replace the interface assembly with V2
	[SetupCompileAfter ("interface.dll", new[] { "Dependencies/InterfaceV2.cs" })]
	// Ensure that we are able to resolve IFooDefault from the expectations assembly
	[CompileAfterForExpectations]

	[KeptInterfaceOnTypeInAssembly (
		"interface.dll", "Mono.Linker.Tests.Cases.Inheritance.Interfaces.Dependencies.IFoo",
		"interface.dll", "Mono.Linker.Tests.Cases.Inheritance.Interfaces.Dependencies.IFooDefault")]
	[KeptMemberInAssembly (
		"interface.dll", "Mono.Linker.Tests.Cases.Inheritance.Interfaces.Dependencies.IFooDefault",
		"MethodDefault")]
	class RecursiveInterfaces
	{
		public static void Main ()
		{
			UseIFoo.CallIFooMethod(new Foo());
		}

		[KeptInterface (typeof (IFoo))]
		[KeptMember (".ctor()")]
		public class Foo : IFoo
		{
			[Kept]
			public void Method() {
				Console.WriteLine("Foo.Method");
			}

			// This method is an interface implementation of IFooDefault
			// even though Foo doesn't have an explicit impl of IFooDefault.
			[Kept]
			public virtual void MethodDefault() { 
				Console.WriteLine("Foo.MethodDefault");
			}
		}
	}
}
