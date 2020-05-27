﻿using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using Mono.Linker.Tests.Cases.Expectations.Assertions;
using Mono.Linker.Tests.Cases.Expectations.Metadata;

namespace Mono.Linker.Tests.Cases.DynamicDependencies
{
	[LogContains ("IL2037: Unresolved member 'MissingMethod' in DynamicDependencyAttribute on 'System.Void Mono.Linker.Tests.Cases.DynamicDependencies.DynamicDependencyMethod/B::Broken()'")]
	[LogContains ("IL2037: Unresolved member 'Dependency2``1(``0,System.Int32,System.Object)' in DynamicDependencyAttribute on 'System.Void Mono.Linker.Tests.Cases.DynamicDependencies.DynamicDependencyMethod/B::Broken()'")]
	[LogContains ("IL2037: Unresolved member '#ctor()' in DynamicDependencyAttribute on 'System.Void Mono.Linker.Tests.Cases.DynamicDependencies.DynamicDependencyMethod/B::Broken()'")]
	[LogContains ("IL2037: Unresolved member '#cctor()' in DynamicDependencyAttribute on 'System.Void Mono.Linker.Tests.Cases.DynamicDependencies.DynamicDependencyMethod/B::Broken()'")]
	class DynamicDependencyMethod
	{
		public static void Main ()
		{
			new B (); // Needed to avoid lazy body marking stubbing

			B.Method ();
			B.SameContext ();
			B.Broken ();
			B.Conditional ();
		}

		[KeptMember (".ctor()")]
		class B
		{
			[Kept]
			int field;

			[Kept]
			void Method2 (out sbyte arg)
			{
				arg = 1;
			}

			[Kept]
			[DynamicDependency ("Dependency1()", typeof (C))]
			[DynamicDependency ("Dependency2``1(``0[],System.Int32", typeof (C))]
			[DynamicDependency ("#ctor()", typeof (C))] // To avoid lazy body marking stubbing
			[DynamicDependency ("field", typeof (C))]
			[DynamicDependency ("NextOne(Mono.Linker.Tests.Cases.DynamicDependencies.DynamicDependencyMethod.Nested@)", typeof (Nested))]
			[DynamicDependency ("#cctor()", typeof (Nested))]
			// Dependency on a property itself should be expressed as a dependency on one or both accessor methods
			[DynamicDependency ("get_Property()", typeof (C))]
			public static void Method ()
			{
			}

			[Kept]
			[DynamicDependency ("field")]
			[DynamicDependency ("Method2(System.SByte@)")]
			public static void SameContext ()
			{
			}

			[Kept]
			[DynamicDependency ("MissingMethod", typeof (C))]
			[DynamicDependency ("Dependency2``1(``0,System.Int32,System.Object)", typeof (C))]
			[DynamicDependency ("")]
			[DynamicDependency ("#ctor()", typeof (NestedStruct))]
			[DynamicDependency ("#cctor()", typeof (C))]
			public static void Broken ()
			{
			}

			[Kept]
			[DynamicDependency ("ConditionalTest()", typeof (C), Condition = "don't have it")]
			public static void Conditional ()
			{
			}
		}

		class Nested
		{
			[Kept]
			private static void NextOne (ref Nested arg1)
			{
			}

			[Kept]
			static Nested ()
			{

			}
		}

		struct NestedStruct
		{
			public string Name;

			public NestedStruct (string name)
			{
				Name = name;
			}
		}
	}

	[KeptMember (".ctor()")]
	class C
	{
		[Kept]
		internal string field;

		[Kept]
		internal void Dependency1 ()
		{
		}

		internal void Dependency1 (long arg1)
		{
		}

		[Kept]
		internal void Dependency2<T> (T[] arg1, int arg2)
		{
		}

		[Kept]
		[KeptBackingField]
		internal string Property { [Kept] get; set; }

		internal void ConditionalTest ()
		{
		}
	}
}