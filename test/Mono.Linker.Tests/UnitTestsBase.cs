using System;
using Xunit;
using Xunit.Abstractions;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Linker.Steps;
using Mono.Linker;
using Mono.Collections.Generic;
using System.Xml.XPath;
using System.Threading;

namespace Mono.Linker.Tests
{
    public class UnitTestsBase
    {
        private readonly ITestOutputHelper output;

        public UnitTestsBase(ITestOutputHelper output)
        {
            this.output = output;
        }

        private string _testCasesRoot;
        private string _testCase;
        private LinkContext _context;
        private Pipeline _pipeline;

        private LinkContext Context {
            get { return _context; }
        }

        private Pipeline Pipeline {
            get { return _pipeline; }
        }

        private string GetTestcaseDir()
        {
            return Path.Combine(_testCasesRoot, _testCase);
        }

        private string GetOutputDir()
        {
            return Path.Combine(GetTestcaseDir(), "publish");
        }
        
        private string GetLinkDir()
        {
            return Path.Combine(GetTestcaseDir(), "linked");
        }
        
        private string GetAssemblyFileName(AssemblyDefinition asm)
        {
            // TODO: fix this workaround once we are able to target .NET core.
            // On the desktop framework, the main executable has a .exe extension.
            // For all of the test programs, the main executable is named
            // "Program", so we change the extension to .exe in this case.
//            if (asm.Name.Name.Equals("Program")) {
//                return asm.Name.Name + ".exe";
//            } else {
                return asm.Name.Name + ".dll";
//            }
        }

        private bool NotLinked(ICustomAttributeProvider provider)
        {
            foreach (CustomAttribute ca in provider.CustomAttributes)
                if (ca.Constructor.DeclaringType.Name == "NotLinkedAttribute")
                    return true;

            return false;
        }

        private void Compare()
        {
            // compare original and linked assemblies
            foreach (AssemblyDefinition assembly in Context.GetAssemblies ()) {
                output.WriteLine($"assembly: {assembly.Name}");
                if (Context.Annotations.GetAction(assembly) != AssemblyAction.Link)
                    continue;
                
                string fileName = GetAssemblyFileName(assembly);
                string originalAssembly = Path.Combine(GetOutputDir(), fileName);
                string linkedAssembly = Path.Combine(GetLinkDir(), fileName);
                output.WriteLine($"comparing assemblies {originalAssembly} and {linkedAssembly}");
                
                CompareAssemblies(AssemblyDefinition.ReadAssembly(originalAssembly),
                                  AssemblyDefinition.ReadAssembly(linkedAssembly));
            }
        }

        private bool ParametersEqual(Collection<ParameterDefinition> paramsA, Collection<ParameterDefinition> paramsB)
        {
            var stringParamsA = paramsA.Select(p => p.Name);
            var stringParamsB = paramsB.Select(p => p.Name);
            return stringParamsA.SequenceEqual(stringParamsB);
        }
        
        private void CompareTypes(TypeDefinition type, TypeDefinition linkedType)
        {
            foreach (FieldDefinition originalField in type.Fields) {
                // FieldDefinition linkedField = linkedType.GetField(originalField.Name);// TODO: also get with the type!
                FieldDefinition linkedField = linkedType.Fields.Where(f => f.Name == originalField.Name).FirstOrDefault();
                if (NotLinked(originalField)) {
                    output.WriteLine($"field {originalField.Name} is not supposed to be linked");
                    Assert.Null(linkedField);
                    continue;
                }

                output.WriteLine($"field {originalField.Name} is supposed to be linked");
                Assert.NotNull(linkedField);
            }

            foreach (MethodDefinition originalMethod in type.Methods) {
                MethodDefinition linkedMethod = linkedType.Methods.Where(m => m.Name == originalMethod.Name && 
                                                                         ParametersEqual(m.Parameters, originalMethod.Parameters) &&
                                                                         m.IsStatic == originalMethod.IsStatic).FirstOrDefault();
                string sig = originalMethod.Name + "(" + String.Join(" ", originalMethod.Parameters.Select(p => p.Name)) + ")";
                if (NotLinked(originalMethod)) {
                    output.WriteLine($"method {sig} is not supposed to be linked");
                    Assert.Null(linkedMethod);
                    continue;
                }

                output.WriteLine($"method {sig} is supposed to be linked");
                Assert.NotNull(linkedMethod);
            }
        }
        
        private void CompareAssemblies(AssemblyDefinition original, AssemblyDefinition linked)
        {
            foreach (TypeDefinition originalType in original.MainModule.Types) {
                TypeDefinition linkedType = linked.MainModule.GetType(originalType.FullName);
                if (NotLinked(originalType)) {
                    Assert.Null(linkedType);
                    continue;
                }

                Assert.NotNull(linkedType);
                output.WriteLine($"comparing types: {originalType.Name} and {linkedType.Name}");
                CompareTypes(originalType, linkedType);
            }
        }

        private Pipeline GetPipeline()
        {
            Pipeline p = new Pipeline();
            p.AppendStep(new LoadReferencesStep());
            p.AppendStep(new BlacklistStep());
            p.AppendStep(new TypeMapStep());
            p.AppendStep(new MarkStep());
            p.AppendStep(new SweepStep());
            p.AppendStep(new CleanStep());
            p.AppendStep(new OutputStep());
            return p;
        }

        private void Setup()
        {
            _pipeline = GetPipeline();
            // set up link context to pass through the pipeline
            _context = new LinkContext(_pipeline);
            _context.CoreAction = AssemblyAction.Copy;
            _context.OutputDirectory = GetLinkDir();

            // delete linker dir from previous test run if it exists
            if (Directory.Exists(GetLinkDir()))
                Directory.Delete(GetLinkDir(), true);
            Directory.CreateDirectory(GetLinkDir());
        }

        protected void RunTest(string testName)
        {
            _testCasesRoot = "TestAssets";
            _testCase = testName;

            Setup();
            
            string testExe = Path.Combine(GetOutputDir(), "Program.dll");
            Assert.True(File.Exists(testExe));
            _pipeline.PrependStep(new ResolveFromAssemblyStep(testExe));

            _pipeline.Process(_context);

            Compare();
        }

        protected void RunXmlTest(string testName)
        {
            _testCasesRoot = "TestAssets";
            _testCase = testName;

            Setup();

            string xmlPath = Path.Combine(GetTestcaseDir(), "desc.xml");
            Assert.True(File.Exists(xmlPath));
            _pipeline.PrependStep(new ResolveFromXmlStep(new XPathDocument(xmlPath)));

            // In xml mode, we need to specify the input directory to
            // look for assemblies in. The tests used to accomplish
            // this by setting the environment's current directory to
            // the assembly directory, but this prevents us from
            // running tests concurrently. Instead, we tell the linker
            // where to look.
            _context.Resolver.AddSearchDirectory(GetOutputDir());

            _pipeline.Process(_context);

            Compare();
        }
    }
}
