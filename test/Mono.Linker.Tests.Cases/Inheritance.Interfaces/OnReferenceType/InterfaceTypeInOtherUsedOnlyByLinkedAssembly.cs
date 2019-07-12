using Mono.Linker.Tests.Cases.Expectations.Assertions;
using Mono.Linker.Tests.Cases.Expectations.Metadata;
using Mono.Linker.Tests.Cases.Inheritance.Interfaces.Dependencies;

namespace Mono.Linker.Tests.Cases.Inheritance.Interfaces.OnReferenceType {
	[SetupCompileBefore ("skipped.dll", new [] {typeof (InterfaceTypeInOtherUsedOnlyByCopiedAssembly_Link)})]
	[SetupCompileBefore ("link.dll", new [] {typeof (InterfaceTypeInOtherUsedOnlyByCopiedAssembly_Copy)}, new [] {"skipped.dll"})]

	[SetupLinkerAction ("link", "link")]
	[SetupLinkerAction ("skip", "skipped")]
	[SetupLinkerArgument ("-r", "link")]

	[KeptMemberInAssembly ("link.dll", typeof (InterfaceTypeInOtherUsedOnlyByCopiedAssembly_Copy.A), "Method()")]
	[KeptMemberInAssembly ("link.dll", typeof (InterfaceTypeInOtherUsedOnlyByCopiedAssembly_Copy.B), "Method()")]
	[KeptMemberInAssembly ("link.dll", typeof (InterfaceTypeInOtherUsedOnlyByCopiedAssembly_Copy.B), "Method2()")]
	[ExpectInterfaceTypeReferenceInAssembly ("link.dll", typeof (InterfaceTypeInOtherUsedOnlyByCopiedAssembly_Copy.A), "skipped.dll", typeof (InterfaceTypeInOtherUsedOnlyByCopiedAssembly_Link.IFoo))]
	[ExpectInterfaceTypeReferenceInAssembly ("link.dll", typeof (InterfaceTypeInOtherUsedOnlyByCopiedAssembly_Copy.B), "skipped.dll", typeof (InterfaceTypeInOtherUsedOnlyByCopiedAssembly_Link.IFoo))]
	[ExpectInterfaceTypeReferenceInAssembly ("link.dll", typeof (InterfaceTypeInOtherUsedOnlyByCopiedAssembly_Copy.B), "skipped.dll", typeof (InterfaceTypeInOtherUsedOnlyByCopiedAssembly_Link.IBar))]
	[RemovedInterfaceOnTypeInAssembly ("link.dll",
		"Mono.Linker.Tests.Cases.Inheritance.Interfaces.Dependencies.InterfaceTypeInOtherUsedOnlyByCopiedAssembly_Copy/C",
		"link.dll",
		"Mono.Linker.Tests.Cases.Inheritance.Interfaces.Dependencies.InterfaceTypeInOtherUsedOnlyByCopiedAssembly_Copy/IBaz")]
	public class InterfaceTypeInOtherUsedOnlyByLinkedAssembly {
		public static void Main ()
		{
			InterfaceTypeInOtherUsedOnlyByCopiedAssembly_Copy.ToKeepReferenceAtCompileTime ();
		}
	}
}