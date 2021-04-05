using System;
using Mono.Linker.Tests.Cases.Expectations.Assertions;
using Mono.Linker.Tests.Cases.Expectations.Metadata;
using Mono.Linker.Tests.Cases.TypeForwarding.Dependencies;

namespace Mono.Linker.Tests.Cases.TypeForwarding
{
	// Actions:
	// link - This assembly, Forwarder.dll and Implementation.dll
	[SetupLinkerDefaultAction ("link")]
	[SetupLinkerAction ("copyused", "Attribute")]

	[SetupCompileBefore ("Forwarder.dll", new[] { "Dependencies/MyEnum.cs" }, defines: new[] { "INCLUDE_REFERENCE_IMPL" })]
	[SetupCompileBefore ("Removed.dll", new[] { "Dependencies/UnusedLibrary.cs" })]
	[SetupCompileBefore ("Attribute.dll", new[] { "Dependencies/AttributeWithEnumArgument.cs" }, references: new[] { "Forwarder.dll", "Removed.dll" })]

	// After compiling the test case we then replace the reference impl with implementation + type forwarder
	[SetupCompileAfter ("Implementation.dll", new[] { "Dependencies/MyEnum.cs" })]
	[SetupCompileAfter ("Forwarder.dll", new[] { "Dependencies/MyEnumForwarder.cs" }, references: new[] { "Implementation.dll" })]

	[KeptTypeInAssembly ("Forwarder.dll", typeof (UsedToReferenceForwarderAssembly))]
	// [KeptTypeInAssembly ("Implementation.dll", "Mono.Linker.Tests.Cases.TypeForwarding.Dependencies.MyEnum")]
	class AttributeEnumArgumentForwardedCopyUsed
	{
		static void Main ()
		{
            // For the issue to repro, the forwarder assembly must be processed by SweepStep before
            // the attribute. Referencing it first in the test does this, even though it's not really
            // a guarantee, since the assembly action dictionary doesn't guarantee order.
            var _ = typeof (UsedToReferenceForwarderAssembly);
			// To repro the issue in a world where we are marking all forwarders that point to marked types,
			// avoid marking the attributed type (and hence the Enum). Instead, just reference the assembly
			// with the enum and make it "copyused" to preserve the attributed type anyway (while updating typeref scopes).
            var _2 = typeof (UsedToReferenceAttributeAssembly);
			// Additionally, to trigger the behavior the "copyused" assembly has to be changed to "save" in SweepStep.
			// This happens when it has a reference to a removed assembly.
		}
	}
}
