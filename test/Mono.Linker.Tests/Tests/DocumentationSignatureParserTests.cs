using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Mono.Cecil;
using Mono.Linker.Tests.Cases.Expectations.Assertions;
using Mono.Linker.Tests.Extensions;

namespace Mono.Linker.Tests
{
	[TestFixture]
	public class DocumentationSignatureParserTests
	{
		[TestCaseSource (nameof (GetMemberAssertionsAsArray), new object[] { typeof (DocumentationSignatureParserTests) })]
		public void TestSignatureParsing (IMemberDefinition member, CustomAttribute customAttribute)
		{
			var attributeString = (string) customAttribute.ConstructorArguments[0].Value;
			switch (customAttribute.AttributeType.Name) {
			case nameof (ExpectExactlyResolvedDocumentationSignatureAttribute):
				CheckUniqueParsedString (member, attributeString);
				break;
			case nameof (ExpectGeneratedDocumentationSignatureAttribute):
				CheckGeneratedString (member, attributeString);
				break;
			case nameof (ExpectResolvedDocumentationSignatureAttribute):
				CheckParsedString (member, attributeString);
				break;
			case nameof (ExpectUnresolvedDocumentationSignatureAttribute):
				CheckUnresolvedDocumentationSignature (member, attributeString);
				break;
			default:
				throw new NotImplementedException ();
			}
		}

		public static void CheckUniqueParsedString (IMemberDefinition member, string input)
		{
			var module = (member as TypeDefinition)?.Module ?? member.DeclaringType?.Module;
			Assert.NotNull (module);
			var parseResults = DocumentationSignatureParser.GetSymbolsForDeclarationId (input, module);
			Assert.AreEqual (1, parseResults.Count ());
			Assert.AreEqual (member, parseResults.First ());
		}

		public static void CheckGeneratedString (IMemberDefinition member, string expected)
		{
			var generator = DocumentationSignatureGenerator.Instance;
			var builder = new StringBuilder ();
			switch (member) {
			case TypeDefinition type:
				generator.VisitTypeDefinition (type, builder);
				break;
			case MethodDefinition method:
				generator.VisitMethod (method, builder);
				break;
			case FieldDefinition field:
				generator.VisitField (field, builder);
				break;
			case PropertyDefinition property:
				generator.VisitProperty (property, builder);
				break;
			case EventDefinition evt:
				generator.VisitEvent (evt, builder);
				break;
			default:
				throw new NotImplementedException ();
			}
			Assert.AreEqual (expected, builder.ToString ());
		}

		public static void CheckParsedString (IMemberDefinition member, string input)
		{
			var module = (member as TypeDefinition)?.Module ?? member.DeclaringType?.Module;
			Assert.NotNull (module);
			var parseResults = DocumentationSignatureParser.GetSymbolsForDeclarationId (input, module);
			CollectionAssert.Contains (parseResults, member);
		}

		public static void CheckUnresolvedDocumentationSignature (IMemberDefinition member, string input)
		{
			var module = (member as TypeDefinition)?.Module ?? member.DeclaringType?.Module;
			Assert.NotNull (module);
			var parseResults = DocumentationSignatureParser.GetSymbolsForDeclarationId (input, module);
			CollectionAssert.DoesNotContain (parseResults, member);
		}

		static IEnumerable<(IMemberDefinition member, CustomAttribute ca)> GetMemberAssertions (Type type)
		{
			var resolver = new DefaultAssemblyResolver ();
			resolver.AddSearchDirectory (Path.GetDirectoryName (type.Assembly.Location));
			var assembly = resolver.Resolve (new AssemblyNameReference (type.Assembly.GetName ().Name, null));
			var t = assembly.MainModule.GetType (type.Namespace + "." + type.Name);
			if (t == null)
				throw new InvalidOperationException ($"type {type} not found in {assembly}");
			var results = new List<(IMemberDefinition, CustomAttribute)> ();
			CollectMemberAssertions (t, results);
			return results;
		}

		private static bool IsMemberAssertion (TypeReference attributeType)
		{
			if (attributeType == null)
				return false;

			if (attributeType.Namespace != "Mono.Linker.Tests.Cases.Expectations.Assertions")
				return false;

			return attributeType.Resolve ().DerivesFrom (nameof (BaseMemberAssertionAttribute));
		}

		private static void CollectMemberAssertions (TypeDefinition type, List<(IMemberDefinition, CustomAttribute)> results)
		{
			if (type.HasCustomAttributes) {
				foreach (var ca in type.CustomAttributes) {
					if (!IsMemberAssertion (ca.AttributeType))
						continue;
					results.Add ((type, ca));
				}
			}

			foreach (var m in type.Methods) {
				if (!m.HasCustomAttributes)
					continue;

				foreach (var ca in m.CustomAttributes) {
					if (!IsMemberAssertion (ca.AttributeType))
						continue;
					results.Add ((m, ca));
				}
			}

			foreach (var f in type.Fields) {
				if (!f.HasCustomAttributes)
					continue;

				foreach (var ca in f.CustomAttributes) {
					if (!IsMemberAssertion (ca.AttributeType))
						continue;
					results.Add ((f, ca));
				}
			}

			foreach (var p in type.Properties) {
				if (!p.HasCustomAttributes)
					continue;

				foreach (var ca in p.CustomAttributes) {
					if (!IsMemberAssertion (ca.AttributeType))
						continue;
					results.Add ((p, ca));
				}
			}

			foreach (var e in type.Events) {
				if (!e.HasCustomAttributes)
					continue;

				foreach (var ca in e.CustomAttributes) {
					if (!IsMemberAssertion (ca.AttributeType))
						continue;
					results.Add ((e, ca));
				}
			}

			if (!type.HasNestedTypes)
				return;

			foreach (var nested in type.NestedTypes) {
				CollectMemberAssertions (nested, results);
			}
		}

		static IEnumerable<object[]> GetMemberAssertionsAsArray (Type type)
		{
			return GetMemberAssertions (type).Select (v => new object[] { v.member, v.ca });
		}

		// testcases

		[ExpectGeneratedDocumentationSignature ("T:Mono.Linker.Tests.DocumentationSignatureParserTests.A")]
		[ExpectExactlyResolvedDocumentationSignature ("T:Mono.Linker.Tests.DocumentationSignatureParserTests.A")]
		public class A
		{
			[ExpectGeneratedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.#ctor")]
			[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.#ctor")]
			public A ()
			{
			}

			[ExpectGeneratedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.#ctor(System.Int32)")]
			[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.#ctor(System.Int32)")]
			public A (int a)
			{
			}

			[ExpectGeneratedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.#cctor")]
			[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.#cctor")]
			static A ()
			{
			}

			[ExpectGeneratedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.M(System.Int32[])")]
			[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.M(System.Int32[])")]
			public void M (int[] a)
			{
			}

			[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.M(System.Int32,System.Int32,System.Int32)~System.Int32")]
			public int M (int a, int b, int c)
			{
				return 0;
			}

			[ExpectGeneratedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.MRef(System.Int32@)")]
			[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.MRef(System.Int32@)")]
			public void MRef (ref int a)
			{
			}

			[ExpectGeneratedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.MOut(System.Int32@)")]
			[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.MOut(System.Int32@)")]
			public void MOut (out int a)
			{
				a = 5;
			}

			[ExpectGeneratedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.MIn(System.Int32@)")]
			[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.MIn(System.Int32@)")]
			public void MIn (in int a)
			{
			}

			public static int i;

			[ExpectGeneratedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.MRefReturn")]
			[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.MRefReturn")]
			[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.MRefReturn~System.Int32@")]
			public ref int MRefReturn ()
			{
				return ref i;
			}

			[ExpectGeneratedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.M")]
			[ExpectResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.M")] // binds to both.
			[ExpectResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.M()")] // binds to both.
			public void M ()
			{
			}

			[ExpectGeneratedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.M()")]
			[ExpectResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.M")]
			[ExpectResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.M()")]
			public void M (__arglist)
			{
			}

			[ExpectGeneratedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.M(System.Int32[][])")]
			[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.M(System.Int32[][])")]
			public void M (int[][] a)
			{
			}

			[ExpectGeneratedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.M(System.Int32[][0:,0:,0:])")]
			[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.M(System.Int32[][0:,0:,0:])")]
			public void M (int[,,][] a)
			{
			}

			[ExpectGeneratedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.M(System.Int32[0:,0:])")]
			[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.M(System.Int32[0:,0:])")]
			public void M (int[,] a)
			{
			}

			[ExpectGeneratedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.M(System.Object)")]
			[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.M(System.Object)")]
			public void M (dynamic d)
			{
			}

			[ExpectGeneratedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.M(System.Int32*)")]
			[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.M(System.Int32*)")]
			public unsafe void M (int* a)
			{
			}

			[ExpectGeneratedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.M``1(Mono.Linker.Tests.DocumentationSignatureParserTests.S{Mono.Linker.Tests.DocumentationSignatureParserTests.G{Mono.Linker.Tests.DocumentationSignatureParserTests.A,``0}}**[0:,0:,0:][][][0:,0:]@)")]
			[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.M``1(Mono.Linker.Tests.DocumentationSignatureParserTests.S{Mono.Linker.Tests.DocumentationSignatureParserTests.G{Mono.Linker.Tests.DocumentationSignatureParserTests.A,``0}}**[0:,0:,0:][][][0:,0:]@)")]
			public unsafe void M<T> (ref S<G<A, T>>**[,][][][,,] a)
			{
			}

			[ExpectGeneratedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.M(System.Collections.Generic.List{System.Int32[]})")]
			[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.M(System.Collections.Generic.List{System.Int32[]})")]
			public void M (List<int[]> a)
			{
			}

			[ExpectGeneratedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.M(System.Int32,)")]
			//[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.M(System.Int32,)")]
			// there's no way to reference this, since the parsing logic doesn't like it.
			public void M (int abo, __arglist)
			{
			}

			[ExpectGeneratedDocumentationSignature ("P:Mono.Linker.Tests.DocumentationSignatureParserTests.A.Prop")]
			[ExpectExactlyResolvedDocumentationSignature ("P:Mono.Linker.Tests.DocumentationSignatureParserTests.A.Prop")]
			public int Prop { get; set; }

			[ExpectGeneratedDocumentationSignature ("F:Mono.Linker.Tests.DocumentationSignatureParserTests.A.field")]
			[ExpectExactlyResolvedDocumentationSignature ("F:Mono.Linker.Tests.DocumentationSignatureParserTests.A.field")]
			public int field;


			[ExpectGeneratedDocumentationSignature ("E:Mono.Linker.Tests.DocumentationSignatureParserTests.A.OnEvent")]
			[ExpectExactlyResolvedDocumentationSignature ("E:Mono.Linker.Tests.DocumentationSignatureParserTests.A.OnEvent")]
			public event EventHandler OnEvent;

			[ExpectGeneratedDocumentationSignature ("E:Mono.Linker.Tests.DocumentationSignatureParserTests.A.OnEventInt")]
			[ExpectExactlyResolvedDocumentationSignature ("E:Mono.Linker.Tests.DocumentationSignatureParserTests.A.OnEventInt")]
			public event Action<int> OnEventInt;

			[ExpectGeneratedDocumentationSignature ("T:Mono.Linker.Tests.DocumentationSignatureParserTests.A.Del")]
			[ExpectExactlyResolvedDocumentationSignature ("T:Mono.Linker.Tests.DocumentationSignatureParserTests.A.Del")]
			public delegate int Del (int a, int b);

			[ExpectGeneratedDocumentationSignature ("E:Mono.Linker.Tests.DocumentationSignatureParserTests.A.OnEventDel")]
			[ExpectExactlyResolvedDocumentationSignature ("E:Mono.Linker.Tests.DocumentationSignatureParserTests.A.OnEventDel")]
			public event Del OnEventDel;

			// prevent warning about unused events
			public void UseEvents ()
			{
				OnEventDel?.Invoke (1, 2);
				OnEventInt?.Invoke (1);
				OnEvent?.Invoke (null, null);
			}

			[ExpectGeneratedDocumentationSignature ("P:Mono.Linker.Tests.DocumentationSignatureParserTests.A.Item(System.Int32)")]
			[ExpectExactlyResolvedDocumentationSignature ("P:Mono.Linker.Tests.DocumentationSignatureParserTests.A.Item(System.Int32)")]
			public int this[int i] {
				get => 0;
				set { }
			}

			[ExpectGeneratedDocumentationSignature ("P:Mono.Linker.Tests.DocumentationSignatureParserTests.A.Item(System.String,System.Int32)")]
			[ExpectExactlyResolvedDocumentationSignature ("P:Mono.Linker.Tests.DocumentationSignatureParserTests.A.Item(System.String,System.Int32)")]
			public int this[string s, int i] {
				get => 0;
				set { }
			}

			[ExpectGeneratedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.op_Implicit(Mono.Linker.Tests.DocumentationSignatureParserTests.A)~System.Boolean")]
			[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.op_Implicit(Mono.Linker.Tests.DocumentationSignatureParserTests.A)~System.Boolean")]
			public static implicit operator bool (A a) => false;

			// C# will not generate a return type for this method, but we will.
			// [ExpectGeneratedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.op_Implicit(Mono.Linker.Tests.DocumentationSignatureParserTests.A)~System.Boolean")]
			// [ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.op_Implicit(Mono.Linker.Tests.DocumentationSignatureParserTests.A)~System.Boolean")]
			//public static int op_Implicit (A a) => 0;

			[ExpectGeneratedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.op_UnaryPlus(Mono.Linker.Tests.DocumentationSignatureParserTests.A)")]
			[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.op_UnaryPlus(Mono.Linker.Tests.DocumentationSignatureParserTests.A)")]
			public static A operator + (A a) => null;

			[ExpectGeneratedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.op_Addition(Mono.Linker.Tests.DocumentationSignatureParserTests.A,Mono.Linker.Tests.DocumentationSignatureParserTests.A)")]
			[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.A.op_Addition(Mono.Linker.Tests.DocumentationSignatureParserTests.A,Mono.Linker.Tests.DocumentationSignatureParserTests.A)")]
			public static A operator + (A left, A right) => null;
		}

		public struct S<T>
		{
		}

		[ExpectGeneratedDocumentationSignature ("T:Mono.Linker.Tests.DocumentationSignatureParserTests.A`1")]
		[ExpectExactlyResolvedDocumentationSignature ("T:Mono.Linker.Tests.DocumentationSignatureParserTests.A`1")]
		public class A<T>
		{
			[ExpectGeneratedDocumentationSignature ("P:Mono.Linker.Tests.DocumentationSignatureParserTests.A`1.Item(`0)")]
			[ExpectExactlyResolvedDocumentationSignature ("P:Mono.Linker.Tests.DocumentationSignatureParserTests.A`1.Item(`0)")]
			public int this[T t] {
				get => 0;
				set { }
			}
		}

		[ExpectExactlyResolvedDocumentationSignature ("T:Mono.Linker.Tests.DocumentationSignatureParserTests.B")]
		[ExpectGeneratedDocumentationSignature ("T:Mono.Linker.Tests.DocumentationSignatureParserTests.B")]
		public class B
		{
			[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.B.Method(Mono.Linker.Tests.DocumentationSignatureParserTests.G{Mono.Linker.Tests.DocumentationSignatureParserTests.A{Mono.Linker.Tests.DocumentationSignatureParserTests.B},System.Collections.Generic.List{Mono.Linker.Tests.DocumentationSignatureParserTests.A}})")]
			[ExpectGeneratedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.B.Method(Mono.Linker.Tests.DocumentationSignatureParserTests.G{Mono.Linker.Tests.DocumentationSignatureParserTests.A{Mono.Linker.Tests.DocumentationSignatureParserTests.B},System.Collections.Generic.List{Mono.Linker.Tests.DocumentationSignatureParserTests.A}})")]
			public void Method (G<A<B>, List<A>> l)
			{
			}
		}

		[ExpectGeneratedDocumentationSignature ("T:Mono.Linker.Tests.DocumentationSignatureParserTests.G`2")]
		[ExpectExactlyResolvedDocumentationSignature ("T:Mono.Linker.Tests.DocumentationSignatureParserTests.G`2")]
		public class G<T, U>
		{
			[ExpectGeneratedDocumentationSignature ("T:Mono.Linker.Tests.DocumentationSignatureParserTests.G`2.NG`1")]
			[ExpectExactlyResolvedDocumentationSignature ("T:Mono.Linker.Tests.DocumentationSignatureParserTests.G`2.NG`1")]
			public class NG<V>
			{
				[ExpectGeneratedDocumentationSignature ("T:Mono.Linker.Tests.DocumentationSignatureParserTests.G`2.NG`1.NG2`1")]
				[ExpectExactlyResolvedDocumentationSignature ("T:Mono.Linker.Tests.DocumentationSignatureParserTests.G`2.NG`1.NG2`1")]
				public class NG2<W>
				{
					[ExpectGeneratedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.G`2.NG`1.NG2`1.Method``1(`0,`1,`2,`3,``0)")]
					[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.G`2.NG`1.NG2`1.Method``1(`0,`1,`2,`3,``0)")]
					public void Method<X> (T t, U u, V v, W w, X x)
					{
					}

					[ExpectGeneratedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.G`2.NG`1.NG2`1.Method(Mono.Linker.Tests.DocumentationSignatureParserTests.G{`0,`1}.NG{`2}.NG2{`3})")]
					[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.G`2.NG`1.NG2`1.Method(Mono.Linker.Tests.DocumentationSignatureParserTests.G{`0,`1}.NG{`2}.NG2{`3})")]
					public void Method (NG2<W> n)
					{
					}
				}
			}

			[ExpectGeneratedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.G`2.Method(Mono.Linker.Tests.DocumentationSignatureParserTests.G{`0,`1})")]
			[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.G`2.Method(Mono.Linker.Tests.DocumentationSignatureParserTests.G{`0,`1})")]
			public void Method (G<T, U> g)
			{
			}
		}

		[ExpectGeneratedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.Method")]
		[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.Method")]
		public void Method ()
		{
		}

		[ExpectGeneratedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.Method(System.Int32)")]
		[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.Method(System.Int32)")]
		public void Method (int i)
		{
		}

		[ExpectGeneratedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.IntMethod")]
		[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.IntMethod")]
		public int IntMethod () => 0;

		[ExpectGeneratedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.Method(Mono.Linker.Tests.DocumentationSignatureParserTests.G{Mono.Linker.Tests.DocumentationSignatureParserTests.A,Mono.Linker.Tests.DocumentationSignatureParserTests.A})")]
		[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.Method(Mono.Linker.Tests.DocumentationSignatureParserTests.G{Mono.Linker.Tests.DocumentationSignatureParserTests.A,Mono.Linker.Tests.DocumentationSignatureParserTests.A})")]
		public void Method (G<A, A> g)
		{
		}

		[ExpectGeneratedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.Method(Mono.Linker.Tests.DocumentationSignatureParserTests.G{Mono.Linker.Tests.DocumentationSignatureParserTests.A,Mono.Linker.Tests.DocumentationSignatureParserTests.A}.NG{Mono.Linker.Tests.DocumentationSignatureParserTests.A})")]
		[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.Method(Mono.Linker.Tests.DocumentationSignatureParserTests.G{Mono.Linker.Tests.DocumentationSignatureParserTests.A,Mono.Linker.Tests.DocumentationSignatureParserTests.A}.NG{Mono.Linker.Tests.DocumentationSignatureParserTests.A})")]
		public void Method (G<A, A>.NG<A> g)
		{
		}

		public class Invalid
		{
			[ExpectUnresolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.NoReturnType~")]
			public int NoReturnType () => 0;

			[ExpectUnresolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.NoParameters(,)")]
			public void NoParameters (int a, int b)
			{
			}

			[ExpectUnresolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.NoClosingParen(")]
			public void NoClosingParen () { }

			[ExpectUnresolvedDocumentationSignature ("T:Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.Whitespace ")]
			[ExpectUnresolvedDocumentationSignature (" T:Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.Whitespace")]
			[ExpectUnresolvedDocumentationSignature ("T: Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.Whitespace")]
			[ExpectUnresolvedDocumentationSignature ("T :Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.Whitespace")]
			[ExpectUnresolvedDocumentationSignature ("")]
			[ExpectUnresolvedDocumentationSignature (" ")]
			public class Whitespace
			{
				[ExpectUnresolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.Whitespace.Method(System.Int32, System.Int32)")]
				public void Method (int a, int b)
				{
				}

				[ExpectUnresolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.Whitespace.Method(Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.Generic{System.Int32, System.Int32})")]
				public void Method (Generic<int, int> g)
				{
				}
			}

			public class Generic<T, U>
			{
			}

			[ExpectUnresolvedDocumentationSignature ("T:Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.Generic{`1}")]
			[ExpectUnresolvedDocumentationSignature ("T:Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.Generic{T}")]
			[ExpectUnresolvedDocumentationSignature ("T:Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.Generic<T>")]
			public class Generic<T>
			{
				[ExpectUnresolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.Generic``1.MethodSyntaxForTypeParameter(`0)")]

				public void MethodSyntaxForTypeParameter (T t)
				{
				}

				[ExpectUnresolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.Generic`1.MethodSyntaxForTypeGenericArgument(Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.Generic{``0})")]
				public void MethodSyntaxForTypeGenericArgument (Generic<T> g)
				{
				}

				[ExpectUnresolvedDocumentationSignature ("P:Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.Generic`1.Item(``0)")]
				public bool this[T t] {
					get => false;
					set { }
				}
			}

			[ExpectUnresolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.MethodWithGenericInstantiation(Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.Generic`1)")]
			public void MethodWithGenericInstantiation (Generic<A> g)
			{
			}

			[ExpectUnresolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.Method(System.Int32[:,:])")]
			[ExpectUnresolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.Method(System.Int32[0:,)")]
			public void Method (int[,] a)
			{
			}

			[ExpectUnresolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.NonGenericMethod(``0)")]
			public void NonGenericMethod (int i)
			{
			}

			[ExpectUnresolvedDocumentationSignature ("P:Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.Item(`0)")]
			[ExpectUnresolvedDocumentationSignature ("P:Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.Item(``0)")]
			public int this[int i] {
				get => 0;
				set { }
			}

			[ExpectUnresolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.MethodMissingArgumentTypeName(System.)")]
			public void MethodMissingArgumentTypeName (int i)
			{
			}

			[ExpectUnresolvedDocumentationSignature ("T:Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.")]
			public class NoType
			{
			}

			[ExpectUnresolvedDocumentationSignature ("T:Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.NoParameterType()")]
			public void NoParameterType (int i)
			{
			}

			[ExpectUnresolvedDocumentationSignature ("T:Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.NoParameterType(Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.Generic{})")]
			public void NoGenericParameterType (Generic<A> g)
			{
			}

			// these work, but seem like they shouldn't.
			// see https://github.com/dotnet/roslyn/issues/44315

			[ExpectExactlyResolvedDocumentationSignature ("TMono.Linker.Tests.DocumentationSignatureParserTests.Invalid.NoColon")]
			public class NoColon
			{
			}

			[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.NoClosingParenWithParameters(System.Int32")]
			public void NoClosingParenWithParameters (int a)
			{
			}

			[ExpectExactlyResolvedDocumentationSignature ("M:Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.NoClosingBrace(Mono.Linker.Tests.DocumentationSignatureParserTests.Invalid.Generic{Mono.Linker.Tests.DocumentationSignatureParserTests.A)")]
			public void NoClosingBrace (Generic<A> g)
			{
			}
		}
	}
}