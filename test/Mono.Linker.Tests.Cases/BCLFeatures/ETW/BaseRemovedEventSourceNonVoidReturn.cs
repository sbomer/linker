using System.Diagnostics.Tracing;
using Mono.Linker.Tests.Cases.Expectations.Assertions;
using Mono.Linker.Tests.Cases.Expectations.Metadata;

namespace Mono.Linker.Tests.Cases.BCLFeatures.ETW
{
#if NETCOREAPP
	[IgnoreTestCase ("--exclude-feature is not supported on .NET Core")]
#endif
	[SetupLinkerArgument ("--exclude-feature", "etw")]
	// Keep framework code that calls EventSource methods like OnEventCommand
	[SetupLinkerTrimAction ("skip")]
	public class BaseRemovedEventSourceNonVoidReturn
	{
		public static void Main ()
		{
			var b = CustomCtorEventSourceNonVoidReturn.Log.IsEnabled ();
			if (b)
				CustomCtorEventSourceNonVoidReturn.Log.SomeMethod ();
		}
	}

	[Kept]
	[KeptBaseType (typeof (EventSource))]
	[KeptMember (".ctor()")]
	[KeptMember (".cctor()")]
	[EventSource (Name = "MyCompany")]
	class CustomCtorEventSourceNonVoidReturn : EventSource
	{
		public class Keywords
		{
			public const EventKeywords Page = (EventKeywords) 1;

			public int Unused;
		}

		[Kept]
		public static CustomCtorEventSourceNonVoidReturn Log = new MyEventSourceBasedOnCustomCtorEventSourceNonVoidReturn (1);

		[Kept]
		[ExpectedInstructionSequence (new[] { "ldarg.0", "call", "ret", })]
		public CustomCtorEventSourceNonVoidReturn (int value)
		{
			Removed ();
		}

		[Kept]
		[ExpectedInstructionSequence (new[] { "ldstr", "newobj", "throw" })]
		[ExpectLocalsModified]
		[Event (8)]
		public int SomeMethod ()
		{
			return Removed ();
		}

		public int Removed ()
		{
			return 0;
		}
	}

	[Kept]
	[KeptBaseType (typeof (CustomCtorEventSourceNonVoidReturn))]
	class MyEventSourceBasedOnCustomCtorEventSourceNonVoidReturn : CustomCtorEventSourceNonVoidReturn
	{
		[Kept]
		public MyEventSourceBasedOnCustomCtorEventSourceNonVoidReturn (int value) : base (value)
		{
		}
	}
}