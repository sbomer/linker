using Mono.Linker.Tests.Cases.Expectations.Assertions;
using Mono.Linker.Tests.Cases.Expectations.Metadata;

namespace Mono.Linker.Tests.Cases.Inheritance.Interfaces.OnReferenceType
{
	[SetupLinkerArgument ("--disable-opt", "unusedinterfaces")]
	public class UnusedInterfaceImplIsRemoved
	{
		public static void Main ()
		{
			Derived d = MarkDerived ();
			(d as IFoo).Method ();
		}

		[Kept]
		static Derived MarkDerived () => null;

		[Kept]
		interface IFoo
		{
			[Kept]
			void Method ();
		}

		[Kept]
		class Base {
			// THIS SHOULD BE KEPT!
			public virtual void Method () { }
		}

		[Kept]
		[KeptBaseType (typeof (Base))]
		class Derived : Base, IFoo { }
	}
}