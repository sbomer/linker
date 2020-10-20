using Mono.Linker.Tests.Cases.Expectations.Assertions;
using Mono.Linker.Tests.Cases.Expectations.Metadata;
using Mono.Linker.Tests.Cases.Inheritance.Interfaces.Dependencies;

namespace Mono.Linker.Tests.Cases.Inheritance.Interfaces
{
	[SetupLinkerArgument ("--disable-opt", "unusedinterfaces")]
	[SetupCompileBefore ("base.dll", new[] { "Dependencies/BaseWithVirtual.cs" })]
	[SetupLinkerArgument ("--enable-opt", "overrideremoval")]
	[SetupLinkerArgument ("--disable-opt", "overrideremoval", "base")]
	[KeptMemberInAssembly ("base.dll", typeof (BaseWithVirtual), "Method()")]

	// It's unclear what per-assembly overrideremoval should do.
	// Either:
	// 1. disabling the optimization on base ensures that its methods are kept
	//    if a derived type (from any assembly) requiring that method is marked
	// 2. disabling the optimization on derived ensures that the base method (from possibly another assembly) is
	//    kept if the derived type requires that the method is marked
	// We seem to remove it if EITHER the base or derived type has the optimization enabled.
	public class OverrideRemovalOnDerived
	{
		public static void Main ()
		{
			Derived d = MarkDerived ();
			(d as IFoo).Method ();
		}

		[Kept]
		static Derived MarkDerived () => null;

		interface IFoo
		{
			[Kept]
			void Method ();
		}

		[Kept]
		[KeptBaseType (typeof (BaseWithVirtual))]
		// [KeptInterface (typeof (IFoo))] IFoo is removed, even if we disable the unusedinterface opt
		class Derived : BaseWithVirtual, IFoo { }
	}
}
