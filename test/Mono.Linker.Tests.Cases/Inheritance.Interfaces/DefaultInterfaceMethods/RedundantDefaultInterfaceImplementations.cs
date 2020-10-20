using System;
using System.Collections.Generic;
using System.Text;
using Mono.Linker.Tests.Cases.Expectations.Assertions;
using Mono.Linker.Tests.Cases.Expectations.Metadata;

namespace Mono.Linker.Tests.Cases.Inheritance.Interfaces.DefaultInterfaceMethods
{
#if false
	class RedundantDefaultInterfaceImplementations
	{
		public static void Main ()
		{
			(new Type () as IBase).Foo();
		}

		interface IBase
		{
			void Foo() {}
		}

		// IBase.Foo is implemented on IBar as IBar:IBase
		interface IBar : IBase
		{
		}

		interface IFoo : IBar
		{
			// this is an override of IBase.Foo
			void Foo() {}
		}

		// IFoo.Foo is implemented for Type as Type:IFoo
		// IBase.Foo is implemented for Type as IBar:IBase
		// IBase.Foo is implemented for Type as IFoo:Base
		// IBase.Foo is implemented for Type as IBar:IBase
		// IBase.Foo is implemented for Type as Type:IBase
		class Type : IFoo
		{
		}

		// default impls for Type are:
		// (Type as IBase).Foo -> IBase.Foo
		// (Type as IBar).Foo -> IBase.Foo
		// (Type as IFoo).Foo -> IFoo.Foo
	}
#endif
}
