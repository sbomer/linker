using System;
using System.Reflection;
using System.Reflection.Emit;
using Mono.Linker.Tests.Cases.Expectations.Assertions;
using Mono.Linker.Tests.Cases.Expectations.Metadata;

namespace Mono.Linker.Tests.Cases.Sealer
{
	[SetupLinkerArgument ("--enable-opt", "sealer")]
	[AddedPseudoAttribute ((uint) TypeAttributes.Sealed)]
	public class TypesCanBeSealed
	{
		public static void Main ()
		{
			Type t;
			t = typeof (SimpleNestedClass);
			t = typeof (SimpleNestedIface);

			t = typeof (Data.SimpleClass);
			t = typeof (Data.AlreadySealed);
			t = typeof (Data.Derived);
			t = typeof (Data.DerivedWithNested.Nested);
			t = typeof (Data.DerivedWithNested);
			t = typeof (Data.BaseWithUnusedDerivedClass);
		}

		[Kept]
		[AddedPseudoAttribute ((uint) TypeAttributes.Sealed)]
		class SimpleNestedClass
		{
		}

		[Kept]
		interface SimpleNestedIface
		{
		}
	}
}

namespace Mono.Linker.Tests.Cases.Sealer.Data
{
	[Kept]
	[AddedPseudoAttribute ((uint) TypeAttributes.Sealed)]
	class SimpleClass
	{
	}

	[Kept]
	static class AlreadySealed
	{
	}

	[Kept]
	class Base
	{
	}

	[Kept]
	[KeptBaseType (typeof (Base))]
	[AddedPseudoAttribute ((uint) TypeAttributes.Sealed)]
	class Derived : Base
	{
	}

	[Kept]
	class BaseWithNested
	{
		[Kept]
		[AddedPseudoAttribute ((uint) TypeAttributes.Sealed)]
		internal class Nested
		{
		}
	}

	[Kept]
	[KeptBaseType (typeof (BaseWithNested))]
	[AddedPseudoAttribute ((uint) TypeAttributes.Sealed)]
	class DerivedWithNested : BaseWithNested
	{
	}

	class UnusedClass
	{
	}

	[Kept]
	[AddedPseudoAttribute ((uint) TypeAttributes.Sealed)]
	class BaseWithUnusedDerivedClass
	{

	}

	class UnusedDerivedClass : BaseWithUnusedDerivedClass
	{
	}
}