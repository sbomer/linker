using Mono.Linker.Tests.Cases.Expectations.Assertions;

namespace Mono.Linker.Tests.Cases.Inheritance.VirtualMethods
{
	public class NewSlotVirtualIsTreatedAsOverride
	{
		public static void Main ()
		{
			(new Derived () as Base).Method ();
		}

		[KeptMember (".ctor()")]
		class Base
		{
			[Kept]
			public virtual void Method ()
			{
			}
		}

		[Kept]
		[KeptMember (".ctor()")]
		[KeptBaseType (typeof (Base))]
		class Derived : Base
		{
			[Kept] // This doesn't need to be kept, but it currently is.
			public virtual new void Method ()
			{
			}
		}
	}
}