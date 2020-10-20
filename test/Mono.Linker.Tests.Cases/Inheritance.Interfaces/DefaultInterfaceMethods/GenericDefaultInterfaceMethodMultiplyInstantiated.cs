using System;
using System.Collections.Generic;
using System.Text;
using Mono.Linker.Tests.Cases.Expectations.Assertions;

namespace Mono.Linker.Tests.Cases.Inheritance.Interfaces.DefaultInterfaceMethods
{
#if NETCOREAPP
	class GenericDefaultInterfaceMethodMultiplyInstantiated
	{
		public static void Main ()
		{
			((IFoo<int>) new Foo ()).Method (12);
		}

		interface IFoo<T>
		{
			[Kept]
			void Method (T x);

		}

		[KeptInterface (typeof (IFoo<int>))]
		interface IFooInt : IFoo<int>
		{
			[Kept]
			void IFoo<int>.Method(int x) { }
		}

		[Kept] // SHOULD BE REMOVED!
		[KeptInterface (typeof (IFoo<object>))]
		interface IFooObject : IFoo<object>
		{
			[Kept]
			void IFoo<object>.Method(object x) { }
		}

		[KeptMember (".ctor()")]
		[KeptInterface (typeof (IFooInt))]
		[KeptInterface (typeof (IFoo<int>))]
		[KeptInterface (typeof (IFooObject))] // SHOULD BE REMOVED
		[KeptInterface (typeof (IFoo<object>))] // SHOULD BE REMOVED
		class Foo : IFooInt, IFooObject { }
	}
#endif
}
