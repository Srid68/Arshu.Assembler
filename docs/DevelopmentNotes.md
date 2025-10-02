
# Development Notes

Developing with AI is empowering for tackling both known-unknown and unknown-unknown tasks, but it is challenging and painful to ensure the AI follows directions without extensive rework and to verify changes without deep understanding. Optimal or correct implementation cannot always be verified for these tasks due to my limited knowledge in other languages, but I can validate my requirements and ensure the AI can self-validate my requirements by asking it to implement validation code and print the results.

Using AI for building declarative programs may be optimal since declarative programs focus on what we need, not how we achieve it. Specifying both the input and output requirements, and using AI to confirm whether the need has been achieved, is a better use of AI tools. These tools excel at iteration and pattern matching, finding optimal solutions by convergence. Based on this assumption, I not only instructed the AI on my requirements but also used it in parallel to build various testing code to verify whether the requirements were satisfied.

## Verification Loops for AI Agents to Self-Verify

* For example, when I asked AI to generate the normal engine for HTML templates, I requested it to capture the implementation output and compare it with the input templates and my prompt requirements to check if it satisfies my needs.

* When I asked AI to implement the preprocess engine for HTML templates, I requested it to create a dump of the preprocess structure into the template analysis folder in JSON format, and then asked the AI to read that JSON and compare it with the template and implementation output to check if the implementation satisfies the requirements.

By iteratively following the above process—asking the AI to compare implementations in other languages, test outputs, and prompt requirements—I was able to re-implement my assembler in various languages, starting with statically typed languages, then dynamically typed languages (server-side), and finally in JavaScript (client-side).

## Getting Started from Scratch

I began by building a minimal API in C# and created the test AppSites, instructing the AI to load the templates as an AppSite_AppFile key in a dictionary. Then, I asked it to process the dictionary to merge the templates, generate the output, print the output, and also display it in the AppSite endpoint. When I noticed issues in the output, I asked the AI to run the program for a particular endpoint, print the output, and review the result so it could see that the implementation was incorrect. This forced the AI to iteratively correct its own mistakes. Only after some success with this did I start defining all my rules and began creating structure by establishing three different projects: Assembler, AssemblerTest (for logical testing and for the AI itself to verify), and AssemblerWeb (for visual verification).

Using the above iterative approach, I believe the assembler can be implemented in any language for which AI support is strong.

## Notes of AI Model

The main models used were GPT-4.1 and Claude Sonnet 4 equally, but when major code or logical issues arose, Claude Sonnet 4 was invariably able to solve them better than GPT-4.1. I also used Gork Code Fast 1 (Preview) for some requirements, and it was equivalent to GPT-4.1.

All AI model outputs must be carefully reviewed and context recreated to ensure the prompt requirements are met. I found Claude Sonnet 4 was generally better for solving coding issues, though at a higher cost.
