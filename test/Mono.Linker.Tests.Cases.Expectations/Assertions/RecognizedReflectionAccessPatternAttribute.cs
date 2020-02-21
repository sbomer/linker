using System;

namespace Mono.Linker.Tests.Cases.Expectations.Assertions
{
	[AttributeUsage (AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
	public class RecognizedReflectionAccessPatternAttribute : BaseExpectedLinkedBehaviorAttribute
	{
		public RecognizedReflectionAccessPatternAttribute (Type reflectionMethodType, string reflectionMethodName, Type[] reflectionMethodParameters,
			Type accessedItemType, string accessedItemName, Type[] accessedItemParameters)
		{
			if (reflectionMethodType == null)
				throw new ArgumentNullException (nameof (reflectionMethodType));
			if (string.IsNullOrEmpty (reflectionMethodName))
				throw new ArgumentException ("Value cannot be null or empty.", nameof (reflectionMethodName));
			if (reflectionMethodParameters == null)
				throw new ArgumentNullException (nameof (reflectionMethodParameters));

			if (accessedItemType == null)
				throw new ArgumentNullException (nameof (accessedItemType));
			if (string.IsNullOrEmpty (accessedItemName))
				throw new ArgumentException ("Value cannot be null or empty.", nameof (accessedItemName));
		}

		public RecognizedReflectionAccessPatternAttribute (Type reflectionMethodType, string reflectionMethodName, Type [] reflectionMethodParameters,
			Type accessedItemType, string accessedItemName, string [] accessedItemParameters)
		{
			if (reflectionMethodType == null)
				throw new ArgumentNullException (nameof (reflectionMethodType));
			if (string.IsNullOrEmpty (reflectionMethodName))
				throw new ArgumentException ("Value cannot be null or empty.", nameof (reflectionMethodName));
			if (reflectionMethodParameters == null)
				throw new ArgumentNullException (nameof (reflectionMethodParameters));

			if (accessedItemType == null)
				throw new ArgumentNullException (nameof (accessedItemType));
			if (string.IsNullOrEmpty (accessedItemName))
				throw new ArgumentException ("Value cannot be null or empty.", nameof (accessedItemName));
		}
	}
}
