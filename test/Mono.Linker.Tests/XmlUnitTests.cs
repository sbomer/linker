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
    public class XmlUnitTests : UnitTestsBase
    {
        public XmlUnitTests(ITestOutputHelper o) : base(o) {}

        
        [Fact]
        public void TestSimpleXml()
        {
            RunXmlTest("SimpleXml");
        }

        /*
        // TODO: investigate why this test is failing
        [Fact]
        public void TestInterface()
        {
            RunXmlTest("Interface");
        }
        */
        
        [Fact]
        public void TestReferenceInVirtualMethod()
        {
            RunXmlTest("ReferenceInVirtualMethod");
        }
        
        [Fact]
        public void TestGenerics()
        {
            RunXmlTest("Generics");
        }
        
        [Fact]
        public void TestNestedNested()
        {
            RunXmlTest("NestedNested");
        }
        
        [Fact]
        public void TestPreserveFieldsRequired()
        {
            RunXmlTest("PreserveFieldsRequired");
        }
        
        /*
        // TODO: port this unit test
        [Fact]
        public void TestReferenceInAttributes()
        {
            RunXmlTest("ReferenceInAttributes");
        }
        */
        
        [Fact]
        public void TestXmlPattern()
        {
            RunXmlTest("XmlPattern");
        }

    }
}
