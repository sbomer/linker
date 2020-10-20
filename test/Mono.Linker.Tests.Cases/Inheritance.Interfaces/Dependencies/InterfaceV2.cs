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

	public interface IGenFoo<T>
	{
		void Method (T t);
	}

	public interface IGenBar<T> : IGenFoo<T>
	{
		void BarMethod (T t);

		void IGenFoo<T>.Method(T t) { }
	}
}