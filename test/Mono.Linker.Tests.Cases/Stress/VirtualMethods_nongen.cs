using Mono.Linker.Tests.Cases.Expectations.Assertions;

namespace Mono.Linker.Tests.Cases.Statics
{
	class VirtualMethods
	{
		public static void Main ()
		{
		}

		static void Dead ()
		{
			new B ();
		}

		class B
		{
			static B ()
			{
			}
		}
	}
}