using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Mono.Linker.Tests.Cases.Generator
{
    [Generator]
    public class TestCaseGenerator : ISourceGenerator
    {
        public void Execute(GeneratorExecutionContext context)
        {
            // begin creating the source we'll inject into the users compilation
            StringBuilder sourceBuilder = new StringBuilder(@"
using System;
using Mono.Linker.Tests.Cases.Expectations.Assertions;

namespace Mono.Linker.Tests.Cases.Stress
{
    public static class VirtualMethods
    {
        public static void Main()
        {
            new Derived0 ().Method ();
        }

        [KeptMember ("".ctor()"")]
        public class Base
        {
            [Kept]
            public virtual void Method () { }
        }
");

            var numOverrides = 2000;
            for (var i = 0; i < numOverrides - 1; i++) {
                sourceBuilder.AppendLine($@"

        [KeptMember ("".ctor()"")]
        [KeptBaseType (typeof (Base))]
        public class Derived{i} : Base
        {{
            [Kept]
            public override void Method () {{
                new Derived{i + 1} ().Method ();
            }}
        }}
");
            }

             sourceBuilder.AppendLine($@"

        [KeptMember ("".ctor()"")]
        [KeptBaseType (typeof (Base))]
        public class Derived{numOverrides - 1} : Base
        {{
            [Kept]
            public override void Method () {{ }}
        }}
");

            sourceBuilder.AppendLine(@"
    }
}");

            context.AddSource("Stress.VirtualMethods", SourceText.From(sourceBuilder.ToString (), Encoding.UTF8));

            // TODO: remove this once https://github.com/dotnet/roslyn/pull/47047/files flows
            context.AnalyzerConfigOptions.GlobalOptions.TryGetValue("build_property.CompilerGeneratedFilesOutputPath", out var outputRoot);
//            context.ReportDiagnostic(Diagnostic.Create(MyErr, Location.None, outputFile));

            var outputFile = Path.Combine (outputRoot, "Stress", "VirtualMethods.cs");
            var outputDir = Path.GetDirectoryName (outputFile);

            if (!Directory.Exists (outputDir))
                Directory.CreateDirectory (outputDir);
            if (File.Exists (outputFile))
                File.Delete (outputFile);
            File.WriteAllText(outputFile, sourceBuilder.ToString ());

            // // using the context, get a list of syntax trees in the users compilation
            // IEnumerable<SyntaxTree> syntaxTrees = context.Compilation.SyntaxTrees;
// 
            // // add the filepath of each tree to the class we're building
            // foreach (SyntaxTree tree in syntaxTrees)
            // {
            //     sourceBuilder.AppendLine($@"Console.WriteLine(@"" - {tree.FilePath}"");");
            // }

            // finish creating the source to inject

            // inject the created source into the users compilation
//            context.AddSource("helloWorldGenerated", SourceText.From(sourceBuilder.ToString(), Encoding.UTF8));
//            throw new Exception("Hello");
//            context.ReportDiagnostic(Diagnostic.Create(MyErr, Location.None, outputFile));
        }

        private static readonly DiagnosticDescriptor MyErr = new DiagnosticDescriptor (
            id: "MYWARNING",
            title: "output path",
            messageFormat: "my message {0}",
            category: "CAT",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        public void Initialize(GeneratorInitializationContext context)
        {
            // No initialization required
        }
    }
}
