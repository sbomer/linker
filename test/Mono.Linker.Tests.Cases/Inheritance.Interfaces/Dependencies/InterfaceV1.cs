namespace Mono.Linker.Tests.Cases.Inheritance.Interfaces.Dependencies
{
	public interface IFoo
	{
		void Method ();
	}

	public class UseIFoo
	{
		public static void CallIFooMethod(IFoo foo) {
			foo.Method ();
		}
	}
}