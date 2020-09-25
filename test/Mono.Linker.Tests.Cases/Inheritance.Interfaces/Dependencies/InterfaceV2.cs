using System;

namespace Mono.Linker.Tests.Cases.Inheritance.Interfaces.Dependencies
{
	public interface IFoo : IFooDefault
	{
		void Method ();
	}

	public interface IFooDefault
	{
		void MethodDefault () {
			Console.WriteLine("IFooDefault.MethodDefault");	
		}
	}

	public class UseIFoo
	{
		public static void CallIFooMethod(IFoo foo) {
			foo.Method ();
			foo.MethodDefault();
		}
	}
}