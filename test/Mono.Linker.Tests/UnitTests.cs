using System;
using Xunit;
using Xunit.Abstractions;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Linker.Steps;
using Mono.Linker;
using System.Xml.XPath;
using System.Threading;

namespace Mono.Linker.Tests
{
    public class UnitTests : UnitTestsBase
    {
        public UnitTests(ITestOutputHelper o) : base(o) {}


        [Fact]
        public void TestSimple()
        {
            RunTest("Simple");
        }

        [Fact]
        public void TestVirtualCall()
        {
            RunTest("VirtualCall");
        }

        [Fact]
        public void TestMultipleReferences()
        {
            RunTest("MultipleReferences");
        }

    }
}
