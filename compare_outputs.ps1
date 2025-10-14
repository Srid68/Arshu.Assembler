# PowerShell script to compare C# vs Rust, Node, Go, and PHP HTML outputs for all scenarios
param(
    [string]$Scenario = "",
    [switch]$All = $false
)

$allScenarios = @(    "HtmlRule1A", "HtmlRule1B", "HtmlRule2A", "HtmlRule2B", "HtmlRule2C",
    "HtmlRule2D", "HtmlRule2E", "HtmlRule2F", "HtmlRule3A", "HtmlRule3B",
    "Index",
    "JsonRule1A", "JsonRule1B", "JsonRule1C", "JsonRule1D", "JsonRule2A", "JsonRule2B",
    "JsonRule2C", "JsonRule2D", "JsonRule3AHtml2A"
)

# Determine which scenarios to test
if ($All) {
    $scenarios = $allScenarios
} elseif ($Scenario -ne "") {
    $scenarios = @($Scenario)
} else {
    # Default to first scenario if no parameter provided
    $scenarios = @("HtmlRule1A")
    Write-Host "No scenario specified. Testing first scenario: HtmlRule1A" -ForegroundColor Yellow
    Write-Host "Usage: .\compare_outputs.ps1 -Scenario <ScenarioName>  OR  .\compare_outputs.ps1 -All" -ForegroundColor Yellow
    Write-Host ""
}
    
    # Run Rust test
    Write-Host "  Running Rust..." -ForegroundColor Red
    Set-Location "rust\AssemblerTest"
    $rustOutput = & cargo run --release -- --skipdetails --printhtml --advancedtests --appsite $scenario 2>&1
    Set-Location "..\..\"
    
    # Extract Rust output size from DETAILED OUTPUT ANALYSIS
    $rustAnalysis = $rustOutput | Select-String "Normal length: (\d+) chars" | Select-Object -First 1
    $rustNormalSize = if ($rustAnalysis) { $rustAnalysis.Matches[0].Groups[1].Value } else { "ERROR" }

    $rustPreProcessAnalysis = $rustOutput | Select-String "PreProcess length: (\d+) chars" | Select-Object -First 1
    $rustPreProcessSize = if ($rustPreProcessAnalysis) { $rustPreProcessAnalysis.Matches[0].Groups[1].Value } else { "ERROR" }    
    # Run Go test
    Write-Host "  Running Go..." -ForegroundColor Green
    Set-Location "go\AssemblerTest"
    $goOutput = & go run . --skipdetails --printhtml --advancedtests --appsite $scenario 2>&1
    Set-Location "..\..\"
    
    # Extract Go output size from DETAILED OUTPUT ANALYSIS
    $goAnalysis = $goOutput | Select-String "Normal length: (\d+) chars" | Select-Object -First 1
    $goNormalSize = if ($goAnalysis) { $goAnalysis.Matches[0].Groups[1].Value } else { "ERROR" }

    $goPreProcessAnalysis = $goOutput | Select-String "PreProcess length: (\d+) chars" | Select-Object -First 1
    $goPreProcessSize = if ($goPreProcessAnalysis) { $goPreProcessAnalysis.Matches[0].Groups[1].Value } else { "ERROR" }    
    # Run Node test
    Write-Host "  Running Node..." -ForegroundColor Magenta
    Set-Location "node\AssemblerTest"
    $nodeOutput = & node index.js --skipdetails --printhtml --advancedtests --appsite $scenario 2>&1
    Set-Location "..\..\"
    
    # Extract Node output size from DETAILED OUTPUT ANALYSIS
    $nodeAnalysis = $nodeOutput | Select-String "Normal length: (\d+) chars" | Select-Object -First 1
    $nodeNormalSize = if ($nodeAnalysis) { $nodeAnalysis.Matches[0].Groups[1].Value } else { "ERROR" }

    $nodePreProcessAnalysis = $nodeOutput | Select-String "PreProcess length: (\d+) chars" | Select-Object -First 1
    $nodePreProcessSize = if ($nodePreProcessAnalysis) { $nodePreProcessAnalysis.Matches[0].Groups[1].Value } else { "ERROR" }    
    # Run PHP test
    Write-Host "  Running PHP..." -ForegroundColor Blue
    Set-Location "php\AssemblerTest"
    $phpOutput = & php index.php --skipdetails --printhtml --advancedtests --appsite $scenario 2>&1
    Set-Location "..\..\"
    
    # Extract PHP output size from DETAILED OUTPUT ANALYSIS
    $phpAnalysis = $phpOutput | Select-String "Normal length: (\d+) chars" | Select-Object -First 1
    $phpNormalSize = if ($phpAnalysis) { $phpAnalysis.Matches[0].Groups[1].Value } else { "ERROR" }

    $phpPreProcessAnalysis = $phpOutput | Select-String "PreProcess length: (\d+) chars" | Select-Object -First 1
    $phpPreProcessSize = if ($phpPreProcessAnalysis) { $phpPreProcessAnalysis.Matches[0].Groups[1].Value } else { "ERROR" }    
    # Compare results - all languages now count UTF-16 characters
    $rustNormalMatch = if ($csharpNormalSize -eq $rustNormalSize -and $csharpNormalSize -ne "ERROR") { "✅" } else { "❌" }
    $goNormalMatch = if ($csharpNormalSize -eq $goNormalSize -and $csharpNormalSize -ne "ERROR") { "✅" } else { "❌" }
    $nodeNormalMatch = if ($csharpNormalSize -eq $nodeNormalSize -and $csharpNormalSize -ne "ERROR") { "✅" } else { "❌" }
    $phpNormalMatch = if ($csharpNormalSize -eq $phpNormalSize -and $csharpNormalSize -ne "ERROR") { "✅" } else { "❌" }

    $rustPreProcessMatch = if ($csharpPreProcessSize -eq $rustPreProcessSize -and $csharpPreProcessSize -ne "ERROR") { "✅" } else { "❌" }
    $goPreProcessMatch = if ($csharpPreProcessSize -eq $goPreProcessSize -and $csharpPreProcessSize -ne "ERROR") { "✅" } else { "❌" }
    $nodePreProcessMatch = if ($csharpPreProcessSize -eq $nodePreProcessSize -and $csharpPreProcessSize -ne "ERROR") { "✅" } else { "❌" }
    $phpPreProcessMatch = if ($csharpPreProcessSize -eq $phpPreProcessSize -and $csharpPreProcessSize -ne "ERROR") { "✅" } else { "❌" }

    $result = [PSCustomObject]@{
        Scenario = $scenario
        "CSharp Normal" = $csharpNormalSize
        "Rust Normal" = "$rustNormalSize $rustNormalMatch"
        "Go Normal" = "$goNormalSize $goNormalMatch"
        "Node Normal" = "$nodeNormalSize $nodeNormalMatch"
        "PHP Normal" = "$phpNormalSize $phpNormalMatch"
        "CSharp PreProcess" = $csharpPreProcessSize
        "Rust PreProcess" = "$rustPreProcessSize $rustPreProcessMatch"
        "Go PreProcess" = "$goPreProcessSize $goPreProcessMatch"
        "Node PreProcess" = "$nodePreProcessSize $nodePreProcessMatch"
        "PHP PreProcess" = "$phpPreProcessSize $phpPreProcessMatch"
    }
    
    $results += $result

    Write-Host "  Normal Mode:" -ForegroundColor White
    Write-Host "    C#: $csharpNormalSize chars" -ForegroundColor White
    Write-Host "    Rust: $rustNormalSize chars $rustNormalMatch" -ForegroundColor $(if ($rustNormalMatch -eq "✅") { "Green" } else { "Red" })
    Write-Host "    Go: $goNormalSize chars $goNormalMatch" -ForegroundColor $(if ($goNormalMatch -eq "✅") { "Green" } else { "Red" })
    Write-Host "    Node: $nodeNormalSize chars $nodeNormalMatch" -ForegroundColor $(if ($nodeNormalMatch -eq "✅") { "Green" } else { "Red" })
    Write-Host "    PHP: $phpNormalSize chars $phpNormalMatch" -ForegroundColor $(if ($phpNormalMatch -eq "✅") { "Green" } else { "Red" })
    Write-Host "  PreProcess Mode:" -ForegroundColor White
    Write-Host "    C#: $csharpPreProcessSize chars" -ForegroundColor White
    Write-Host "    Rust: $rustPreProcessSize chars $rustPreProcessMatch" -ForegroundColor $(if ($rustPreProcessMatch -eq "✅") { "Green" } else { "Red" })
    Write-Host "    Go: $goPreProcessSize chars $goPreProcessMatch" -ForegroundColor $(if ($goPreProcessMatch -eq "✅") { "Green" } else { "Red" })
    Write-Host "    Node: $nodePreProcessSize chars $nodePreProcessMatch" -ForegroundColor $(if ($nodePreProcessMatch -eq "✅") { "Green" } else { "Red" })
    Write-Host "    PHP: $phpPreProcessSize chars $phpPreProcessMatch" -ForegroundColor $(if ($phpPreProcessMatch -eq "✅") { "Green" } else { "Red" })
    Write-Host ""
}

# Display summary
Write-Host "FINAL MULTI-LANGUAGE COMPARISON SUMMARY" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
$results | Format-Table -AutoSize

# Count matches vs mismatches for each language
$rustNormalMatches = ($results | Where-Object { $_."Rust Normal" -like "*✅*" }).Count
$goNormalMatches = ($results | Where-Object { $_."Go Normal" -like "*✅*" }).Count
$nodeNormalMatches = ($results | Where-Object { $_."Node Normal" -like "*✅*" }).Count
$phpNormalMatches = ($results | Where-Object { $_."PHP Normal" -like "*✅*" }).Count

$rustNormalMismatches = ($results | Where-Object { $_."Rust Normal" -like "*❌*" }).Count
$goNormalMismatches = ($results | Where-Object { $_."Go Normal" -like "*❌*" }).Count
$nodeNormalMismatches = ($results | Where-Object { $_."Node Normal" -like "*❌*" }).Count
$phpNormalMismatches = ($results | Where-Object { $_."PHP Normal" -like "*❌*" }).Count

$rustPreProcessMatches = ($results | Where-Object { $_."Rust PreProcess" -like "*✅*" }).Count
$goPreProcessMatches = ($results | Where-Object { $_."Go PreProcess" -like "*✅*" }).Count
$nodePreProcessMatches = ($results | Where-Object { $_."Node PreProcess" -like "*✅*" }).Count
$phpPreProcessMatches = ($results | Where-Object { $_."PHP PreProcess" -like "*✅*" }).Count

$rustPreProcessMismatches = ($results | Where-Object { $_."Rust PreProcess" -like "*❌*" }).Count
$goPreProcessMismatches = ($results | Where-Object { $_."Go PreProcess" -like "*❌*" }).Count
$nodePreProcessMismatches = ($results | Where-Object { $_."Node PreProcess" -like "*❌*" }).Count
$phpPreProcessMismatches = ($results | Where-Object { $_."PHP PreProcess" -like "*❌*" }).Count

Write-Host "LANGUAGE COMPARISON RESULTS:" -ForegroundColor White
Write-Host "============================" -ForegroundColor White
Write-Host "Total scenarios tested: $($results.Count)" -ForegroundColor White
Write-Host ""
Write-Host "NORMAL MODE:" -ForegroundColor Cyan
Write-Host "Rust vs C#:" -ForegroundColor Red
Write-Host "  Exact matches: $rustNormalMatches" -ForegroundColor Green
Write-Host "  Mismatches: $rustNormalMismatches" -ForegroundColor Red
Write-Host ""
Write-Host "Go vs C#:" -ForegroundColor Green
Write-Host "  Exact matches: $goNormalMatches" -ForegroundColor Green
Write-Host "  Mismatches: $goNormalMismatches" -ForegroundColor Red
Write-Host ""
Write-Host "Node vs C#:" -ForegroundColor Magenta
Write-Host "  Exact matches: $nodeNormalMatches" -ForegroundColor Green
Write-Host "  Mismatches: $nodeNormalMismatches" -ForegroundColor Red
Write-Host ""
Write-Host "PHP vs C#:" -ForegroundColor Blue
Write-Host "  Exact matches: $phpNormalMatches" -ForegroundColor Green
Write-Host "  Mismatches: $phpNormalMismatches" -ForegroundColor Red
Write-Host ""
Write-Host "PREPROCESS MODE:" -ForegroundColor Cyan
Write-Host "Rust vs C#:" -ForegroundColor Red
Write-Host "  Exact matches: $rustPreProcessMatches" -ForegroundColor Green
Write-Host "  Mismatches: $rustPreProcessMismatches" -ForegroundColor Red
Write-Host ""
Write-Host "Go vs C#:" -ForegroundColor Green
Write-Host "  Exact matches: $goPreProcessMatches" -ForegroundColor Green
Write-Host "  Mismatches: $goPreProcessMismatches" -ForegroundColor Red
Write-Host ""
Write-Host "Node vs C#:" -ForegroundColor Magenta
Write-Host "  Exact matches: $nodePreProcessMatches" -ForegroundColor Green
Write-Host "  Mismatches: $nodePreProcessMismatches" -ForegroundColor Red
Write-Host ""
Write-Host "PHP vs C#:" -ForegroundColor Blue
Write-Host "  Exact matches: $phpPreProcessMatches" -ForegroundColor Green
Write-Host "  Mismatches: $phpPreProcessMismatches" -ForegroundColor Red
Write-Host ""

# Overall assessment
$totalMatches = $rustNormalMatches + $goNormalMatches + $nodeNormalMatches + $phpNormalMatches + $rustPreProcessMatches + $goPreProcessMatches + $nodePreProcessMatches + $phpPreProcessMatches
$totalMismatches = $rustNormalMismatches + $goNormalMismatches + $nodeNormalMismatches + $phpNormalMismatches + $rustPreProcessMismatches + $goPreProcessMismatches + $nodePreProcessMismatches + $phpPreProcessMismatches
$totalTests = ($results.Count * 8)

if ($totalMismatches -eq 0) {
    Write-Host "🎉 SUCCESS: All languages produce identical outputs to C# in both Normal and PreProcess modes!" -ForegroundColor Green
} else {
    Write-Host "⚠️  WARNING: $totalMismatches out of $totalTests comparisons failed" -ForegroundColor Yellow
    Write-Host "Some language implementations may not be producing exactly the same results as C#" -ForegroundColor Yellow

    if ($rustNormalMismatches -eq 0 -and $rustPreProcessMismatches -eq 0) { Write-Host "✅ Rust implementation is perfect!" -ForegroundColor Green }
    if ($goNormalMismatches -eq 0 -and $goPreProcessMismatches -eq 0) { Write-Host "✅ Go implementation is perfect!" -ForegroundColor Green }
    if ($nodeNormalMismatches -eq 0 -and $nodePreProcessMismatches -eq 0) { Write-Host "✅ Node implementation is perfect!" -ForegroundColor Green }
    if ($phpNormalMismatches -eq 0 -and $phpPreProcessMismatches -eq 0) { Write-Host "✅ PHP implementation is perfect!" -ForegroundColor Green }
}