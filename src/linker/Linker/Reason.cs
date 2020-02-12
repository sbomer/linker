using System;

namespace Mono.Linker
{
	public struct Reason<T0> {
		private readonly T0 o;
		public Reason(T0 o) {
			this.o = o;
		}

		public void Switch(Action<T0> f0) {
			f0(o);
		}
	}


#pragma warning disable CS8509
	public struct Reason<T0, T1> {
		private readonly object o;

		public Reason(T0 o)
		{
			this.o = o;
		}

		public Reason(T1 o) {
			this.o = o;
		}

		public T Switch<T>(Func<T0, T> f0, Func<T1, T> f1) => o switch {
			T0 o => f0(o),
			T1 o => f1(o),
		};

		public void Switch(
			Action<T0> f0,
			Action<T1> f1) {
			switch (o) {
			case T0 o:
				f0(o);
				break;
			case T1 o:
				f1(o);
				break;
			}
		}
	}

	public struct Reason<T0, T1, T2> {
		private readonly object o;
		public Reason(T0 o)
		{
			this.o = o;
		}

		public Reason(T1 o)
		{
			this.o = o;
		}
		public Reason(T2 o)
		{
			this.o = o;
		}

		public T Switch<T>(
			Func<T0, T> f0,
			Func<T1, T> f1,
			Func<T2, T> f2)
		=> o switch {
			T0 o => f0(o),
			T1 o => f1(o),
			T2 o => f2(o),
		};

		public void Switch(
			Action<T0> f0,
			Action<T1> f1,
			Action<T2> f2) {
			switch (o) {
			case T0 o:
				f0(o);
				break;
			case T1 o:
				f1(o);
				break;
			case T2 o:
				f2(o);
				break;
			}
		}
	}
#pragma warning restore CS8509
}