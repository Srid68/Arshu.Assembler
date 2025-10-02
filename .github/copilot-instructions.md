There are two engines, the normal engine and preprocess engine. 

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

After any refactor, change compile the program and get the output from the terminal and ensure the program can compile without errors.

Important: Follow the same structure/logic as the rust/csharp project strictly accross all other projects

Can you please be brief and no need to explain all the changes. Just fix the issues.

Follow the same naming conventions as the rust/csharp project strictly accross all other projects using idiomatic patterns in respective languages.

STRUCTURAL CONSISTENCY ACROSS PROJECTS IS CRUCIAL. DO NOT CHANGE THE STRUCTURE BE DIFFERENT BETWEEN PROJECTS

Do Not Use RegEx

