@echo off
REM Run performance tests for all languages

REM C# cd /d "./csharp/AssemblerTest" && dotnet run -c Release && cd /d ../../
cd /d "%~dp0csharp\AssemblerTest" && dotnet run -c Release -- --skipdetails --standardtests && cd /d "%~dp0"
cd /d "%~dp0csharp\AssemblerTest" && dotnet run -c Release -- --skipdetails && cd /d "%~dp0"

REM Rust cd /d "./rust/AssemblerTest" && cargo run --release && cd /d ../../
cd /d "%~dp0rust\AssemblerTest" && cargo run --release -- --skipdetails --standardtests && cd /d "%~dp0"
cd /d "%~dp0rust\AssemblerTest" && cargo run --release -- --skipdetails && cd /d "%~dp0"

REM Go cd /d "./go/AssemblerTest" && go run . && cd /d ../../
cd /d "%~dp0go\AssemblerTest" && go run . --skipdetails --standardtests && cd /d "%~dp0"
cd /d "%~dp0go\AssemblerTest" && go run . --skipdetails && cd /d "%~dp0"

REM Node.js cd /d "./node/AssemblerTest" && node index.js && cd /d ../../
cd /d "%~dp0node\AssemblerTest" && node index.js -- --skipdetails --standardtests && cd /d "%~dp0"
cd /d "%~dp0node\AssemblerTest" && node index.js -- --skipdetails && cd /d "%~dp0"

REM PHP cd /d "./php/AssemblerTest" && php index.php && cd /d ../../
cd /d "%~dp0php\AssemblerTest" && php index.php -- --skipdetails --standardtests && cd /d "%~dp0"
cd /d "%~dp0php\AssemblerTest" && php index.php -- --skipdetails && cd /d "%~dp0"

pause
