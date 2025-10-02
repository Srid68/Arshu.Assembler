# PowerShell script to compare C# vs Rust, Node, Go, and PHP HTML outputs for all scenarios
$scenarios = @(
    "HtmlRule1A", "HtmlRule1B", "HtmlRule2A", "HtmlRule2B", "HtmlRule2C", 
    "HtmlRule2D", "HtmlRule2E", "HtmlRule2F", "HtmlRule3A", "HtmlRule3B",
    "JsonRule1A", "JsonRule1B", "JsonRule1C", "JsonRule2A", "JsonRule2B", 
    "JsonRule2C", "JsonRule2D", "Test0", "Test1", "Test2", "Test3", "Test4", "Test5"
)

$results = @()

Write-Host "Testing HTML output comparison between C# and all other languages..." -ForegroundColor Magenta
Write-Host "=====================================================================" -ForegroundColor Magenta

foreach ($scenario in $scenarios) {
    Write-Host "Testing scenario: $scenario" -ForegroundColor Yellow
    
    # Run C# test (reference implementation)
    Write-Host "  Running C#..." -ForegroundColor Cyan
    $csharpOutput = & dotnet run --project csharp/AssemblerTest/AssemblerTest.csproj -- --printhtml --appsite $scenario 2>&1
    
    # Extract C# output size - look for the performance line which shows "Normal: X chars"
    $csharpMatch = $csharpOutput | Select-String "Normal: (\d+) chars" | Select-Object -First 1
    $csharpSize = if ($csharpMatch) { $csharpMatch.Matches[0].Groups[1].Value } else { "ERROR" }
    
    # Run Rust test
    Write-Host "  Running Rust..." -ForegroundColor Red
    Set-Location "rust\AssemblerTest"
    $rustOutput = & cargo run -- --printhtml --appsite $scenario 2>&1
    Set-Location "..\..\"
    
    # Extract Rust output size - look for the performance line which shows "Output size: X chars"
    $rustMatch = $rustOutput | Select-String "Normal Engine.*Output size: (\d+) chars" | Select-Object -First 1
    $rustSize = if ($rustMatch) { $rustMatch.Matches[0].Groups[1].Value } else { "ERROR" }
    
    # Run Go test
    Write-Host "  Running Go..." -ForegroundColor Green
    Set-Location "go\AssemblerTest"
    $goOutput = & go run . --printhtml --appsite $scenario 2>&1
    Set-Location "..\..\"
    
    # Extract Go output size - look for the performance line which shows "Output size: X chars"
    $goMatch = $goOutput | Select-String "Normal Engine.*Output size: (\d+) chars" | Select-Object -First 1
    $goSize = if ($goMatch) { $goMatch.Matches[0].Groups[1].Value } else { "ERROR" }
    
    # Run Node test
    Write-Host "  Running Node..." -ForegroundColor Magenta
    Set-Location "node\AssemblerTest"
    $nodeOutput = & node index.js --printhtml --appsite $scenario 2>&1
    Set-Location "..\..\"
    
    # Extract Node output size - look for the performance line which shows "Normal: X chars"
    $nodeMatch = $nodeOutput | Select-String "Normal: (\d+) chars" | Select-Object -First 1
    $nodeSize = if ($nodeMatch) { $nodeMatch.Matches[0].Groups[1].Value } else { "ERROR" }
    
    # Run PHP test
    Write-Host "  Running PHP..." -ForegroundColor Blue
    Set-Location "php\AssemblerTest"
    $phpOutput = & php index.php --printhtml --appsite $scenario 2>&1
    Set-Location "..\..\"
    
    # Extract PHP output size - look for the performance line which shows "Normal: X chars"
    $phpMatch = $phpOutput | Select-String "Normal: (\d+) chars" | Select-Object -First 1
    $phpSize = if ($phpMatch) { $phpMatch.Matches[0].Groups[1].Value } else { "ERROR" }
    
    # Compare results
    $rustMatch = if ($csharpSize -eq $rustSize -and $csharpSize -ne "ERROR") { "✅" } else { "❌" }
    $goMatch = if ($csharpSize -eq $goSize -and $csharpSize -ne "ERROR") { "✅" } else { "❌" }
    $nodeMatch = if ($csharpSize -eq $nodeSize -and $csharpSize -ne "ERROR") { "✅" } else { "❌" }
    $phpMatch = if ($csharpSize -eq $phpSize -and $csharpSize -ne "ERROR") { "✅" } else { "❌" }
    
    $result = [PSCustomObject]@{
        Scenario = $scenario
        CSharp = $csharpSize
        Rust = "$rustSize $rustMatch"
        Go = "$goSize $goMatch"
        Node = "$nodeSize $nodeMatch"
        PHP = "$phpSize $phpMatch"
        RustDiff = if ($csharpSize -ne "ERROR" -and $rustSize -ne "ERROR") { [int]$csharpSize - [int]$rustSize } else { "N/A" }
        GoDiff = if ($csharpSize -ne "ERROR" -and $goSize -ne "ERROR") { [int]$csharpSize - [int]$goSize } else { "N/A" }
        NodeDiff = if ($csharpSize -ne "ERROR" -and $nodeSize -ne "ERROR") { [int]$csharpSize - [int]$nodeSize } else { "N/A" }
        PHPDiff = if ($csharpSize -ne "ERROR" -and $phpSize -ne "ERROR") { [int]$csharpSize - [int]$phpSize } else { "N/A" }
    }
    
    $results += $result
    
    Write-Host "  C#: $csharpSize chars" -ForegroundColor White
    Write-Host "  Rust: $rustSize chars $rustMatch" -ForegroundColor $(if ($rustMatch -eq "✅") { "Green" } else { "Red" })
    Write-Host "  Go: $goSize chars $goMatch" -ForegroundColor $(if ($goMatch -eq "✅") { "Green" } else { "Red" })
    Write-Host "  Node: $nodeSize chars $nodeMatch" -ForegroundColor $(if ($nodeMatch -eq "✅") { "Green" } else { "Red" })
    Write-Host "  PHP: $phpSize chars $phpMatch" -ForegroundColor $(if ($phpMatch -eq "✅") { "Green" } else { "Red" })
    Write-Host ""
}

# Display summary
Write-Host "FINAL MULTI-LANGUAGE COMPARISON SUMMARY" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
$results | Format-Table -AutoSize

# Count matches vs mismatches for each language
$rustMatches = ($results | Where-Object { $_.Rust -like "*✅*" }).Count
$goMatches = ($results | Where-Object { $_.Go -like "*✅*" }).Count
$nodeMatches = ($results | Where-Object { $_.Node -like "*✅*" }).Count
$phpMatches = ($results | Where-Object { $_.PHP -like "*✅*" }).Count

$rustMismatches = ($results | Where-Object { $_.Rust -like "*❌*" }).Count
$goMismatches = ($results | Where-Object { $_.Go -like "*❌*" }).Count
$nodeMismatches = ($results | Where-Object { $_.Node -like "*❌*" }).Count
$phpMismatches = ($results | Where-Object { $_.PHP -like "*❌*" }).Count

Write-Host "LANGUAGE COMPARISON RESULTS:" -ForegroundColor White
Write-Host "============================" -ForegroundColor White
Write-Host "Total scenarios tested: $($results.Count)" -ForegroundColor White
Write-Host ""
Write-Host "Rust vs C#:" -ForegroundColor Red
Write-Host "  Exact matches: $rustMatches" -ForegroundColor Green
Write-Host "  Mismatches: $rustMismatches" -ForegroundColor Red
Write-Host ""
Write-Host "Go vs C#:" -ForegroundColor Green  
Write-Host "  Exact matches: $goMatches" -ForegroundColor Green
Write-Host "  Mismatches: $goMismatches" -ForegroundColor Red
Write-Host ""
Write-Host "Node vs C#:" -ForegroundColor Magenta
Write-Host "  Exact matches: $nodeMatches" -ForegroundColor Green
Write-Host "  Mismatches: $nodeMismatches" -ForegroundColor Red
Write-Host ""
Write-Host "PHP vs C#:" -ForegroundColor Blue
Write-Host "  Exact matches: $phpMatches" -ForegroundColor Green
Write-Host "  Mismatches: $phpMismatches" -ForegroundColor Red
Write-Host ""

# Overall assessment
$totalMatches = $rustMatches + $goMatches + $nodeMatches + $phpMatches
$totalMismatches = $rustMismatches + $goMismatches + $nodeMismatches + $phpMismatches
$totalTests = ($results.Count * 4)

if ($totalMismatches -eq 0) {
    Write-Host "🎉 SUCCESS: All languages produce identical outputs to C#!" -ForegroundColor Green
} else {
    Write-Host "⚠️  WARNING: $totalMismatches out of $totalTests comparisons failed" -ForegroundColor Yellow
    Write-Host "Some language implementations may not be producing exactly the same results as C#" -ForegroundColor Yellow
    
    if ($rustMismatches -eq 0) { Write-Host "✅ Rust implementation is perfect!" -ForegroundColor Green }
    if ($goMismatches -eq 0) { Write-Host "✅ Go implementation is perfect!" -ForegroundColor Green }  
    if ($nodeMismatches -eq 0) { Write-Host "✅ Node implementation is perfect!" -ForegroundColor Green }
    if ($phpMismatches -eq 0) { Write-Host "✅ PHP implementation is perfect!" -ForegroundColor Green }
}