﻿using System;
namespace Mono.Linker.Tests.Cases.DynamicDependencies.Dependencies
{
	public class DynamicDependencyMethodInAssemblyLibrary
	{
		public DynamicDependencyMethodInAssemblyLibrary ()
		{
		}

		private void Foo ()
		{
		}

		private int privateField;

		public class Nested<T>
		{
			public void Method (T t)
			{
			}
		}
	}
}
