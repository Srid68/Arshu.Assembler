@echo off
REM Run performance tests for all languages

REM cd "./csharp/AssemblerTest" && dotnet run -c Release && cd ../../
REM cd "./csharp/AssemblerTest" && dotnet run -c Release --skipdetails --appsite Index && cd ../../
REM cd "./csharp/AssemblerTest" && dotnet run -c Release --skipdetails --advancedtests && cd ../../
REM cd "./csharp/AssemblerTest" && dotnet run -c Release --skipdetails --standardtests && cd ../../
rm /s/q "%~dp0csharp\AssemblerTest\template_analysis"
rm /s/q "%~dp0csharp\AssemblerWeb\template_analysis"
cd "%~dp0csharp\AssemblerTest" && dotnet run -c Release -- --skipdetails --standardtests && cd "%~dp0"
cd "%~dp0csharp\AssemblerTest" && dotnet run -c Release -- --skipdetails && cd "%~dp0"
rem pause

REM cd "./rust/AssemblerTest" && cargo run --release && cd ../../
REM cd "./rust/AssemblerTest" && cargo run --release -- --skipdetails --appsite Index && cd ../../
REM cd "./rust/AssemblerTest" && cargo run --release -- --skipdetails --advancedtests && cd ../../
REM cd "./rust/AssemblerTest" && cargo run --release -- --skipdetails --standardtests && cd ../../
rm /s/q "%~dp0rust\AssemblerTest\template_analysis"
rm /s/q "%~dp0rust\AssemblerWeb\template_analysis"
cd "%~dp0rust\AssemblerTest" && cargo run --release -- --skipdetails --standardtests && cd "%~dp0"
cd "%~dp0rust\AssemblerTest" && cargo run --release -- --skipdetails && cd "%~dp0"

REM cd "./go/AssemblerTest" && go run . && cd ../../
REM cd "./go/AssemblerTest" && go run . --skipdetails --appsite Index && cd ../../
REM cd "./go/AssemblerTest" && go run . --skipdetails --advancedtests && cd ../../
REM cd "./go/AssemblerTest" && go run . --skipdetails --standardtests && cd ../../
rm /s/q "%~dp0go\AssemblerTest\template_analysis"
rm /s/q "%~dp0go\AssemblerWeb\template_analysis"
cd "%~dp0go\AssemblerTest" && go run . --skipdetails --standardtests && cd "%~dp0"
cd "%~dp0go\AssemblerTest" && go run . --skipdetails && cd "%~dp0"

REM cd "./node/AssemblerTest" && node index.js && cd ../../
REM cd "./node/AssemblerTest" && node index.js --skipdetails --appsite Index && cd ../../
REM cd "./node/AssemblerTest" && node index.js --skipdetails --advancedtests && cd ../../
REM cd "./node/AssemblerTest" && node index.js --skipdetails --standardtests && cd ../../
rm /s/q "%~dp0node\AssemblerTest\template_analysis"
rm /s/q "%~dp0node\AssemblerWeb\template_analysis"
cd "%~dp0node\AssemblerTest" && node index.js -- --skipdetails --standardtests && cd "%~dp0"
cd "%~dp0node\AssemblerTest" && node index.js -- --skipdetails && cd "%~dp0"

REM cd "./php/AssemblerTest" && php index.php && cd ../../
REM cd "./php/AssemblerTest" && php index.php --skipdetails --appsite Index && cd ../../
REM cd "./php/AssemblerTest" && php index.php --skipdetails --advancedtests && cd ../../
REM cd "./php/AssemblerTest" && php index.php --skipdetails --standardtests && cd ../../
rm /s/q "%~dp0php\AssemblerTest\template_analysis"
rm /s/q "%~dp0php\AssemblerWeb\template_analysis"
cd "%~dp0php\AssemblerTest" && php index.php -- --skipdetails --standardtests && cd "%~dp0"
cd "%~dp0php\AssemblerTest" && php index.php -- --skipdetails && cd "%~dp0"

pause
