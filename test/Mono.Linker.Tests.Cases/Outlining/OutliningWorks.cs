using Mono.Linker.Tests.Cases.Expectations.Assertions;
using Mono.Linker.Tests.Cases.Expectations.Metadata;

namespace Mono.Linker.Tests.Cases.Outlining
{
	public class OutliningWorks
	{
		public static void Main ()
		{
			A();
			B();
		}

		public static int A() {
			return 42;
		}

		public static int B() {
			return 42;
		}
	}
}