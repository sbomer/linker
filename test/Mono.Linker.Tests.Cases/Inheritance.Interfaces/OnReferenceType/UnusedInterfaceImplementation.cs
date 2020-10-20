using Mono.Linker.Tests.Cases.Expectations.Assertions;
using Mono.Linker.Tests.Cases.Expectations.Metadata;

namespace Mono.Linker.Tests.Cases.Inheritance.Interfaces.OnReferenceType
{
	[SetupLinkerArgument ("--disable-opt", "unusedinterfaces")]
	public class UnusedInterfaceImplementation
	{
		public static void Main ()
		{
			MarkFoo ();
			MarkInterface ();
		}

		[Kept]
		static void MarkFoo () {
			MarkFooHelper (null);
		}

		[Kept]
		static void MarkFooHelper (Foo f) { }

		[Kept]
		static void MarkInterface () {
			MarkInterfaceHelper (null);
		}

		[Kept]
		static void MarkInterfaceHelper (IFoo i) {
			i.Method ();
		}

		[Kept]
		interface IFoo
		{
			[Kept]
			void Method ();
		}

		[Kept]
		// BUG! The impl is DROPPED even though the opt was disabled.
		class Foo : IFoo
		{
			public void Method () { }
		}
	}
}