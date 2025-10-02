@echo off
REM Run performance tests for all languages

REM C#
cd /d "%~dp0csharp\AssemblerTest" && dotnet run -c Release && cd /d "%~dp0"

REM Rust
cd /d "%~dp0rust\AssemblerTest" && cargo run --release  && cd /d "%~dp0"

REM Go
cd /d "%~dp0go\AssemblerTest" && go run . && cd /d "%~dp0"

REM Node.js
cd /d "%~dp0node\AssemblerTest" && node index.js && cd /d "%~dp0"

REM PHP
cd /d "%~dp0php\AssemblerTest" && php index.php && cd /d "%~dp0"

pause
