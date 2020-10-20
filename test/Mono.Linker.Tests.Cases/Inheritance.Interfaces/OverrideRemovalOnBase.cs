using Mono.Linker.Tests.Cases.Expectations.Assertions;
using Mono.Linker.Tests.Cases.Expectations.Metadata;
using Mono.Linker.Tests.Cases.Inheritance.Interfaces.Dependencies;

namespace Mono.Linker.Tests.Cases.Inheritance.Interfaces
{
	[SetupLinkerArgument ("--disable-opt", "unusedinterfaces")]
	[SetupCompileBefore ("base.dll", new[] { "Dependencies/BaseWithVirtual.cs" })]
	[SetupLinkerArgument ("--disable-opt", "overrideremoval")]
	[SetupLinkerArgument ("--enable-opt", "overrideremoval", "base")]
	[RemovedMemberInAssembly ("base.dll", typeof (BaseWithVirtual), "Method()")]
	public class OverrideRemovalOnBase
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
		// [KeptInterface (typeof (IFoo))] // IFoo is removed... even if we disable the unusedinterfaces opt.
		class Derived : BaseWithVirtual, IFoo { }
	}
}
