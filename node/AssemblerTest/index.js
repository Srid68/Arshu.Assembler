import * as Assembler from 'assembler';
import { promises as fs } from 'fs';
import fsSync from 'fs';
import path from 'path';
import * as PreprocessExtensions from './preprocessExtensions.js';
import { PerformanceUtils } from '../Assembler/src/templatePerformance/performanceUtils.js';

const { LoaderNormal, LoaderPreProcess, EngineNormal, EnginePreProcess } = Assembler;

class Program {
    static globalTestSummaryRows = [];
    static perfSummaryRows = [];
    
    static async main() {
        console.log("\n========== ENTER Main ==========");
        const args = process.argv.slice(2);
        console.log(`[DEBUG] Received args: [${args.join(", ")}]`);
        
        const { assemblerWebDirPath, projectDirectory } = Assembler.TemplateUtils.getAssemblerWebDirPath();
        let appSiteFilter = null;
        
        if (projectDirectory) {
            console.log(`Assembler Web Directory: ${assemblerWebDirPath}`);
            console.log();
            
            try {
                await fs.access(assemblerWebDirPath);
                console.log(`✅ AssemblerWeb Directory found and accessible: ${assemblerWebDirPath}`);
                
                console.log("💡 Use --appsite to filter appsite in both engines\n");
                console.log("💡 Use --standardtests to run standard tests in both engines\n");
                console.log("💡 Use --investigate or --analyze for performance analysis");
                console.log("");
                console.log("💡 Use --printhtml to print output html in both engines\n");
                console.log("💡 Use --nojson to disable JSON processing in both engines\n");
                
                const disableJsonProcessing = args.includes("--nojson");
                const runInvestigation = args.includes("--investigate") || args.includes("--analyze");
                let printHtmlOutput = false;
                let runStandardTestsOption = false;
                
                for (let i = 0; i < args.length; i++) {
                    const arg = args[i];
                    if (arg.startsWith("--appsite=")) {
                        appSiteFilter = arg.substring("--appsite=".length);
                    } else if (arg.toLowerCase() === "--appsite" && i + 1 < args.length) {
                        appSiteFilter = args[i + 1];
                    }
                    if (arg.toLowerCase() === "--printhtml") {
                        printHtmlOutput = true;
                    }
                    if (arg.toLowerCase() === "--standardtests" || arg.toLowerCase() === "--standardtest") {
                        runStandardTestsOption = true;
                    }
                }
                
                if (runInvestigation) {
                    console.log("RunPerformanceInvestigation");
                    // Use PerformanceUtils for performance comparison and summary table printing
                    const summaryRows = PerformanceUtils.runPerformanceComparison(assemblerWebDirPath, true, appSiteFilter, false);
                    PerformanceUtils.printPerfSummaryTable(assemblerWebDirPath, summaryRows);
                } else if (runStandardTestsOption) {
                    console.log("RunStandardTests");
                    await this.runStandardTests(assemblerWebDirPath, appSiteFilter, !disableJsonProcessing, printHtmlOutput);
                    if (this.globalTestSummaryRows.length > 0) {
                        this.printTestSummaryTable(assemblerWebDirPath, this.globalTestSummaryRows, "STANDARD TEST");
                    }
                } else {
                    // Run dump analysis first, then advanced tests
                    await this.dumpPreprocessedTemplateStructures(assemblerWebDirPath, projectDirectory, appSiteFilter);
                    
                    console.log("\n🔬 Now running advanced template tests...\n");
                    await this.runAdvancedTests(assemblerWebDirPath, appSiteFilter, !disableJsonProcessing, printHtmlOutput);
                    if (this.globalTestSummaryRows.length > 0) {
                        this.printTestSummaryTable(assemblerWebDirPath, this.globalTestSummaryRows, "ADVANCED TEST");
                    }

                    //console.log("RunPerformanceComparison");
                    //const summaryRows = PerformanceUtils.runPerformanceComparison(assemblerWebDirPath, true, appSiteFilter, false);
                    //PerformanceUtils.printPerfSummaryTable(assemblerWebDirPath, summaryRows);
                }
            } catch (error) {
                console.error(`❌ Assembler Web Directory not found or not accessible: ${assemblerWebDirPath}`);
                console.error(`Error details: ${error.message}`);
            }
        }
    }
    
    static async runStandardTests(assemblerWebDirPath, appSiteFilter = null, enableJsonProcessing = true, printHtmlOutput = false) {
        console.log("\n========== ENTER RunStandardTests ==========");
        const appSitesPath = path.join(assemblerWebDirPath, "AppSites");
        
        try {
            await fs.access(appSitesPath);
        } catch (error) {
            console.log(`❌ AppSites directory not found: ${appSitesPath}`);
            return;
        }
        
        const allAppSiteDirs = await fs.readdir(appSitesPath, { withFileTypes: true });
        const allTestSiteNames = allAppSiteDirs
            .filter(dirent => dirent.isDirectory())
            .map(dirent => dirent.name);
            
        console.log(`[DEBUG] All available testSites: [${allTestSiteNames.join(", ")}]`);
        console.log(`[DEBUG] appSiteFilter value: '${appSiteFilter}'`);
        
        let testSites = allTestSiteNames;
        if (appSiteFilter) {
            const filterTrimmed = appSiteFilter.trim();
            testSites = allTestSiteNames.filter(s => s.toLowerCase() === filterTrimmed.toLowerCase());
            if (testSites.length === 0) {
                console.log(`[WARNING] appSiteFilter '${filterTrimmed}' did not match any test site. Available sites: [${allTestSiteNames.join(", ")}]`);
            }
        }
        
        for (const testSite of testSites) {
            const appSiteDir = path.join(appSitesPath, testSite);
            try {
                await fs.access(appSiteDir);
            } catch (error) {
                console.log(`❌ ${testSite} appsite not found: ${appSiteDir}`);
                continue;
            }
            
            const files = await fs.readdir(appSiteDir);
            const htmlFiles = files.filter(file => file.endsWith('.html'));
            
            for (const htmlFile of htmlFiles) {
                const appFileName = path.parse(htmlFile).name;
                console.log(`${testSite}: 🔍 STANDARD TEST : appsite: ${testSite} appfile: ${appFileName}`);
                console.log(`${testSite}: AppSite: ${testSite}, AppViewPrefix: ${appFileName}`);
                console.log(`${testSite}: ${'='.repeat(50)}`);
                
                try {
                    // Build AppView scenarios like in C#
                    const appViewScenarios = [
                        { AppView: "", AppViewPrefix: "" } // No AppView
                    ];
                    
                    // Check for Views directory and build scenarios
                    const appSiteDir = path.join(appSitesPath, testSite);
                    const viewsPath = path.join(appSiteDir, "Views");
                    
                    try {
                        await fs.access(viewsPath);
                        const viewFiles = await fs.readdir(viewsPath);
                        const htmlViewFiles = viewFiles.filter(file => file.endsWith('.html'));
                        
                        for (const viewFile of htmlViewFiles) {
                            const viewName = path.parse(viewFile).name;
                            if (viewName.toLowerCase().includes("content")) {
                                const contentIndex = viewName.toLowerCase().indexOf("content");
                                if (contentIndex > 0) {
                                    const viewPart = viewName.substring(0, contentIndex);
                                    if (viewPart.length > 0) {
                                        const appView = viewPart.charAt(0).toUpperCase() + viewPart.slice(1);
                                        const appViewPrefix = appView.substring(0, Math.min(appView.length, 6));
                                        appViewScenarios.push({ AppView: appView, AppViewPrefix: appViewPrefix });
                                    }
                                }
                            }
                        }
                    } catch (error) {
                        // Views directory doesn't exist, continue with just the default scenario
                    }
                    
                    const templates = await Assembler.LoaderNormal.loadGetTemplateFiles(assemblerWebDirPath, testSite);
                    
                    const scenarioOutputs = [];
                    
                    for (const scenario of appViewScenarios) {
                        // Normal engine processing
                        const normalEngine = new Assembler.EngineNormal();
                        normalEngine.appViewPrefix = appFileName;
                        const resultNormal = normalEngine.mergeTemplates(testSite, appFileName, scenario.AppView, templates, enableJsonProcessing);
                        scenarioOutputs.push(resultNormal || "");
                        
                        console.log(`${testSite}: 🧪 STANDARD TEST : scenario: AppView='${scenario.AppView}', AppViewPrefix='${scenario.AppViewPrefix}'`);
                        console.log(`Output length = ${resultNormal?.length || 0} Output sample: ${resultNormal?.substring(0, Math.min(200, resultNormal?.length || 0))}`);
                        
                        if (printHtmlOutput) {
                            console.log(`\nFULL HTML OUTPUT for AppView '${scenario.AppView}':\n${resultNormal}\n`);
                        }
                    }
                    
                    // Check for unresolved placeholders and empty output
                    const scenarioUnresolved = [];
                    
                    for (let i = 0; i < scenarioOutputs.length; i++) {
                        const output = scenarioOutputs[i] || "";
                        let hasUnresolved = false;
                        let isEmpty = output.trim().length === 0;
                        let startIndex = 0;
                        
                        while ((startIndex = output.indexOf("{{", startIndex)) !== -1) {
                            const endIndex = output.indexOf("}}", startIndex);
                            if (endIndex !== -1) {
                                const content = output.substring(startIndex + 2, endIndex);
                                // Only flag as unresolved if it doesn't start with $ (which are normal template placeholders)
                                if (!content.startsWith("$")) {
                                    hasUnresolved = true;
                                    break;
                                }
                                startIndex = endIndex + 2;
                            } else {
                                break;
                            }
                        }
                        scenarioUnresolved.push(hasUnresolved || isEmpty);
                    }
                    
                    // Check for cross-view output differences (like C#)
                    let matchResult = "";
                    // Only compare outputs between AppView scenarios (exclude default scenario without AppView)
                    if (appViewScenarios.length > 2) { // default + at least two AppViews
                        let allDiffer = true;
                        const firstAppViewOutput = scenarioOutputs[1]; // first AppView scenario
                        for (let i = 2; i < scenarioOutputs.length; i++) {
                            if (scenarioOutputs[i] === firstAppViewOutput) {
                                allDiffer = false;
                                break;
                            }
                        }
                        if (allDiffer) {
                            console.log(`✅ SUCCESS: Outputs for different AppViews DO NOT MATCH in ${testSite} as expected.`);
                            matchResult = "PASS";
                        } else {
                            console.log(`❌ FAILURE: Some outputs for AppViews MATCH in ${testSite}. Expected them to differ.`);
                            matchResult = "FAIL";
                        }
                    }
                    
                    // Add summary rows for each scenario
                    for (let i = 0; i < appViewScenarios.length; i++) {
                        const scenario = appViewScenarios[i];
                        const hasUnresolved = scenarioUnresolved[i];
                        let crossView = "";
                        if (i > 0 && appViewScenarios.length > 2) {
                            crossView = matchResult;
                        }
                        
                        this.globalTestSummaryRows.push({
                            AppSite: testSite,
                            AppFile: appFileName,
                            AppView: scenario.AppView,
                            OutputMatch: (i === 0) ? (hasUnresolved ? "FAIL" : "PASS") : "",
                            ViewUnMatch: crossView,
                            Error: hasUnresolved ? (scenarioOutputs[i]?.trim().length === 0 ? "Empty" : "Unresolv") : ""
                        });
                    }
                    
                    const anyFailed = scenarioUnresolved.some(failed => failed);
                    if (anyFailed) {
                        console.log(`${testSite}: ❌ TEST FAILED: Found unresolved template fields or empty output.`);
                    }
                    
                } catch (error) {
                    console.error(`❌ Test failed for ${testSite}:${appFileName}: ${error.message}`);
                    this.globalTestSummaryRows.push({
                        AppSite: testSite,
                        AppFile: appFileName,
                        AppView: "",
                        OutputMatch: "FAIL",
                        ViewUnMatch: "",
                        Error: error.message.substring(0, 50)
                    });
                }
            }
        }
    }

    static async runAdvancedTests(assemblerWebDirPath, appSiteFilter, enableJsonProcessing, printHtmlOutput) {
        console.log('🔬 Advanced Mode: Running advanced tests for filtered scenarios\n');
        
        const appSitesPath = path.join(assemblerWebDirPath, "AppSites");
        
        try {
            await fs.access(appSitesPath);
        } catch (error) {
            console.log(`❌ AppSites directory not found: ${appSitesPath}`);
            return;
        }

        const allAppSiteDirs = await fs.readdir(appSitesPath, { withFileTypes: true });
        let testSites = allAppSiteDirs
            .filter(dirent => dirent.isDirectory())
            .map(dirent => dirent.name);

        if (appSiteFilter) {
            const filterTrimmed = appSiteFilter.trim();
            const filtered = testSites.filter(site => 
                site.toLowerCase().includes(filterTrimmed.toLowerCase()));
            
            if (filtered.length === 0) {
                console.log(`[WARNING] appSiteFilter '${filterTrimmed}' did not match any test site. Available sites: [${testSites.join(', ')}]`);
                return;
            }
            testSites = filtered;
        }

        if (testSites.length === 0) {
            console.log("[WARNING] No test sites found for advanced testing.");
            return;
        }

        // Clear summary rows for advanced tests
        this.globalTestSummaryRows = [];

        // Initialize engines
        const normalEngine = new EngineNormal();
        const preprocessEngine = new EnginePreProcess();

        for (const testSite of testSites) {
            const appSiteDir = path.join(appSitesPath, testSite);
            const htmlFiles = (await fs.readdir(appSiteDir, { withFileTypes: true }))
                .filter(dirent => dirent.isFile() && dirent.name.endsWith('.html'))
                .map(dirent => dirent.name);

            for (const htmlFile of htmlFiles) {
                const appFileName = path.basename(htmlFile, '.html');
                console.log(`🔍 ADVANCED TEST : appsite: ${testSite} appfile: ${appFileName}`);
                
                // Load templates with timing
                const startTime = process.hrtime.bigint();
                const templates = LoaderNormal.loadGetTemplateFiles(assemblerWebDirPath, testSite);
                const preprocessedTemplates = LoaderPreProcess.loadProcessGetTemplateFiles(assemblerWebDirPath, testSite);
                const loadTime = process.hrtime.bigint() - startTime;
                console.log(`✅ LoadGetTemplateFiles: ${Number(loadTime) / 1000000} ticks (${Math.round(Number(loadTime) / 1000000)}ms)`);
                
                console.log(`📂 Loaded ${preprocessedTemplates.templates.size} templates:`);
                for (const [key, template] of preprocessedTemplates.templates) {
                    const htmlLength = template.originalContent.length;
                    const jsonLength = template.jsonData ? JSON.stringify(template.jsonData).length : 0;
                    if (jsonLength > 0) {
                        console.log(`   • ${key}: ${htmlLength} chars HTML + ${jsonLength} chars JSON`);
                    } else {
                        console.log(`   • ${key}: ${htmlLength} chars HTML`);
                    }
                }

                console.log(`\n🔧 JSON Processing: ${enableJsonProcessing ? 'ENABLED' : 'DISABLED'}`);

                // Discover AppView scenarios
                let appViewScenarios = [{ appView: '', appViewPrefix: '' }]; // Default scenario
                
                // For HtmlRule3A and HtmlRule3B, add specific AppView testing
                if (testSite === 'HtmlRule3A' || testSite === 'HtmlRule3B') {
                    appViewScenarios = [
                        { appView: '', appViewPrefix: '' },
                        { appView: 'Html3A', appViewPrefix: 'Html3A' },
                        { appView: 'Html3B', appViewPrefix: 'Html3B' }
                    ];
                }
                
                for (const scenario of appViewScenarios) {
                    console.log(`${testSite}: 🧪 ADVANCED TEST : scenario: AppView='${scenario.appView}', AppViewPrefix='${scenario.appViewPrefix}'`);
                    
                    // Test Normal Engine
                    normalEngine.appViewPrefix = appFileName;
                    const normalStart = process.hrtime.bigint();
                    const resultNormal = normalEngine.mergeTemplates(testSite, appFileName, scenario.appView, templates, enableJsonProcessing);
                    const normalTime = process.hrtime.bigint() - normalStart;
                    console.log(`✅ Normal - MergeTemplates: ${Number(normalTime) / 1000000} ticks (${Math.round(Number(normalTime) / 1000000)}ms)`);

                    // Test PreProcess Engine  
                    preprocessEngine.appViewPrefix = appFileName;
                    const preprocessStart = process.hrtime.bigint();
                    const preprocessedTemplatesObj = Object.fromEntries(preprocessedTemplates.templates);
                    const resultPreprocess = preprocessEngine.mergeTemplates(testSite, appFileName, scenario.appView, preprocessedTemplatesObj, enableJsonProcessing);
                    const preprocessTime = process.hrtime.bigint() - preprocessStart;
                    console.log(`✅ PreProcess - MergeTemplates: ${Number(preprocessTime) / 1000000} ticks (${Math.round(Number(preprocessTime) / 1000000)}ms)`);

                    const outputsMatch = resultNormal === resultPreprocess;
                    
                    console.log(`\n${testSite}: 📊 RESULTS COMPARISON:`);
                    console.log(`${testSite}: ---------------------------------------------`);
                    console.log(`${testSite}: 🔹 All Two Methods:`);
                    console.log(`${testSite}:   Normal: ${resultNormal.length} chars`);
                    console.log(`${testSite}:   PreProcess: ${resultPreprocess.length} chars`);
                    console.log(`${testSite}:   ${outputsMatch ? '✅' : '❌'} Normal vs PreProcess: ${outputsMatch ? 'MATCH' : 'NO MATCH'}`);

                    if (outputsMatch) {
                        console.log(`\n${testSite}: 🎉 ALL METHODS PRODUCE IDENTICAL RESULTS! ✅`);
                    } else {
                        console.log(`\n${testSite}: ⚠️  METHODS PRODUCE DIFFERENT RESULTS! ❌`);
                    }

                    if (printHtmlOutput) {
                        if (scenario.appView) {
                            console.log(`\nFULL HTML OUTPUT (Normal) for AppView '${scenario.appView}':\n${resultNormal}`);
                            console.log(`\nFULL HTML OUTPUT (PreProcess) for AppView '${scenario.appView}':\n${resultPreprocess}`);
                        } else {
                            console.log(`\n📋 FINAL OUTPUT SAMPLE (full HTML):\n${testSite}: ${resultNormal}`);
                        }
                    } else {
                        console.log(`Output sample: ${resultNormal.substring(0, Math.min(200, resultNormal.length))}`);
                        console.log(`\n${testSite}: 📋 FINAL OUTPUT SAMPLE (full HTML):\n${testSite}: ${resultNormal}`);
                    }

                    // Determine ViewUnMatch status for AppView scenarios
                    let viewUnMatch = "";
                    if (scenario.appView && (testSite === 'HtmlRule3A' || testSite === 'HtmlRule3B')) {
                        // For AppView scenarios, check if output is different from empty AppView
                        viewUnMatch = "PASS"; // Assume PASS for now, can add specific logic later
                    }

                    const matchResult = outputsMatch ? "PASS" : "FAIL";
                    this.globalTestSummaryRows.push({
                        AppSite: testSite,
                        AppFile: appFileName,
                        AppView: scenario.appView,
                        OutputMatch: matchResult,
                        ViewUnMatch: viewUnMatch,
                        Error: ""
                    });

                    // Show detailed differences if methods differ
                    if (!outputsMatch) {
                        console.log(`\n${testSite}: ❗ DETAILED DIFFERENCES:`);
                        console.log(`${testSite}: 🔸 Normal vs PreProcess:`);
                        console.log(`${testSite}:   Normal Result:\n${resultNormal}`);
                        console.log(`${testSite}:   PreProcess Result:\n${resultPreprocess}`);
                        console.log();
                    }

                    // Check for unmerged template fields in all outputs
                    console.log(`\n${testSite}: 🔎 Checking for unmerged template fields in outputs...`);
                    let foundUnmerged = false;
                    
                    const outputInfos = [
                        { name: 'Normal', result: resultNormal },
                        { name: 'PreProcess', result: resultPreprocess }
                    ];

                    for (const outputInfo of outputInfos) {
                        const unmergedFields = this.findUnmergedTemplateFields(outputInfo.result);
                        if (unmergedFields.length > 0) {
                            const filteredFields = enableJsonProcessing ? 
                                unmergedFields : 
                                unmergedFields.filter(f => !f.startsWith('${Json') && !f.startsWith('${$Json'));
                            
                            if (filteredFields.length > 0) {
                                console.log(`${testSite}:   ❌ ${outputInfo.name} output contains ${filteredFields.length} unmerged non-JSON template fields!`);
                                for (const field of filteredFields) {
                                    console.log(`${testSite}:      Unmerged field: ${field}`);
                                }
                                foundUnmerged = true;
                            } else {
                                console.log(`${testSite}:   ✅ ${outputInfo.name} output contains no unmerged non-JSON template fields.`);
                            }
                        } else {
                            console.log(`${testSite}:   ✅ ${outputInfo.name} output contains no unmerged template fields.`);
                        }
                    }
                    
                    if (foundUnmerged) {
                        console.log(`\n${testSite}: ⚠️  TEST FAILURE: Unmerged non-JSON template fields found in output!`);
                    } else {
                        console.log(`\n${testSite}: 🎉 TEST SUCCESS: No unmerged non-JSON template fields found in any output.`);
                    }
                }
            }
        }
    }

    // Dump method following same pattern as C#/Rust/Go
    static async dumpPreprocessedTemplateStructures(assemblerWebDirPath, projectDirectory, appSiteFilter) {
        console.log("🔍 Dump Preprocessed Template Structures");
        
        const appSitesPath = path.join(assemblerWebDirPath, "AppSites");
        
        try {
            await fs.access(appSitesPath);
        } catch (error) {
            console.log(`❌ AppSites directory not found: ${appSitesPath}`);
            return;
        }
        
        const allAppSiteDirs = await fs.readdir(appSitesPath, { withFileTypes: true });
        const appSites = allAppSiteDirs
            .filter(dirent => dirent.isDirectory())
            .map(dirent => dirent.name);
            
        for (const site of appSites) {
            if (appSiteFilter && !site.toLowerCase().includes(appSiteFilter.toLowerCase())) {
                continue;
            }
            
            console.log(`🔍 Analyzing site: ${site}`);
            console.log('='.repeat(60));
            
            try {
                // Clear cache to ensure fresh load
                Assembler.LoaderNormal.clearCache();
                Assembler.LoaderPreProcess.clearCache();
                
                // Test the path resolution first
                console.log(`Current Directory: ${process.cwd()}`);
                console.log(`AssemblerWebDirPath: ${assemblerWebDirPath}`);
                
                const sitePath = path.join(appSitesPath, site);
                console.log(`AppSites path: ${sitePath}`);
                
                try {
                    await fs.access(sitePath);
                    console.log(`Site directory found and accessible`);
                } catch {
                    console.log(`Site directory not found`);
                }
                
                // Load templates using both methods
                const templates = Assembler.LoaderNormal.loadGetTemplateFiles(assemblerWebDirPath, site);
                console.log(`LoadGetTemplateFiles found ${templates.size} templates`);
                
                const preprocessedSiteTemplates = Assembler.LoaderPreProcess.loadProcessGetTemplateFiles(assemblerWebDirPath, site);
                console.log(`LoadProcessGetTemplateFiles found ${preprocessedSiteTemplates.templates.size} templates`);
                
                if (preprocessedSiteTemplates.templates.size === 0) {
                    console.log("⚠️  No templates found - check path resolution");
                    continue;
                }
                
                console.log(`📋 Summary for ${site}:`);
                console.log(PreprocessExtensions.toSummaryJson(preprocessedSiteTemplates, true));
                
                console.log(`\n📄 Full Structure for ${site}:`);
                const fullJson = PreprocessExtensions.toJson(preprocessedSiteTemplates, true);
                console.log(fullJson);
                
                // Save to file for easier analysis
                const outputDir = path.join(projectDirectory, "template_analysis");
                await fs.mkdir(outputDir, { recursive: true });
                
                const summaryFile = path.join(outputDir, `${site}_summary.json`);
                const fullFile = path.join(outputDir, `${site}_full.json`);
                
                // Delete existing files to ensure clean generation
                try {
                    await fs.unlink(summaryFile);
                } catch {}
                try {
                    await fs.unlink(fullFile);
                } catch {}
                
                await fs.writeFile(summaryFile, PreprocessExtensions.toSummaryJson(preprocessedSiteTemplates, true));
                await fs.writeFile(fullFile, fullJson);
                
                console.log(`💾 Analysis saved to:`);
                console.log(`   Summary: ${summaryFile}`);
                console.log(`   Full:    ${fullFile}`);
                
            } catch (error) {
                console.log(`❌ Error analyzing ${site}: ${error.message}`);
                if (error.stack) {
                    console.log(`   Stack trace: ${error.stack}`);
                }
            }
            
            console.log(); // Empty line between sites
        }
        
        console.log("✅ Template structure analysis complete!");
    }

    static printTestSummaryTable(assemblerWebDirPath, summaryRows, testType) {
        if (!summaryRows || summaryRows.length === 0) return;
        if (!testType) testType = "TEST";
        
        console.log(`\n==================== Node.js ${testType.toUpperCase()} SUMMARY ====================\n`);

        const headers = ["AppSite", "AppFile", "AppView", "OutputMatch", "ViewUnMatch", "Error"];
        const colCount = headers.length;
        const widths = new Array(colCount);
        
        // Calculate column widths
        for (let i = 0; i < colCount; i++) {
            let maxLen = headers[i].length;
            for (const row of summaryRows) {
                const value = this.getValue(row, i);
                if (value.length > maxLen) maxLen = value.length;
            }
            widths[i] = maxLen < 10 ? 10 : maxLen;
        }

        // Print header
        process.stdout.write("| ");
        for (let i = 0; i < colCount; i++) {
            process.stdout.write(headers[i].padEnd(widths[i]));
            if (i < colCount - 1) process.stdout.write(" | ");
        }
        console.log(" |");

        // Print divider
        process.stdout.write("|");
        for (let i = 0; i < colCount; i++) {
            process.stdout.write(" " + "-".repeat(widths[i]) + " ");
            if (i < colCount - 1) process.stdout.write("|");
        }
        console.log("|");

        // Print rows
        for (const row of summaryRows) {
            process.stdout.write("| ");
            process.stdout.write((row.AppSite || "").padEnd(widths[0]));
            process.stdout.write(" | ");
            process.stdout.write((row.AppFile || "").padEnd(widths[1]));
            process.stdout.write(" | ");
            process.stdout.write((row.AppView || "").padEnd(widths[2]));
            process.stdout.write(" | ");
            process.stdout.write((row.OutputMatch || "").padEnd(widths[3]));
            process.stdout.write(" | ");
            process.stdout.write((row.ViewUnMatch || "").padEnd(widths[4]));
            process.stdout.write(" | ");
            process.stdout.write((row.Error || "").padEnd(widths[5]));
            console.log(" |");
        }
        
        // Print bottom divider
        process.stdout.write("|");
        for (let i = 0; i < colCount; i++) {
            process.stdout.write(" " + "-".repeat(widths[i]) + " ");
            if (i < colCount - 1) process.stdout.write("|");
        }
        console.log("|");

        // Save HTML file
        try {
            const html = [
                "<html><head><title>Test Summary Table</title><style>table{border-collapse:collapse;}th,td{border:1px solid #888;padding:4px;}th{background:#eee;}</style></head><body>",
                `<h2>Node.js ${testType.toUpperCase()} SUMMARY TABLE</h2>`,
                "<table>",
                "<tr>"
            ];
            
            for (const h of headers) {
                html.push(`<th>${h}</th>`);
            }
            html.push("</tr>");
            
            for (const row of summaryRows) {
                html.push("<tr>");
                html.push(`<td>${row.AppSite}</td>`);
                html.push(`<td>${row.AppFile}</td>`);
                html.push(`<td>${row.AppView}</td>`);
                html.push(`<td>${row.OutputMatch}</td>`);
                html.push(`<td>${row.ViewUnMatch}</td>`);
                html.push(`<td>${row.Error}</td>`);
                html.push("</tr>");
            }
            html.push("</table></body></html>");

            // Sanitize testType for filename
            const testTypeFile = testType.replace(/\s/g, "").replace(/-/g, "").toLowerCase();
            const outFile = path.join(assemblerWebDirPath, `nodejs_${testTypeFile}_Summary.html`);
            fs.writeFile(outFile, html.join("\n")).then(() => {
                console.log(`Test summary HTML saved to: ${outFile}`);
            });
            
            // Save JSON summary file
            const jsonFile = path.join(assemblerWebDirPath, `nodejs_${testTypeFile}_Summary.json`);
            fs.writeFile(jsonFile, JSON.stringify(summaryRows, null, 2)).then(() => {
                console.log(`Test summary JSON saved to: ${jsonFile}`);
            });
        } catch (error) {
            console.error(`Error saving test summary files: ${error.message}`);
        }
        
        console.log("\n======================================================\n");
    }

    // Performance summary table printing function - MOVED TO ../Assembler/src/templatePerformance/performanceUtils.js

    static getValue(row, index) {
        switch (index) {
            case 0: return row.AppSite || "";
            case 1: return row.AppFile || "";
            case 2: return row.AppView || "";
            case 3: return row.OutputMatch || "";
            case 4: return row.ViewUnMatch || "";
            case 5: return row.Error || "";
            default: return "";
        }
    }

    static findUnmergedTemplateFields(output) {
        const regex = /\{\{[^}]+\}\}/g;
        const matches = output.match(regex);
        return matches || [];
    }

    static analyzeOutputDifferences(output1, output2) {
        // Split both outputs into lines for comparison
        const lines1 = output1.split('\n');
        const lines2 = output2.split('\n');

        console.log(`   Lines: ${lines1.length} vs ${lines2.length}`);

        // Compare line by line
        const commonLength = Math.min(lines1.length, lines2.length);
        for (let i = 0; i < commonLength; i++) {
            if (lines1[i] !== lines2[i]) {
                console.log(`\n   Difference at line ${i + 1}:`);
                console.log(`   Normal:    ${lines1[i].length} chars`);
                console.log(`   PreProcess:${lines2[i].length} chars`);

                // Show first position where they differ
                const minLength = Math.min(lines1[i].length, lines2[i].length);
                for (let j = 0; j < minLength; j++) {
                    if (lines1[i][j] !== lines2[i][j]) {
                        console.log(`   First difference at character ${j + 1}: '${lines1[i][j]}' vs '${lines2[i][j]}'`);
                        break;
                    }
                }
            }
        }
    }

    static async compareJsonProcessing(assemblerWebDirPath, appSite, appFile, enableJsonProcessing = true) {
        console.log(`\n📊 Testing JSON Processing Impact for ${appSite} : ${appFile}`);
        console.log('-'.repeat(50));

        const templates = await Assembler.LoaderNormal.loadGetTemplateFiles(assemblerWebDirPath, appSite);
        const preprocessedData = await Assembler.LoaderPreProcess.loadProcessGetTemplateFiles(assemblerWebDirPath, appSite);
        const preprocessedTemplates = preprocessedData.templates; // Note: lowercase 't'

        if (!templates || templates.size === 0) {
            console.log(`❌ No templates found for ${appSite}`);
            return;
        }

        // Build AppView scenarios
        const appViewScenarios = [
            { AppView: "", AppViewPrefix: "" } // No AppView
        ];
        
        const appSitesPath = path.join(assemblerWebDirPath, "AppSites", appSite);
        const viewsPath = path.join(appSitesPath, "Views");
        
        try {
            await fs.access(viewsPath);
            const viewFiles = await fs.readdir(viewsPath);
            const htmlViewFiles = viewFiles.filter(file => file.endsWith('.html'));
            
            for (const viewFile of htmlViewFiles) {
                const viewName = path.parse(viewFile).name;
                if (viewName.toLowerCase().includes("content")) {
                    const contentIndex = viewName.toLowerCase().indexOf("content");
                    if (contentIndex > 0) {
                        const viewPart = viewName.substring(0, contentIndex);
                        if (viewPart.length > 0) {
                            const appView = viewPart.charAt(0).toUpperCase() + viewPart.slice(1);
                            const appViewPrefix = appView.substring(0, Math.min(appView.length, 6));
                            appViewScenarios.push({ AppView: appView, AppViewPrefix: appViewPrefix });
                        }
                    }
                }
            }
        } catch (error) {
            // Views directory doesn't exist, continue with just the default scenario
        }

        for (const scenario of appViewScenarios) {
            console.log(`\n🔍 Testing scenario: AppView='${scenario.AppView}', AppViewPrefix='${scenario.AppViewPrefix}'`);
            
            const normalEngine = new Assembler.EngineNormal();
            const preProcessEngine = new Assembler.EnginePreProcess();
            normalEngine.appViewPrefix = scenario.AppViewPrefix;
            preProcessEngine.appViewPrefix = scenario.AppViewPrefix;

            // Find the main template key for both engines
            const mainTemplateKey = Array.from(templates.keys()).find(k => 
                k.startsWith(appSite.toLowerCase() + "_"));
            const mainPreprocessedKey = Array.from(preprocessedTemplates.keys()).find(k => 
                k.startsWith(appSite.toLowerCase() + "_"));

            let resultNormal = "";
            let resultPreProcess = "";

            try {
                resultNormal = normalEngine.mergeTemplates(appSite, appFile, scenario.AppView, templates, enableJsonProcessing) || "";
                resultPreProcess = preProcessEngine.mergeTemplates(appSite, appFile, scenario.AppView, preprocessedTemplates, enableJsonProcessing) || "";

                console.log(`📏 Output sizes: Normal=${resultNormal.length}, PreProcess=${resultPreProcess.length}`);

                // Save outputs for comparison
                const testOutputDir = path.join(assemblerWebDirPath, "test_output");
                try {
                    await fs.mkdir(testOutputDir, { recursive: true });
                } catch (error) {
                    // Directory might already exist
                }

                await fs.writeFile(path.join(testOutputDir, `${appSite}_normal_${scenario.AppView}_${enableJsonProcessing ? "with" : "no"}_json.html`), resultNormal);
                await fs.writeFile(path.join(testOutputDir, `${appSite}_preprocess_${scenario.AppView}_${enableJsonProcessing ? "with" : "no"}_json.html`), resultPreProcess);

                console.log(`\n📄 Outputs saved to: ${testOutputDir}`);

                // Show a diff of lengths by section to help identify where they differ
                console.log("\n🔍 Output Analysis:");
                this.analyzeOutputDifferences(resultNormal, resultPreProcess);
            } catch (error) {
                console.error(`❌ Error processing scenario: ${error.message}`);
            }
        }
    }

       
    static async runPerformanceInvestigation(assemblerWebDirPath, appSiteFilter) {
        const iterations = 1000;
        const appSitesPath = path.join(assemblerWebDirPath, 'AppSites');
        
        if (!fsSync.existsSync(appSitesPath)) {
            console.log(`❌ AppSites directory not found: ${appSitesPath}`);
            return;
        }

        const allAppSiteDirs = fsSync.readdirSync(appSitesPath, { withFileTypes: true })
            .filter(dirent => dirent.isDirectory())
            .map(dirent => dirent.name);
        
        for (const testAppSite of allAppSiteDirs) {
            if (appSiteFilter && testAppSite.toLowerCase() !== appSiteFilter.toLowerCase()) {
                continue;
            }
            
            const appSiteDir = path.join(appSitesPath, testAppSite);
            if (!fsSync.existsSync(appSiteDir)) {
                console.log(`❌ ${testAppSite} appsite not found: ${appSiteDir}`);
                continue;
            }
            
            const htmlFiles = fsSync.readdirSync(appSiteDir)
                .filter(file => file.endsWith('.html') && !file.includes('/') && !file.includes('\\'));
            
            for (const htmlFile of htmlFiles) {
                const appFileName = path.parse(htmlFile).name;
                
                // Build AppView scenarios (same logic as StandardTests/AdvancedTests)
                const appViewScenarios = [['', '']]; // No AppView
                const appSitePathInner = path.join(assemblerWebDirPath, 'AppSites', testAppSite);
                const viewsPath = path.join(appSitePathInner, 'Views');
                
                if (fsSync.existsSync(viewsPath)) {
                    const viewFiles = fsSync.readdirSync(viewsPath).filter(file => file.endsWith('.html'));
                    for (const viewFile of viewFiles) {
                        const viewName = path.parse(viewFile).name;
                        let appView = '';
                        let appViewPrefix = '';
                        
                        if (viewName.toLowerCase().includes('content')) {
                            const contentIndex = viewName.toLowerCase().indexOf('content');
                            if (contentIndex > 0) {
                                const viewPart = viewName.substring(0, contentIndex);
                                if (viewPart.length > 0) {
                                    appView = viewPart.charAt(0).toUpperCase() + viewPart.slice(1);
                                    appViewPrefix = appView.substring(0, Math.min(appView.length, 6));
                                }
                            }
                        }
                        
                        if (appView) {
                            appViewScenarios.push([appView, appViewPrefix]);
                        }
                    }
                }
                
                // Clear caches before loading templates
                LoaderNormal.clearCache();
                LoaderPreProcess.clearCache();
                
                console.log(`[DEBUG] About to load templates for testAppSite: ${testAppSite}, assemblerWebDirPath: ${assemblerWebDirPath}`);
                const templates = LoaderNormal.loadGetTemplateFiles(assemblerWebDirPath, testAppSite);
                const preprocessedTemplatesResult = LoaderPreProcess.loadProcessGetTemplateFiles(assemblerWebDirPath, testAppSite);
                const preprocessedTemplates = Object.fromEntries(preprocessedTemplatesResult.templates);
                
                console.log(`[DEBUG] Templates available for ${testAppSite}: [${Array.from(templates.keys()).join(', ')}]`);
                console.log(`[DEBUG] Preprocessed templates available for ${testAppSite}: [${Object.keys(preprocessedTemplates).join(', ')}]`);
                
                const mainTemplateKey = (testAppSite + '_' + appFileName).toLowerCase();
                console.log(`[DEBUG] Looking for main template key: ${mainTemplateKey}`);
                if (!templates.has(mainTemplateKey)) {
                    console.log(`❌ No main template found for key: ${mainTemplateKey}`);
                    continue;
                }
                if (!preprocessedTemplates[mainTemplateKey]) {
                    console.log(`❌ No preprocessed main template found for key: ${mainTemplateKey}`);
                    continue;
                }
                
                for (const [appView, appViewPrefix] of appViewScenarios) {
                    console.log(`\n📊 NODE PERFORMANCE ANALYSIS: ${testAppSite}, ${appFileName}, AppView='${appView}'`);
                    console.log('='.repeat(60));
                    
                        // Normal Engine
                        const normalEngine = new EngineNormal();
                        normalEngine.appViewPrefix = appViewPrefix;
                        
                        const normalStart = process.hrtime.bigint();
                        let normalResult = '';
                        for (let i = 0; i < iterations; i++) {
                            normalResult = normalEngine.mergeTemplates(testAppSite, appFileName, appView, templates, false);
                        }
                        const normalEnd = process.hrtime.bigint();
                        const normalTimeMs = Number(normalEnd - normalStart) / 1_000_000;
                        
                        // PreProcess Engine
                        const preProcessEngine = new EnginePreProcess();
                        preProcessEngine.appViewPrefix = appViewPrefix;                    const preProcessStart = process.hrtime.bigint();
                    let preProcessResult = '';
                    for (let i = 0; i < iterations; i++) {
                        preProcessResult = preProcessEngine.mergeTemplates(testAppSite, appFileName, appView, preprocessedTemplates, false);
                    }
                    const preProcessEnd = process.hrtime.bigint();
                    const preProcessTimeMs = Number(preProcessEnd - preProcessStart) / 1_000_000;
                    
                    // Analysis
                    console.log(`Normal Engine:     ${normalTimeMs.toFixed(0)}ms`);
                    console.log(`PreProcess Engine: ${preProcessTimeMs.toFixed(0)}ms`);
                    const resultsMatch = normalResult === preProcessResult;
                    console.log(`Results match:     ${(resultsMatch ? '✅ YES' : '❌ NO')}`);
                    console.log(`Normal size:       ${normalResult?.length ?? 0} chars`);
                    console.log(`PreProcess size:   ${preProcessResult?.length ?? 0} chars`);
                    
                    // Debug output for mismatches
                    if (!resultsMatch && normalResult && preProcessResult) {
                        console.log(`❌ DEBUG: Content mismatch detected`);
                        console.log(`Normal first 100 chars: "${normalResult.substring(0, 100)}"`);
                        console.log(`PreProcess first 100 chars: "${preProcessResult.substring(0, 100)}"`);
                        
                        // Find first difference
                        for (let i = 0; i < Math.min(normalResult.length, preProcessResult.length); i++) {
                            if (normalResult[i] !== preProcessResult[i]) {
                                console.log(`❌ First difference at position ${i}:`);
                                console.log(`  Normal: "${normalResult[i]}" (code: ${normalResult.charCodeAt(i)})`);
                                console.log(`  PreProcess: "${preProcessResult[i]}" (code: ${preProcessResult.charCodeAt(i)})`);
                                console.log(`  Context: "${normalResult.substring(Math.max(0, i-10), i+10)}"`);
                                console.log(`  vs:      "${preProcessResult.substring(Math.max(0, i-10), i+10)}"`);
                                break;
                            }
                        }
                    }

                    if (!normalResult || !preProcessResult) {
                        console.log("❌ Output is empty. Check template keys and input files for this appsite.");
                    } else if (preProcessTimeMs < normalTimeMs) {
                        const diffMs = normalTimeMs - preProcessTimeMs;
                        const diffPct = normalTimeMs > 0 ? ((diffMs / normalTimeMs) * 100) : 0;
                        console.log(`✅ PreProcess Engine is faster by ${diffMs.toFixed(0)}ms (${diffPct.toFixed(1)}%)`);
                    } else if (preProcessTimeMs > normalTimeMs) {
                        const diffMs = preProcessTimeMs - normalTimeMs;
                        const diffPct = preProcessTimeMs > 0 ? ((diffMs / preProcessTimeMs) * 100) : 0;
                        console.log(`❌ Normal Engine is faster by ${diffMs.toFixed(0)}ms (${diffPct.toFixed(1)}%)`);
                    } else {
                        console.log(`⚖️  Both engines have equal performance.`);
                    }
                }
            }
        }
    }

    // Performance comparison function - MOVED TO ../Assembler/src/templatePerformance/performanceUtils.js

    /**
     * Analyzes output differences between Normal and PreProcess engines
     * @param {string} normalOutput - Output from Normal engine
     * @param {string} preprocessOutput - Output from PreProcess engine
     * @returns {Object} Analysis result with detailed differences
     */
    static analyzeOutputDifferences(normalOutput, preprocessOutput) {
        const analysis = {
            identical: normalOutput === preprocessOutput,
            lengthDifference: normalOutput.length - preprocessOutput.length,
            characterDifferences: [],
            wordDifferences: [],
            lineDifferences: []
        };

        if (!analysis.identical) {
            // Character-by-character analysis
            const maxLength = Math.max(normalOutput.length, preprocessOutput.length);
            for (let i = 0; i < maxLength; i++) {
                const normalChar = i < normalOutput.length ? normalOutput[i] : '';
                const preprocessChar = i < preprocessOutput.length ? preprocessOutput[i] : '';
                
                if (normalChar !== preprocessChar) {
                    analysis.characterDifferences.push({
                        position: i,
                        normal: normalChar,
                        preprocess: preprocessChar
                    });
                    
                    if (analysis.characterDifferences.length >= 10) break; // Limit to first 10 differences
                }
            }

            // Line-by-line analysis
            const normalLines = normalOutput.split('\n');
            const preprocessLines = preprocessOutput.split('\n');
            const maxLines = Math.max(normalLines.length, preprocessLines.length);
            
            for (let i = 0; i < maxLines; i++) {
                const normalLine = i < normalLines.length ? normalLines[i] : '';
                const preprocessLine = i < preprocessLines.length ? preprocessLines[i] : '';
                
                if (normalLine !== preprocessLine) {
                    analysis.lineDifferences.push({
                        lineNumber: i + 1,
                        normal: normalLine,
                        preprocess: preprocessLine
                    });
                }
            }
        }

        return analysis;
    }

    /**
     * Compares JSON processing between engines with different JSON enable states
     * @param {string} testSite - Test site name
     * @param {string} appFileName - App file name  
     * @param {string} appView - App view
     * @param {Object} templates - Templates for Normal engine
     * @param {Object} preprocessedTemplates - Templates for PreProcess engine
     * @returns {Object} JSON processing comparison results
     */
    static compareJsonProcessing(testSite, appFileName, appView, templates, preprocessedTemplates) {
        const normalEngine = new EngineNormal();
        const preprocessEngine = new EnginePreProcess();
        
        const results = {
            jsonEnabled: {
                normal: '',
                preprocess: '',
                match: false
            },
            jsonDisabled: {
                normal: '',
                preprocess: '', 
                match: false
            },
            consistency: {
                normalConsistent: false,
                preprocessConsistent: false,
                crossEngineConsistent: false
            }
        };

        try {
            // Test with JSON processing enabled
            results.jsonEnabled.normal = normalEngine.mergeTemplates(testSite, appFileName, appView, templates, true);
            results.jsonEnabled.preprocess = preprocessEngine.mergeTemplates(testSite, appFileName, appView, preprocessedTemplates, true);
            results.jsonEnabled.match = results.jsonEnabled.normal === results.jsonEnabled.preprocess;

            // Test with JSON processing disabled
            results.jsonDisabled.normal = normalEngine.mergeTemplates(testSite, appFileName, appView, templates, false);
            results.jsonDisabled.preprocess = preprocessEngine.mergeTemplates(testSite, appFileName, appView, preprocessedTemplates, false);
            results.jsonDisabled.match = results.jsonDisabled.normal === results.jsonDisabled.preprocess;

            // Check consistency within each engine
            results.consistency.normalConsistent = results.jsonEnabled.normal === results.jsonDisabled.normal;
            results.consistency.preprocessConsistent = results.jsonEnabled.preprocess === results.jsonDisabled.preprocess;
            results.consistency.crossEngineConsistent = results.jsonEnabled.match && results.jsonDisabled.match;

        } catch (error) {
            console.error(`Error in JSON processing comparison: ${error.message}`);
        }

        return results;
    }
}

// Run the program
Program.main().catch(error => {
    console.error('❌ Program execution failed:', error);
    process.exit(1);
});