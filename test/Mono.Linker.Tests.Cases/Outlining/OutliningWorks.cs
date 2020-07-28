using Mono.Linker.Tests.Cases.Expectations.Assertions;
using Mono.Linker.Tests.Cases.Expectations.Metadata;

namespace Mono.Linker.Tests.Cases.Outlining
{
	// Use Release mode so that Roslyn emits fewer locals.
	[SetupCompileArgument ("/optimize+")]
	public class OutliningWorks
	{
		public static void Main ()
		{
			A();
			B();
			AddA(1, 2);
			AddB(3, 4);
		}

		[Kept]
		[ExpectedInstructionSequence (new[]
		{
			"call",
			"ret"
		})]
		public static int A() {
			return 42;
		}

		[Kept]
		[ExpectedInstructionSequence (new[]
		{
			"call",
			"ret"
		})]
		public static int B() {
			return 42;
		}

		[Kept]
		[ExpectedInstructionSequence (new[]
		{
			"ldarg.0",
			"ldarg.1",
			"call",
			"ret"
		})]
		public static int AddA(int a, int b) {
			return a + b; // ldarg, ldarg, add, ret (in Release)
		}

		[Kept]
		[ExpectedInstructionSequence (new[]
		{
			"ldarg.0",
			"ldarg.1",
			"call",
			"ret"
		})]
		public static int AddB(int a, int b) {
			return a + b; // ldarg, ldarg, add, ret (in Release)
		}
	}
}