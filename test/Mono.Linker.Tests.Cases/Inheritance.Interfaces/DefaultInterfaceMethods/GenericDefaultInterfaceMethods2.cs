using System;
using System.Collections.Generic;
using System.Text;
using Mono.Linker.Tests.Cases.Expectations.Assertions;

namespace Mono.Linker.Tests.Cases.Inheritance.Interfaces.DefaultInterfaceMethods
{
#if NETCOREAPP
	class GenericDefaultInterfaceMethods2
	{
		public static void Main ()
		{
			// Should keep the object interfaces, not others!
			((IFoo<object>) new BarBaz ()).Method (12);
		}

		[Kept]
		interface IFoo<T>
		{
			[Kept]
			void Method (T x);

		}

		// Can be removed!
		interface IBar<T> : IFoo<T>
		{
			void IFoo<T>.Method (T x)
			{
			}
		}

		[Kept]
		[KeptInterface (typeof (IFoo<object>))]
		interface IBaz : IFoo<object>
		{
			[Kept]
			void IFoo<object>.Method (object o)
			{
			}
		}

		[Kept]
		[KeptInterface (typeof (IBaz))]
		[KeptInterface (typeof (IFoo<object>))]
		class BarBaz : IBar<int>, IBaz
		{
		}
	}
#endif
}
