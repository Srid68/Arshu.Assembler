# PowerShell script to run JavaScript client-side performance tests
$ErrorActionPreference = "Continue"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$serverDir = Join-Path $scriptDir "csharp\AssemblerWebJs"
$resultFile = Join-Path $serverDir "template_analysis\Reports\javascript_perfsummary.json"
$serverUrl = "http://localhost:5000"
$perfTestUrl = "$serverUrl/?runPerf=true"

Write-Host "`n========== Starting JavaScript Performance Tests ==========" -ForegroundColor Cyan
Write-Host "Server directory: $serverDir" -ForegroundColor Gray
Write-Host "Result file: $resultFile" -ForegroundColor Gray

# Clean up any existing dotnet processes on port 5000
Write-Host "`nCleaning up existing processes..." -ForegroundColor Yellow
$existingProcesses = Get-NetTCPConnection -LocalPort 5000 -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique
if ($existingProcesses) {
    foreach ($processId in $existingProcesses) {
        Write-Host "  Stopping process $processId on port 5000" -ForegroundColor Yellow
        Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
    }
    Start-Sleep -Seconds 2
}

# Start the server
Write-Host "`nStarting AssemblerWebJs server..." -ForegroundColor Cyan
$serverProcess = Start-Process -FilePath "dotnet" -ArgumentList "run --skipIdleTracking" -WorkingDirectory $serverDir -PassThru -WindowStyle Hidden

# Wait for server to be ready
Write-Host "Waiting for server to start..." -ForegroundColor Yellow
$maxWaitSeconds = 30
$waited = 0
$serverReady = $false

while ($waited -lt $maxWaitSeconds) {
    try {
        $response = Invoke-WebRequest -Uri $serverUrl -Method GET -TimeoutSec 2 -ErrorAction SilentlyContinue
        if ($response.StatusCode -eq 200) {
            $serverReady = $true
            Write-Host "Server is ready!" -ForegroundColor Green
            break
        }
    }
    catch {
        # Server not ready yet
    }
    Start-Sleep -Seconds 1
    $waited++
    Write-Host "." -NoNewline
}

if (-not $serverReady) {
    Write-Host "`n❌ Server failed to start within $maxWaitSeconds seconds" -ForegroundColor Red
    Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue
    exit 1
}

# Delete old result file if exists
if (Test-Path $resultFile) {
    Write-Host "`nRemoving old result file..." -ForegroundColor Yellow
    Remove-Item $resultFile -Force
}

# Open browser to trigger performance tests
Write-Host "`nTriggering performance tests in browser..." -ForegroundColor Cyan
Write-Host "Opening: $perfTestUrl" -ForegroundColor Gray

# Use Start-Process to open the URL in the default browser
Start-Process $perfTestUrl

# Wait for the result file to be created
Write-Host "`nWaiting for performance tests to complete..." -ForegroundColor Yellow
$maxTestWaitSeconds = 180  # 3 minutes for tests to complete
$testWaited = 0
$testsComplete = $false

while ($testWaited -lt $maxTestWaitSeconds) {
    if (Test-Path $resultFile) {
        # Check if file has content (not empty)
        $fileInfo = Get-Item $resultFile
        if ($fileInfo.Length -gt 100) {  # Minimal valid JSON should be > 100 bytes
            $testsComplete = $true
            Write-Host "`n✅ Performance tests completed!" -ForegroundColor Green
            Write-Host "Result file size: $($fileInfo.Length) bytes" -ForegroundColor Gray
            break
        }
    }
    Start-Sleep -Seconds 2
    $testWaited += 2
    if ($testWaited % 10 -eq 0) {
        Write-Host "`n  Waited $testWaited seconds..." -ForegroundColor Gray
    }
    else {
        Write-Host "." -NoNewline
    }
}

# Stop the server
Write-Host "`nStopping server..." -ForegroundColor Yellow
Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue

# Wait a moment for process cleanup
Start-Sleep -Seconds 2

if (-not $testsComplete) {
    Write-Host "❌ Performance tests did not complete within $maxTestWaitSeconds seconds" -ForegroundColor Red
    Write-Host "   Please run tests manually by:" -ForegroundColor Yellow
    Write-Host "   1. cd csharp/AssemblerWebJs && dotnet run --skipIdleTracking" -ForegroundColor Yellow
    Write-Host "   2. Open browser to http://localhost:5000" -ForegroundColor Yellow
    Write-Host "   3. Click 'Run Performance Tests' button" -ForegroundColor Yellow
    Write-Host "   4. Wait for completion and check Reports folder" -ForegroundColor Yellow
    exit 1
}

Write-Host "`n========== JavaScript Performance Tests Complete! ==========" -ForegroundColor Green
Write-Host "Results saved to: $resultFile`n" -ForegroundColor Gray
exit 0
