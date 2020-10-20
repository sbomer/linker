using System;
using System.Collections.Generic;
using System.Text;
using Mono.Linker.Tests.Cases.Expectations.Assertions;

namespace Mono.Linker.Tests.Cases.Inheritance.Interfaces.DefaultInterfaceMethods
{
#if NETCOREAPP
	class UnusedDefaultInterfaceImplementation
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

		interface IDefaultImpl : IFoo
		{
			// this is found during mapvirtualmethods, as explicit override
			// NOTE: it is never tracked as a default interface implementation!
			// because we only look for a default implementation of an interface method
			// if we couldn't find a normal implementation on a type with interfaces.
			// mapinterfacelogic will see IFoo.InterfaceMethod,
			// but won't see it implemented on this type because name doesn't match.
			// doesn't look for overrides.
			// looks for default implementation - but only on interfaces (not current type)
			void IFoo.InterfaceMethod ()
			{
			}
		}

		[Kept]
		[KeptInterface (typeof (IFoo))]
		class Foo : IDefaultImpl
		{
			[Kept]
			public Foo () { }

			[Kept]
			// this is found during MapInterfaceMethods, as on-type implementation of IFoo.InterfaceMethod
			public void InterfaceMethod ()
			{
			}
		}
	}
#endif
}
