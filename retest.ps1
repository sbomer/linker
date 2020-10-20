#!/usr/bin/env pwsh-preview

$file = gci test/Mono.Linker.Tests/TestResults/*.trx | Sort-Object LastWriteTime | Select-Object -Last 1
echo $file.FullName
$xml = Select-Xml -Path $file.FullName `
    -XPath "/ns:TestRun/ns:Results/ns:UnitTestResult[@outcome='Failed']/@testName" `
    -Namespace @{"ns"="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
# /Results/UnitTestResult[@outcome='Failed']/@testName"
# -Namespace @{}
$failing = $xml | %{ $_.Node.Value } #| %{$_ -replace '^(.*?)\(.*$','$1'}
$all_failing = $failing | Join-String -Separator ','

$all_failing_or = $failing | Join-String -Separator '|'
#echo $all_failing
dotnet test test/Mono.Linker.Tests/Mono.Linker.Tests.csproj --filter $all_failing_or -l trx
