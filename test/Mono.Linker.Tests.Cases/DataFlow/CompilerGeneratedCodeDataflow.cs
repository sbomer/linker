// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Mono.Linker.Tests.Cases.Expectations.Assertions;
using Mono.Linker.Tests.Cases.Expectations.Helpers;

namespace Mono.Linker.Tests.Cases.DataFlow
{
	[SkipKeptItemsValidation]
	[ExpectedNoWarnings]
	public class CompilerGeneratedCodeDataflow
	{
		public static void Main ()
		{
			Iterator.Test ();
			Async.Test ();
			AsyncIterator.Test ();
			LocalFunction.Test ();
			Lambda.Test ();
		}

		class Iterator
		{
			// TODO: location should point back to user code for linker.
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
			// TODO: location should point back to user code for linker.
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

			// Linker should produce warning with de-mangled name.
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
				tParameter = GetWithPublicMethods (); // Shouldn't change the state of the parameter.
				LocalFunction ();

				[ExpectedWarning ("IL2067", nameof (ReadCapturedParameter), "tParameter", nameof (DataFlowTypeExtensions.RequiresAll))]
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

			// Linker warning should point back to user code
			static void ReadCapturedVariable ()
			{
				Type t = GetWithPublicMethods ();
				Action lambda =
					[ExpectedWarning ("IL2072", nameof (GetWithPublicMethods), nameof (DataFlowTypeExtensions.RequiresAll))]
					() => t.RequiresAll ();
				// TODO...
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
				var lambda =
					[ExpectedWarning ("IL2067", nameof (ReadCapturedParameter), "tParameter", nameof (DataFlowTypeExtensions.RequiresAll))]
					() => tParameter.RequiresAll ();
				tParameter = GetWithPublicMethods (); // Shouldn't change the state of the parameter.
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
				ReadCapturedParameter (null);
				ReadCapturedParameterAfterWrite (null);
				ReadCapturedVariableWithUnhoistedLocals ();
				WriteCapturedVariable ();
			}
		}

		[return: DynamicallyAccessedMembers (DynamicallyAccessedMemberTypes.PublicMethods)]
		static Type GetWithPublicMethods () => null;

		[return: DynamicallyAccessedMembers (DynamicallyAccessedMemberTypes.PublicFields)]
		static Type GetWithPublicFields () => null;

		static async Task<int> MethodAsync ()
		{
			return await Task.FromResult (0);
		}
	}
}