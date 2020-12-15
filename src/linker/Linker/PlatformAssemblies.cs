namespace Mono.Linker
{
	public static class PlatformAssemblies
	{
#if FEATURE_ILLINK
		public const string CoreLib = "System.Private.CoreLib";
#else
		public const string CoreLib = "mscorlib";
#endif
	}
}