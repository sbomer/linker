using System;
namespace Mono.Linker
{
	public class ConsoleLogger : ILogger
	{
		public void LogMessage (MessageImportance importance, string message, params object[] values)
		{
			Console.WriteLine (message, values);
		}

		public void LogWarning (string message, params object[] values)
		{
			Console.WriteLine ("warning : " + message, values);
		}

		public void LogError (string message, params object[] values)
		{
			Console.WriteLine ("error : " + message, values);
		}
	}
}
