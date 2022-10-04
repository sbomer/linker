// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics;
using Mono.Linker;

namespace Mono.Linker.Tests.TestCasesRunner
{
	public class LinkerDriver
	{
		protected class TestDriver : Driver
		{
			readonly LinkerCustomizations _customization;

			public TestDriver (LinkerOptions options, LinkerCustomizations customizations) : base (options)
			{
				_customization = customizations;
			}

			protected override LinkContext GetDefaultContext (Pipeline pipeline, ILogger logger)
			{
				LinkContext context = base.GetDefaultContext (pipeline, logger);
				_customization.CustomizeLinkContext (context);
				return context;
			}
		}

		public virtual void Link (string[] args, LinkerCustomizations customizations, ILogger logger)
		{
			bool parsed = new ArgumentProcessor (args).TryProcess (out var options);
			Debug.Assert (parsed);
			using (var driver = new TestDriver (options, customizations)) {
				driver.Run (logger);
			}
		}
	}
}