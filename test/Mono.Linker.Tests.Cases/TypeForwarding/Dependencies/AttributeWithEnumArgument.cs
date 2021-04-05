using System;
using Mono.Linker.Tests.Cases.TypeForwarding.Dependencies;

namespace Mono.Linker.Tests.Cases.TypeForwarding.Dependencies
{
    public class AttributeWithEnumArgumentAttribute : Attribute
    {
    	public AttributeWithEnumArgumentAttribute (MyEnum arg)
    	{
    	}
    }

   	[AttributeWithEnumArgument (MyEnum.A)]
    public class AttributedType
    {
    }

    public class UsedToReferenceAttributeAssembly
    {
    }

    public class UnusedType
    {
        public static void ReferenceUnusedAssembly()
        {
            var _ = typeof (UnusedLibrary);
        }
    }
}