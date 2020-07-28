using System;

namespace console
{
	class Program
	{
		static void Main(string[] args)
		{
			var c = new C();
			Console.WriteLine(c.M(1, 2));
			A();
			B();
			c.M2(3, 4);
		}

		static int A() { return 42; }
		static int B() { return 42; }
	}

	class C
	{
		public int M(int a, int b) {
			return a + b;
		}

		public int M2(int a, int b) {
			return a + b;
		}
	}
}
