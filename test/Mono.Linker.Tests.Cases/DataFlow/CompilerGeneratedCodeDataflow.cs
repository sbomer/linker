// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Reflection;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Mono.Linker.Tests.Cases.Expectations.Assertions;
using Mono.Linker.Tests.Cases.Expectations.Helpers;
using Mono.Linker.Tests.Cases.Expectations.Metadata;

namespace Mono.Linker.Tests.Cases.DataFlow
{
	[SkipKeptItemsValidation]
	[SandboxDependency ("Dependencies/TestStructTypeIReflect.cs")]
	[SetupCompileArgument ("/unsafe")]
	[ExpectedNoWarnings]
	public class CompilerGeneratedCodeDataflow
	{
		public static void Main ()
		{
			Iterator.Test ();
			Async.Test ();
			AsyncIterator.Test ();
			LocalFunction.Test ();
			LocalFunctionsWithRefs.Test ();
			Lambda.Test ();
		}

		class Iterator
		{
			[ExpectedWarning ("IL2072", nameof (GetWithPublicMethods), nameof (DataFlowTypeExtensions.RequiresAll),
				CompilerGeneratedCode = true)]
			static IEnumerable<int> FlowAcrossYieldReturn ()
			{
				Type t = GetWithPublicMethods ();
				yield return 0;
				t.RequiresAll ();
			}

			public static void Test ()
			{
				FlowAcrossYieldReturn ();
			}
		}

		class Async
		{
			[ExpectedWarning ("IL2072", nameof (GetWithPublicMethods), nameof (DataFlowTypeExtensions.RequiresAll),
				CompilerGeneratedCode = true)]
			static async void FlowAcrossAwait ()
			{
				Type t = GetWithPublicMethods ();
				await MethodAsync ();
				t.RequiresAll ();
			}

			public static void Test ()
			{
				FlowAcrossAwait ();
			}
		}

		class AsyncIterator
		{
			[ExpectedWarning ("IL2072", nameof (GetWithPublicMethods), nameof (DataFlowTypeExtensions.RequiresAll),
				CompilerGeneratedCode = true)]
			static async IAsyncEnumerable<int> FlowAcrossAwaitAndYieldReturn ()
			{
				Type t = GetWithPublicMethods ();
				await MethodAsync ();
				yield return 0;
				t.RequiresAll ();
			}

			public static void Test ()
			{
				FlowAcrossAwaitAndYieldReturn ();
			}
		}

		class LocalFunction
		{
			static void WarningsInBody ()
			{
				[ExpectedWarning ("IL2072", nameof (GetWithPublicMethods), nameof (DataFlowTypeExtensions.RequiresAll))]
				static void LocalFunction () {
					Type t = GetWithPublicMethods ();
					t.RequiresAll ();
				}

				LocalFunction ();
			}

			static void WarningsInBodyUnused ()
			{
				// Trimmer doesn't warn because this is unused code.
				[ExpectedWarning ("IL2072", nameof (GetWithPublicMethods), nameof (DataFlowTypeExtensions.RequiresAll),
					ProducedBy = ProducedBy.Analyzer)]
				static void LocalFunction () {
					Type t = GetWithPublicMethods ();
					t.RequiresAll ();
				}
			}

			static void ReadCapturedVariable ()
			{
				Type t = GetWithPublicMethods ();
				LocalFunction ();

				[ExpectedWarning ("IL2072", nameof (GetWithPublicMethods), nameof (DataFlowTypeExtensions.RequiresAll))]
				void LocalFunction () {
					t.RequiresAll ();
				}
			}

			public static void ReadCapturedParameter (Type tParameter)
			{
				LocalFunction ();

				[ExpectedWarning ("IL2067", nameof (ReadCapturedParameter), "tParameter", nameof (DataFlowTypeExtensions.RequiresAll))]
				void LocalFunction () => tParameter.RequiresAll ();
			}

			public static void ReadCapturedParameterAfterWrite (Type tParameter)
			{
				tParameter = GetWithPublicMethods (); // Changes the state of the parameter (only allowed for hoisted locals)
				LocalFunction ();

				[ExpectedWarning ("IL2072", nameof (GetWithPublicMethods), nameof (DataFlowTypeExtensions.RequiresAll))]
				void LocalFunction () => tParameter.RequiresAll ();
			}

			[ExpectedWarning ("IL2072", nameof (GetWithPublicFields), nameof (DataFlowTypeExtensions.RequiresAll),
				// analyzer clears all local state, but trimmer doesn't
				ProducedBy = ProducedBy.Trimmer)]
			static void ReadCapturedVariableWithUnhoistedLocals ()
			{
				Type t = GetWithPublicMethods ();
				Type notCaptured = GetWithPublicFields ();
				LocalFunction ();
				notCaptured.RequiresAll ();

				[ExpectedWarning ("IL2072", nameof (GetWithPublicMethods), nameof (DataFlowTypeExtensions.RequiresAll))]
				void LocalFunction () => t.RequiresAll ();
			}

			// [ExpectedWarning ("IL2072", nameof (GetWithPublicMethods), nameof (DataFlowTypeExtensions.RequiresAll))]
			// TODO: propagate state out. For now, just flows in.
			// This 2062 should really be a 2072.
			[ExpectedWarning ("IL2062", nameof (DataFlowTypeExtensions.RequiresAll))]
			static void WriteCapturedVariable ()
			{
				Type t;
				LocalFunction ();
				t.RequiresAll ();

				void LocalFunction () => t = GetWithPublicMethods ();
			}

			public static void Test ()
			{
				WarningsInBody ();
				WarningsInBodyUnused ();
				ReadCapturedVariable ();
				ReadCapturedParameter (null);
				ReadCapturedParameterAfterWrite (null);
				ReadCapturedVariableWithUnhoistedLocals ();
				WriteCapturedVariable ();
			}
		}

		class LocalFunctionsWithRefs
		{

			// This just tests that ldflda of a hoisted local doesn't cause problems.
			static void TestRefInLocalFunction ()
			{
				var s = new TestStruct ();

				void LocalFunction () {
					// ldflda DisplayClass::s
					// call S::get_TypeWithPublicFields
					var t = s.TypeWithPublicFields;
					ref var tRef = ref t;
					t.RequiresAll ();
				}

				LocalFunction ();
			}

			// Load from FieldReference to hoisted local
			//   where FieldReference is a hoisted local field
			//   where FieldReference is a 
			// create FieldReference to hoisted local
			// Store to FieldReference to hoisted local
			// DereferenceValue 
j
			static unsafe void TestIndirectLoadFromHoistedLocal ()
			{
				TestStructTypeIReflect s = GetWithPublicMethods ();
				var pS = &s;

				// [ExpectedWarning ("IL2062", nameof (Get))]
				void LocalFunction () {
					RequireAll (*pS);
				}

				LocalFunction ();
			}

			static unsafe void TestIndirectStoreToHoistedLocal ()
			{
				var s = new TestStructTypeIReflect ();
				var pS = &s;

				void LocalFunction () {
					*pS = GetWithPublicMethods ();
					RequireAll (*pS);
				}

				LocalFunction ();
			}

			[return: DynamicallyAccessedMembers (DynamicallyAccessedMemberTypes.PublicMethods)]
			static TestStructTypeIReflect GetWithPublicMethods () => default;

			static void RequireAll ([DynamicallyAccessedMembers (DynamicallyAccessedMemberTypes.All)] TestStructTypeIReflect s) {}


			public static void Test ()
			{
				TestRefInLocalFunction ();
				TestIndirectLoadFromHoistedLocal ();
				TestIndirectStoreToHoistedLocal ();
			}
		}

		class Lambda
		{
			static void WarningsInBody ()
			{
				var lambda =
					[ExpectedWarning ("IL2072", nameof (GetWithPublicMethods), nameof (DataFlowTypeExtensions.RequiresAll))]
					() => {
						Type t = GetWithPublicMethods ();
						t.RequiresAll ();
					};

				lambda ();
			}

			static void WarningsInBodyUnused ()
			{
				var lambda =
					[ExpectedWarning ("IL2072", nameof (GetWithPublicMethods), nameof (DataFlowTypeExtensions.RequiresAll))]
					() => {
						Type t = GetWithPublicMethods ();
						t.RequiresAll ();
					};
			}

			static void ReadCapturedVariable ()
			{
				Type t = GetWithPublicMethods ();
				Action lambda =
					[ExpectedWarning ("IL2072", nameof (GetWithPublicMethods), nameof (DataFlowTypeExtensions.RequiresAll))]
					() => t.RequiresAll ();
				lambda ();
			}

			static void ReadCapturedVariableAfterWriteAfterDefinition ()
			{
				Type t = GetAll ();

				// TODO: this is a hole! Maybe we need to track lambda values.
				Action lambda =
					// [ExpectedWarning ("IL2072", nameof (GetWithPublicFields), nameof (DataFlowTypeExtensions.RequiresAll))]
					() => t.RequiresAll ();

				t = GetWithPublicMethods ();
				lambda ();
			}

			public static void ReadCapturedParameter (Type tParameter)
			{
				var lambda =
					[ExpectedWarning ("IL2067", nameof (ReadCapturedParameter), "tParameter", nameof (DataFlowTypeExtensions.RequiresAll))]
					() => tParameter.RequiresAll ();

				lambda ();
			}

			public static void ReadCapturedParameterAfterWrite (Type tParameter)
			{
				tParameter = GetWithPublicMethods ();
				var lambda =
					[ExpectedWarning ("IL2072", nameof (GetWithPublicMethods), nameof (DataFlowTypeExtensions.RequiresAll))] // TODO: should be 2072.
					() => tParameter.RequiresAll ();
				lambda ();
			}

			[ExpectedWarning ("IL2072", nameof (GetWithPublicFields), nameof (DataFlowTypeExtensions.RequiresAll),
				// analyzer clears all local state, but trimmer doesn't
				ProducedBy = ProducedBy.Trimmer)]
			static void ReadCapturedVariableWithUnhoistedLocals ()
			{
				Type t = GetWithPublicMethods ();
				Type notCaptured = GetWithPublicFields ();
				Action lambda =
					[ExpectedWarning ("IL2072", nameof (GetWithPublicMethods), nameof (DataFlowTypeExtensions.RequiresAll))]
					() => t.RequiresAll ();
				lambda ();
				notCaptured.RequiresAll ();
			}

			// [ExpectedWarning ("IL2072", nameof (GetWithPublicMethods), nameof (DataFlowTypeExtensions.RequiresAll))]
			// TODO
			static void WriteCapturedVariable ()
			{
				Type t = null;
				Action lambda = () => t = GetWithPublicMethods ();
				lambda ();
				t.RequiresAll ();
			}

			public static void Test ()
			{
				WarningsInBody ();
				WarningsInBodyUnused ();
				ReadCapturedVariable ();
				ReadCapturedVariableAfterWriteAfterDefinition ();
				ReadCapturedParameter (null);
				ReadCapturedParameterAfterWrite (null);
				ReadCapturedVariableWithUnhoistedLocals ();
				WriteCapturedVariable ();
			}
		}

		[return: DynamicallyAccessedMembers (DynamicallyAccessedMemberTypes.All)]
		static Type GetAll () => null;

		[return: DynamicallyAccessedMembers (DynamicallyAccessedMemberTypes.PublicMethods)]
		static Type GetWithPublicMethods () => null;

		[return: DynamicallyAccessedMembers (DynamicallyAccessedMemberTypes.PublicFields)]
		static Type GetWithPublicFields () => null;

		static async Task<int> MethodAsync ()
		{
			return await Task.FromResult (0);
		}

		[RequiresUnreferencedCode ("RUC")]
		static void RUCMethod () {}

		struct TestStruct
		{
			[DynamicallyAccessedMembers (DynamicallyAccessedMemberTypes.PublicFields)]
			public Type TypeWithPublicFields => null;
		}
	}
} 