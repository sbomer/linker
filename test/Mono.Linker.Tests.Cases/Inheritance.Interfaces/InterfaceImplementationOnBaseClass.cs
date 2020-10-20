using System;
using System.Collections.Generic;
using System.Text;
using Mono.Linker.Tests.Cases.Expectations.Assertions;

namespace Mono.Linker.Tests.Cases.Inheritance.Interfaces
{
#if false
	class InterfaceImplementationOnBaseClass
	{
		public static void Main ()
		{
			((IFoo) new Foo ()).InterfaceMethod ();
		}

		[Kept]
		interface IFoo
		{
			[Kept]
			void InterfaceMethod ();
		}

		class Base : IFoo
		{
			void IFoo.InterfaceMethod ()
			{
			}
		}

		[Kept]
		[KeptInterface (typeof (IFoo))]
		class Foo : Base, IFoo
		{
			[Kept]
			public Foo () { }

		}
	}
#endif
}
