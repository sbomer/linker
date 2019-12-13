# ILLink Roslyn analyzer

This is a very early start on a Roslyn analyzer for linker-unsafe patterns. I started with [DiagnosticAnalyzer template](https://github.com/dotnet/roslyn-sdk/tree/master/src/VisualStudio.Roslyn.SDK/Roslyn.SDK/ProjectTemplates/CSharp/Diagnostic/Analyzer) mentioned in [Tutorial: Write your first analyzer and code fix](https://docs.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/tutorials/how-to-write-csharp-analyzer-code-fix).

## Building

```
> dotnet build illink.sln
```

For some reason I have trouble packaging this project in the linker source tree. In theory this should work:
```
> dotnet pack src/ILLink.Analyzer/ILLink.Analyzer.csproj
```
which should output the package into `src/ILLink.Analyzer/bin/Debug/ILLink.Analyzer.1.0.0.nupkg`. If it doesn't work, try
```
> nuget pack src/ILLink.Analyzer/ILLink.Analyzer.csproj
```
which will place it in the repo root directory.

## Trying it out

Reference the analyzer and let NuGet find the built package. Replace the path below with path to the output directory containing the `.nupkg` you just built.
```xml
<PackageReference Include="ILLink.Analyzer" />
```

```
> dotnet new nugetconfig
```

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <!--To inherit the global NuGet package sources remove the <clear/> line below -->
    <clear />
    <add key="nuget" value="https://api.nuget.org/v3/index.json" />
    <add key="local analyzer" value="path/to/nupkg/directory/" />
  </packageSources>
</configuration>
```

The analyzer should now be enabled for your project (you may need to restore again).

To use it with OmniSharp in VS Code, ensure you are, ensure you are using the C# extension version `1.19.0` or higher (or at least OmniSharp `1.32.13`), and enable support for analyzers using an `omnisharp.json` file:

```json
{
    "RoslynExtensionsOptions": {
        "enableAnalyzersSupport": true
    }
}
```

For different ways to enable this setting, see https://www.strathweb.com/2019/04/roslyn-analyzers-in-code-fixes-in-omnisharp-and-vs-code/

## Iterating

Here's the inner loop that has been working for me:
- Make an update to the analyzer
- Rebuild it
- Re-package
- Remove the cached version of the package from your local nuget cache
- Re-restore your test project
- Restart OmniSharp (using the VS Code Command Palette)