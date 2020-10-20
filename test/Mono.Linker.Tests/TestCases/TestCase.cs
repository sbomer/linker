using System;
using System.Linq;
using System.Reflection;
using Mono.Linker.Tests.Extensions;

namespace Mono.Linker.Tests.TestCases
{
	public class TestCase
	{
		public TestCase (NPath sourceFile, NPath rootCasesDirectory, NPath originalTestCaseAssemblyPath)
		{
			SourceFile = sourceFile;
			OriginalTestCaseAssemblyPath = originalTestCaseAssemblyPath;
			Name = sourceFile.FileNameWithoutExtension;
			DisplayName = $"{sourceFile.RelativeTo (rootCasesDirectory).Parent.ToString (SlashMode.Forward).Replace ('/', '.')}.{sourceFile.FileNameWithoutExtension}";
			ReconstructedFullTypeName = $"{originalTestCaseAssemblyPath.FileNameWithoutExtension}.{DisplayName}";

			var firstParentRelativeToRoot = SourceFile.RelativeTo (rootCasesDirectory).Elements.First ();
			TestSuiteDirectory = rootCasesDirectory.Combine (firstParentRelativeToRoot);
		}

		public string Name { get; }

		public string DisplayName { get; }

		public NPath SourceFile { get; }

		public NPath OriginalTestCaseAssemblyPath { get; }

		public string ReconstructedFullTypeName { get; }

		public bool HasLinkXmlFile {
			get { return SourceFile.ChangeExtension ("xml").FileExists (); }
		}

		public NPath LinkXmlFile {
			get {
				if (!HasLinkXmlFile)
					throw new InvalidOperationException ("This test case does not have a link xml file");

				return SourceFile.ChangeExtension ("xml");
			}
		}

		public NPath TestSuiteDirectory { get; }
	}
}