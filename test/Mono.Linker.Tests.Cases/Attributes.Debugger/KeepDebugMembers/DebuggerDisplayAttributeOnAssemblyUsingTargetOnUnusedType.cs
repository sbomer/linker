﻿using System.Diagnostics;
using Mono.Linker.Tests.Cases.Attributes.Debugger.KeepDebugMembers;
using Mono.Linker.Tests.Cases.Expectations.Assertions;
using Mono.Linker.Tests.Cases.Expectations.Metadata;

[assembly: DebuggerDisplay ("{Property}", Target = typeof (DebuggerDisplayAttributeOnAssemblyUsingTargetOnUnusedType.Foo))]

namespace Mono.Linker.Tests.Cases.Attributes.Debugger.KeepDebugMembers {
	[SetupLinkerCoreAction ("link")]
	[SetupLinkerKeepDebugMembers ("true")]

	// Can be removed once this bug is fixed https://bugzilla.xamarin.com/show_bug.cgi?id=58168
	[SkipPeVerify (SkipPeVerifyForToolchian.Pedump)]

#if NETCOREAPP
	[KeptMemberInAssembly ("System.Private.CoreLib.dll", typeof (DebuggerDisplayAttribute), ".ctor(System.String)")]
#else
	[KeptMemberInAssembly ("mscorlib.dll", typeof (DebuggerDisplayAttribute), ".ctor(System.String)")]
#endif
	public class DebuggerDisplayAttributeOnAssemblyUsingTargetOnUnusedType {
		public static void Main ()
		{
		}

		public class Foo {
			public int Property { get; set; }
		}
	}
}