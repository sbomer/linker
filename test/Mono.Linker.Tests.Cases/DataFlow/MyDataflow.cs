// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Mono.Linker.Tests.Cases.Expectations.Assertions;
using Mono.Linker.Tests.Cases.Expectations.Helpers;
using Mono.Linker.Tests.Cases.Expectations.Metadata;

namespace Mono.Linker.Tests.Cases.DataFlow
{
	[SkipKeptItemsValidation]
	[SetupCompileArgument ("/unsafe")]
	[ExpectedNoWarnings]
	public class MyDataflow
	{
		public static void Main ()
		{
			Lambda.Test ();
		}

		class Lambda
		{
			static void WarningsInBody ()
			{
				var lambda =
					[ExpectedWarning ("IL2072", nameof (GetWithPublicMethods), nameof (DataFlowTypeExtensions.RequiresAll),
					ProducedBy = ProducedBy.Trimmer)]
				() => {
					Type t = GetWithPublicMethods ();
					t.RequiresAll ();
				};

				lambda ();
			}

			public static void Test ()
			{
				WarningsInBody ();
			}
		}

		[return: DynamicallyAccessedMembers (DynamicallyAccessedMemberTypes.All)]
		static Type GetAll () => null;

		[return: DynamicallyAccessedMembers (DynamicallyAccessedMemberTypes.PublicMethods)]
		static Type GetWithPublicMethods () => null;

		[return: DynamicallyAccessedMembers (DynamicallyAccessedMemberTypes.PublicFields)]
		static Type GetWithPublicFields () => null;

		static Type GetUnknownType () => null;

		static async Task<int> MethodAsync ()
		{
			return await Task.FromResult (0);
		}
	}
}
