#!/usr/bin/env pwsh-preview

$file = gci test/Mono.Linker.Tests/TestResults/*.trx | Sort-Object LastWriteTime | Select-Object -Last 1
echo $file.FullName
$xml = Select-Xml -Path $file.FullName `
    -XPath "/ns:TestRun/ns:Results/ns:UnitTestResult[@outcome='Failed']/@testName" `
    -Namespace @{"ns"="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
# /Results/UnitTestResult[@outcome='Failed']/@testName"
# -Namespace @{}
$failing = $xml | %{ $_.Node.Value } #| %{$_ -replace '^(.*?)\(.*$','$1'}

echo $failing
