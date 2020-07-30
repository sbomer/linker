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
			Console.WriteLine(Dup1("hi111"));
			Console.WriteLine(Dup2("hello222"));
			c.M2(3, 4);
			c.Many(1, 2, 3, 4, 5);
			c.Many2(6, 7, 8, 9, 10);
			var cond = new Cond();
			cond.A();
			cond.B();
		}

		static int A() {
			return 42;
		}
		static int B() {
			return 42;
		}

		static string Dup1(string s) {
			return s + 42 + s_common;
			return s;
		}

		public static string s_common = "common";

		static string Dup2(string s2) {
			return s2 + 42 + s_common;
			return s2;
		}
	}

	class C
	{
		public int M(int a, int b) {
			return a + b;
		}

		public int M2(int a, int b) {
			return a + b;
		}

		public int Many(int a, int b, int c, int d, int e) {
			return a + b + c * d - e;
		}

		public int Many2(int a, int b, int c, int d2, int e) {
			return a + b + c * d2 - e;
		}
	}

	class Cond
	{
		public int A() {
			Console.WriteLine("hi");
			if (true)
				return 42;
			if (false)
				return 43;
		}

		public int B() {
			if (true)
				return 42;
			if (false)
				return 43;
		}
	}
}
