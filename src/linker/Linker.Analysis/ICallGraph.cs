using System.Collections.Generic;

namespace Mono.Linker.Analysis
{

	public interface ICallGraph<T>
	{
		ICollection<T> Nodes { get; }
		ICollection<(T, T)> Edges { get; }
		ICollection<(T, T)> Overrides { get; }
		bool IsEntry (T t);
		bool IsInteresting (T t);
	}
}
