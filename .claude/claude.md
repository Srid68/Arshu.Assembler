# INSTRUCTIONS

Follow the same naming conventions as the rust/csharp project strictly accross all other projects using idiomatic patterns in respective languages.

STRUCTURAL CONSISTENCY ACROSS PROJECTS IN DIFFERENT LANGS IS CRUCIAL. DO NOT CHANGE THE STRUCTURE BE DIFFERENT BETWEEN PROJECTS

Important: Follow the same structure/logic as the rust/csharp project strictly accross all other projects

There are two engines, the normal engine and preprocess engine. 

After any refactor, change compile the program and get the output from the terminal and ensure the program can compile without errors.

Do Not Use RegEx, Linq and prefer to do Manual Json Serialization

Use Explicit Namespaces and Plain class names without Namespace Prefix

All Servers has to be started with --skipIdleTracking option

After every change build only and fix any compile errors, do not test unless specificaally instructured to test

Normal Loader

* The loader does the loading
Normal engine 
* Does both parsing and merging. 

PreProcess Loader role:

* Parse templates and structure
* Parse JSON into data structures
* Should NOT do any JSON merging/processing
PreProcess Engine's role:
* PreProcess Engine: Use the parsed data to merge templates
* All JSON merging should happen in the engine, not the loader

Testing for single appsite with html output, use the following commands for respective langs
 
cd /d "./csharp/AssemblerTest" && dotnet run -c Release -- --skipdetails --printhtml --advancedtests --appsite HtmlRule1A && cd /d ../../
cd /d "./rust/AssemblerTest" && cargo run --release -- --skipdetails --printhtml --advancedtests --appsite HtmlRule1A && cd /d ../../
cd /d "./go/AssemblerTest" && go run . --skipdetails --printhtml --advancedtests --appsite HtmlRule1A && cd /d ../../
cd /d "./node/AssemblerTest" && node index.js --skipdetails --printhtml --advancedtests --appsite HtmlRule1A && cd /d ../../
cd /d "./php/AssemblerTest" && php index.php --skipdetails --printhtml --advancedtests --appsite HtmlRule1A && cd /d ../../

Just take note that if you single -appsite option without --advancedtests option, then the performance testing will also be triggered slowing your testing

Advanced and Default Testing to check all appsites, use the following commands for respective langs

cd /d "./csharp/AssemblerTest" && dotnet run -c Release -- --skipdetails
cd /d "./rust/AssemblerTest" && cargo run --release -- --skipdetails
cd /d "./go/AssemblerTest" && go run . --skipdetails
cd /d "./node/AssemblerTest" && node index.js --skipdetails
cd /d "./php/AssemblerTest" && php index.php --skipdetails

after every test move to workspace directory cd /d ../../

Procedure for Testing and Fixing Rule failer

Compare with Html Output with other languages
Compare Structure Dump in template_analysis with other languages
Complare logic with implementation in other languages

Procedure for Fixing Appsites like Index which are very large with many components

Create a Backup of the main component in another languages.
Trunctate the main component to include only one component in another languages
Test as above and if pass add components one by one to find the issue component
Once issue component found include that alone and try to find the issue and fix it
