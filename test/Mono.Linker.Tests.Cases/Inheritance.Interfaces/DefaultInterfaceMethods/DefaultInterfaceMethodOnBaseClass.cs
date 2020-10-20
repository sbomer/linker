using System;
using System.Collections.Generic;
using System.Text;
using Mono.Linker.Tests.Cases.Expectations.Assertions;
using Mono.Linker.Tests.Cases.Expectations.Metadata;

namespace Mono.Linker.Tests.Cases.Inheritance.Interfaces.DefaultInterfaceMethods
{
#if NETCOREAPP
	class DefaultInterfaceMethodOnBaseClass
	{
		public static void Main ()
		{
//			((IBase) new Derived ()).Frob ();
		}
#if FALSE
		[Kept]
		interface IBase
		{
			[Kept]
			void Frob () { }
		}

		interface IUnusedInterface
		{
			void UnusedDefaultImplementation () { }
		}

		[Kept]
		[KeptInterface (typeof (IBase))]
		class Base : IBase, IUnusedInterface
		{
		}

		[Kept]
		[KeptBaseType (typeof (Base))]
		class Derived : Base
		{
		}
#endif
	}
#endif
}
