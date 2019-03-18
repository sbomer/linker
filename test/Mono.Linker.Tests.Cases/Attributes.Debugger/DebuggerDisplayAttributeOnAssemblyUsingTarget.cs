﻿using System.Diagnostics;
using Mono.Linker.Tests.Cases.Attributes.Debugger;
using Mono.Linker.Tests.Cases.Expectations.Assertions;
using Mono.Linker.Tests.Cases.Expectations.Metadata;

[assembly: KeptAttributeAttribute (typeof (DebuggerDisplayAttribute))]
[assembly: DebuggerDisplay ("{Property}", Target = typeof (DebuggerDisplayAttributeOnAssemblyUsingTarget.Foo))]

namespace Mono.Linker.Tests.Cases.Attributes.Debugger {
	[SetupLinkerCoreAction ("link")]
	[SetupLinkerKeepDebugMembers ("false")]
	
	// Can be removed once this bug is fixed https://bugzilla.xamarin.com/show_bug.cgi?id=58168
	[SkipPeVerify (SkipPeVerifyForToolchian.Pedump)]
	
#if NETCOREAPP
	[KeptMemberInAssembly ("System.Private.CoreLib.dll", typeof (DebuggerDisplayAttribute), ".ctor(System.String)")]
	[KeptMemberInAssembly ("System.Private.CoreLib.dll", typeof (DebuggerDisplayAttribute), "set_Target(System.Type)")]
#else
	[KeptMemberInAssembly ("mscorlib.dll", typeof (DebuggerDisplayAttribute), ".ctor(System.String)")]
	[KeptMemberInAssembly ("mscorlib.dll", typeof (DebuggerDisplayAttribute), "set_Target(System.Type)")]
#endif
	public class DebuggerDisplayAttributeOnAssemblyUsingTarget {
		public static void Main ()
		{
			var foo = new Foo ();
			foo.Property = 1;
		}

		[Kept]
		[KeptMember (".ctor()")]
		public class Foo {
			[Kept]
			[KeptBackingField]
			public int Property { get; [Kept] set; }
		}
	}
}