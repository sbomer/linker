using System;

namespace Mono.Linker.Tests.Cases.Expectations.Metadata
{
	/// <summary>
	/// Use ensure that assemblies compiled after compiling the main test case executabe
	/// are also compiled after compiling the test executable with expectations
	/// </summary>
	[AttributeUsage (AttributeTargets.Class)]
	public class CompileAfterForExpectations : BaseMetadataAttribute
	{
		public CompileAfterForExpectations ()
		{
		}
	}
}
