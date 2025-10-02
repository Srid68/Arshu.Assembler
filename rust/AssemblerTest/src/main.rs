mod preprocess_extensions;
use preprocess_extensions::PreprocessExtensions;


use std::env;
use std::path::Path;
use std::collections::HashMap;
use std::fs;
use assembler::template_loader::loader_normal::LoaderNormal;
use assembler::template_loader::loader_preprocess::LoaderPreProcess;
use assembler::template_engine::engine_normal::EngineNormal;
use assembler::template_engine::engine_preprocess::EnginePreProcess;
use assembler::template_performance::PerformanceUtils;


#[derive(Debug, Clone, serde::Serialize)]
struct TestSummaryRow {
    app_site: String,
    app_file: String,
    app_view: String,
    normal_preprocess: String,
    cross_view_unmatch: String,
    error: String,
}

fn main() 
{
    let args: Vec<String> = env::args().collect();
    let mut app_site_filter: Option<String> = None;
    let mut run_standard_tests_option = false;
    let mut run_investigation = false;
    let mut print_html_output = false;
    let mut enable_json_processing = true;

    let mut i = 0;
    while i < args.len() {
        let arg = &args[i];
        if arg.starts_with("--appsite=") {
            app_site_filter = Some(arg[10..].to_string());
        } else if arg == "--appsite" && i + 1 < args.len() {
            app_site_filter = Some(args[i + 1].clone());
            i += 1;
        }
        if arg == "--standardtests" {
            run_standard_tests_option = true;
        }
        if arg == "--investigate" {
            run_investigation = true;
        }
        if arg == "--printhtml" {
            print_html_output = true;
        }
        if arg == "--nojson" {
            enable_json_processing = false;
        }
        i += 1;
    }

    // Use TemplateUtils::get_assembler_web_dir_path to get the AssemblerWeb directory
    let (assembler_web_dir_path, project_dir) = assembler::template_common::template_utils::TemplateUtils::get_assembler_web_dir_path();
    println!("Current Directory: {:?}", project_dir);
    println!("Assembler Web Directory: {:?}", assembler_web_dir_path);
    if assembler_web_dir_path.exists() {
        let web_dir_str = assembler_web_dir_path.to_string_lossy();
        if run_investigation {
            println!("🔬 Investigation Mode: Running performance analysis\n");
            // Use PerformanceUtils for performance comparison and summary table printing
            let summary_rows = PerformanceUtils::run_performance_comparison(&web_dir_str, app_site_filter.as_deref(), false, enable_json_processing);
            PerformanceUtils::print_perf_summary_table(&web_dir_str, &summary_rows);
        } else if run_standard_tests_option {
            println!("📊 Standard Mode: Running template engine comparison tests\n");
            run_standard_tests(&web_dir_str, app_site_filter.as_deref(), enable_json_processing, print_html_output);
        } else {
            dump_preprocessed_template_structures(&web_dir_str, &project_dir.to_string_lossy(), app_site_filter.as_deref());
            run_advanced_tests(&web_dir_str, &project_dir.to_string_lossy(), app_site_filter.as_deref(), enable_json_processing, print_html_output);
            println!("⏱ Performance Mode: Running performance comparison\n");
            let summary_rows = PerformanceUtils::run_performance_comparison(&web_dir_str, app_site_filter.as_deref(), false, enable_json_processing);
            PerformanceUtils::print_perf_summary_table(&web_dir_str, &summary_rows);
        }
    } else {
        println!("AssemblerWeb directory not found.");
    }
}

// Update run_standard_tests to discover AppView scenarios and compare outputs
fn run_standard_tests(assembler_web_dir: &str, app_site_filter: Option<&str>, enable_json_processing: bool, print_html_output: bool) 
{
    println!("✅ JSON Processing {} for both engines\n", if enable_json_processing {"ENABLED"} else {"DISABLED"});
    let app_sites_path = std::path::Path::new(assembler_web_dir).join("AppSites");
    if !app_sites_path.exists() {
        println!("❌ AppSites directory not found: {:?}", app_sites_path);
        return;
    }
    let mut test_sites = Vec::new();
    for entry in std::fs::read_dir(&app_sites_path).unwrap() {
        let entry = entry.unwrap();
        if entry.path().is_dir() {
            let site_name = entry.file_name().to_string_lossy().to_string();
            if let Some(filter) = app_site_filter {
                if !site_name.eq_ignore_ascii_case(filter) {
                    continue;
                }
            }
            test_sites.push(site_name);
        }
    }
    
    let mut global_test_summary_rows: Vec<TestSummaryRow> = Vec::new();
    
    for test_site in test_sites {
        let app_site_dir = app_sites_path.join(&test_site);
        let html_files = std::fs::read_dir(&app_site_dir).unwrap()
            .filter_map(|e| e.ok())
            .filter(|e| e.path().extension().map(|ext| ext == "html").unwrap_or(false))
            .collect::<Vec<_>>();
        for html_file in html_files {
            let app_file_name = html_file.path().file_stem().unwrap().to_string_lossy().to_string();
            println!("{}: STANDARD TEST : appsite: {} appfile: {}", test_site, test_site, app_file_name);
            println!("{}: AppSite: {}, AppViewPrefix: Html3A", test_site, test_site);
            println!("{}: {}", test_site, "=".repeat(50));
            let templates = LoaderNormal::load_get_template_files(assembler_web_dir, &test_site);
            // Discover AppView scenarios
            let mut app_view_scenarios = vec!["".to_string()]; // No AppView
            let views_path = app_site_dir.join("Views");
            if views_path.exists() {
                let view_files = std::fs::read_dir(&views_path).unwrap()
                    .filter_map(|e| e.ok())
                    .filter(|e| e.path().extension().map(|ext| ext == "html").unwrap_or(false))
                    .collect::<Vec<_>>();
                for view_file in view_files {
                    let view_name = view_file.path().file_stem().unwrap().to_string_lossy().to_string();
                    let mut app_view = String::new();
                    let mut _app_view_prefix = String::new();
                    if view_name.to_lowercase().contains("content") {
                        let content_index = view_name.to_lowercase().find("content").unwrap();
                        if content_index > 0 {
                            let view_part = &view_name[..content_index];
                            if !view_part.is_empty() {
                                app_view = view_part[..1].to_uppercase() + &view_part[1..];
                                _app_view_prefix = app_view[..std::cmp::min(app_view.len(), 6)].to_string();
                            }
                        }
                    }
                    if !app_view.is_empty() {
                        app_view_scenarios.push(app_view);
                    }
                }
            }
            let mut scenario_outputs = Vec::new();
            let mut scenario_unresolved: Vec<bool> = Vec::new();
            let mut scenario_match_results: Vec<bool> = Vec::new();
            // Load preprocessed templates for PreProcess engine
            let preprocessed_site_templates = LoaderPreProcess::load_process_get_template_files(assembler_web_dir, &test_site);
            for app_view in app_view_scenarios.iter() {
                let normal_engine = EngineNormal::new(app_file_name.clone());
                let preprocess_engine = EnginePreProcess::new(app_file_name.clone());
                let (result_normal, result_preprocess, outputs_match) = compare_engines_for_scenario(
                    &test_site, &app_file_name, app_view,
                    &normal_engine, &preprocess_engine,
                    &mut templates.clone(), &preprocessed_site_templates.templates,
                    enable_json_processing, assembler_web_dir
                );
                scenario_outputs.push(result_normal.clone());
                scenario_match_results.push(outputs_match);
                println!("Output sample: {}", &result_normal[..std::cmp::min(200, result_normal.len())]);
                if print_html_output {
                    println!("\nFULL HTML OUTPUT (Normal) for AppView '{}':\n{}\n", app_view, result_normal);
                    println!("\nFULL HTML OUTPUT (PreProcess) for AppView '{}':\n{}\n", app_view, result_preprocess);
                }
                // Scan both outputs for unresolved template placeholders and check for empty output
                let mut unresolved = Vec::new();
                let is_empty = result_normal.trim().is_empty() || result_preprocess.trim().is_empty();
                let scan_outputs = [result_normal.as_str(), result_preprocess.as_str()];
                for output in scan_outputs.iter() {
                    let mut search_pos = 0;
                    let chars = output.chars().collect::<Vec<_>>();
                    while search_pos < chars.len() {
                        if search_pos + 1 < chars.len() && chars[search_pos] == '{' && chars[search_pos + 1] == '{' {
                            let special = if search_pos + 2 < chars.len() {
                                chars[search_pos + 2]
                            } else {
                                ' '
                            };
                            if special == '#' || special == '@' || special == '$' || special == '/' {
                                search_pos += 2;
                                continue;
                            }
                            let mut close_pos = search_pos + 2;
                            while close_pos + 1 < chars.len() {
                                if chars[close_pos] == '}' && chars[close_pos + 1] == '}' {
                                    let placeholder: String = chars[search_pos..=close_pos + 1].iter().collect();
                                    unresolved.push(placeholder);
                                    search_pos = close_pos + 2;
                                    break;
                                }
                                close_pos += 1;
                            }
                        } else {
                            search_pos += 1;
                        }
                    }
                }
                if !unresolved.is_empty() || is_empty {
                    if is_empty {
                        println!("❌ Empty output found for AppView '{}'", app_view);
                    }
                    if !unresolved.is_empty() {
                        println!("❌ Unresolved template placeholders found in output for AppView '{}':", app_view);
                        for placeholder in &unresolved {
                            println!("   Unresolved: {}", placeholder);
                        }
                    }
                    scenario_unresolved.push(true);
                } else {
                    println!("✅ No unresolved template placeholders found in output for AppView '{}'.", app_view);
                    scenario_unresolved.push(false);
                }
            }
            // Compare outputs for cross-view
            let mut match_result = String::new();
            if app_view_scenarios.len() > 2 {
                let mut all_differ = true;
                let first_app_view_output = &scenario_outputs[1];
                for output in scenario_outputs.iter().skip(2) {
                    if output == first_app_view_output {
                        all_differ = false;
                        break;
                    }
                }
                if all_differ {
                    println!("✅ SUCCESS: Outputs for different AppViews DO NOT MATCH in {} as expected.", test_site);
                    match_result = "PASS".to_string();
                } else {
                    println!("❌ FAILURE: Some outputs for AppViews MATCH in {}. Expected them to differ.", test_site);
                    match_result = "FAIL".to_string();
                }
            }
            // Add summary rows for each scenario
            for (i, app_view) in app_view_scenarios.iter().enumerate() {
                let cross_view = if i > 0 && app_view_scenarios.len() > 2 { match_result.clone() } else { String::new() };
                let has_unresolved = scenario_unresolved.get(i).copied().unwrap_or(false);
                global_test_summary_rows.push(TestSummaryRow {
                    app_site: test_site.clone(),
                    app_file: app_file_name.clone(),
                    app_view: app_view.clone(),
                    normal_preprocess: if i == 0 { if has_unresolved { "FAIL".to_string() } else { "PASS".to_string() } } else { String::new() },
                    cross_view_unmatch: cross_view,
                    error: if has_unresolved { 
                        if scenario_outputs.get(i).map(|s| s.trim().is_empty()).unwrap_or(false) { 
                            "Empty".to_string() 
                        } else { 
                            "Unresolve".to_string() 
                        } 
                    } else { 
                        String::new() 
                    },
                });
            }
        }
    }
    
    // Print formatted summary table
    print_test_summary_table(&global_test_summary_rows, "STANDARD TEST");
    // Save summary to file with rust prefix and test type in web assembler dir
    let output_dir = assembler_web_dir;
    if let Err(e) = std::fs::create_dir_all(&output_dir) {
        println!("❌ Error creating output directory: {}", e);
        return;
    }
    let summary_json_file = format!("{}/rust_standardtest_Summary.json", output_dir);
    let summary_html_file = format!("{}/rust_standardtest_Summary.html", output_dir);
    let summary_json = serde_json::to_string_pretty(&global_test_summary_rows).unwrap();
    if let Err(e) = fs::write(&summary_json_file, summary_json) {
        println!("❌ Error writing summary JSON file: {}", e);
    } else {
        println!("Test summary JSON saved to: {}", summary_json_file);
    }
    // Generate HTML summary table
    let mut html = String::new();
    html.push_str("<html><head><title>Test Summary Table</title><style>table{border-collapse:collapse;}th,td{border:1px solid #888;padding:4px;}th{background:#eee;}</style></head><body>\n");
    html.push_str(&format!("<h2>RUST {} SUMMARY TABLE</h2>\n", "STANDARD TEST"));
    html.push_str("<table>\n");
    html.push_str("<tr><th>AppSite</th><th>AppFile</th><th>AppView</th><th>OutputMatch</th><th>ViewUnMatch</th><th>Error</th></tr>\n");
    for row in &global_test_summary_rows {
        html.push_str(&format!(
            "<tr><td>{}</td><td>{}</td><td>{}</td><td>{}</td><td>{}</td><td>{}</td></tr>\n",
            row.app_site,
            row.app_file,
            row.app_view,
            row.normal_preprocess,
            row.cross_view_unmatch,
            row.error
        ));
    }
    html.push_str("</table></body></html>\n");
    if let Err(e) = fs::write(&summary_html_file, html) {
        println!("❌ Error writing summary HTML file: {}", e);
    } else {
        println!("Test summary HTML saved to: {}", summary_html_file);
    }
}

fn run_advanced_tests(assembler_web_dir: &str, _project_directory: &str, app_site_filter: Option<&str>, enable_json_processing: bool, print_html_output: bool) 
{
    println!("🔬 Advanced Mode: Running advanced tests for filtered scenarios\n");
            
    let app_sites_path = std::path::Path::new(assembler_web_dir).join("AppSites");
    if !app_sites_path.exists() {
        println!("❌ AppSites directory not found: {:?}", app_sites_path);
        return;
    }
    let mut test_sites = Vec::new();
    for entry in std::fs::read_dir(&app_sites_path).unwrap() {
        let entry = entry.unwrap();
        if entry.path().is_dir() {
            let site_name = entry.file_name().to_string_lossy().to_string();
            if let Some(filter) = app_site_filter {
                if site_name.eq_ignore_ascii_case(filter) {
                    test_sites.push(site_name);
                }
            } else {
                test_sites.push(site_name);
            }
        }
    }
    let mut global_test_summary_rows: Vec<TestSummaryRow> = Vec::new();
    for test_site in test_sites {
        let app_site_dir = app_sites_path.join(&test_site);
        let html_files = std::fs::read_dir(&app_site_dir).unwrap()
            .filter_map(|e| e.ok())
            .filter(|e| e.path().extension().map(|ext| ext == "html").unwrap_or(false))
            .collect::<Vec<_>>();
        for html_file in html_files {
            let app_file_name = html_file.path().file_stem().unwrap().to_string_lossy().to_string();
            println!("🔍 ADVANCED TEST : appsite: {} appfile: {}", test_site, app_file_name);
            
            // Load templates with timing output
            let now = std::time::Instant::now();
            let templates = LoaderNormal::load_get_template_files(assembler_web_dir, &test_site);
            let load_time = now.elapsed();
            println!("✅ LoadGetTemplateFiles: {} ticks ({}ms)", load_time.as_nanos() / 100, load_time.as_millis());
            
            let preprocessed_site_templates = LoaderPreProcess::load_process_get_template_files(assembler_web_dir, &test_site);
            
            println!("📂 Loaded {} templates:", templates.len());
            let mut sorted_templates: Vec<_> = templates.iter().collect();
            sorted_templates.sort_by(|a, b| a.0.cmp(b.0));
            for (key, (html, json_opt)) in sorted_templates {
                let html_length = html.len();
                let json_info = if let Some(json) = json_opt {
                    format!(" + {} chars JSON", json.len())
                } else {
                    String::new()
                };
                println!("   • {}: {} chars HTML{}", key, html_length, json_info);
            }
            println!();
            
            println!("🔧 JSON Processing: {}", if enable_json_processing { "ENABLED" } else { "DISABLED" });
            // Discover AppView scenarios
            let mut app_view_scenarios = vec!["".to_string()]; // No AppView
            let views_path = app_site_dir.join("Views");
            if views_path.exists() {
                let view_files = std::fs::read_dir(&views_path).unwrap()
                    .filter_map(|e| e.ok())
                    .filter(|e| e.path().extension().map(|ext| ext == "html").unwrap_or(false))
                    .collect::<Vec<_>>();
                for view_file in view_files {
                    let view_name = view_file.path().file_stem().unwrap().to_string_lossy().to_string();
                    let mut app_view = String::new();
                    if view_name.to_lowercase().contains("content") {
                        let content_index = view_name.to_lowercase().find("content").unwrap();
                        if content_index > 0 {
                            let view_part = &view_name[..content_index];
                            if !view_part.is_empty() {
                                app_view = view_part[..1].to_uppercase() + &view_part[1..];
                            }
                        }
                    }
                    if !app_view.is_empty() {
                        app_view_scenarios.push(app_view);
                    }
                }
            }
            
            let mut scenario_results = Vec::new();
            for app_view in app_view_scenarios.iter() {
                // Use empty AppViewPrefix for default scenario (when app_view is empty), otherwise use app_file_name
                let app_view_prefix = if app_view.is_empty() { "" } else { &app_file_name };
                println!("{}: 🧪 ADVANCED TEST : scenario: AppView='{}', AppViewPrefix='{}'", test_site, app_view, app_view_prefix);
                
                let normal_engine = EngineNormal::new(app_view_prefix.to_string());
                let preprocess_engine = EnginePreProcess::new(app_view_prefix.to_string());
                
                // Time the Normal engine
                let now = std::time::Instant::now();
                let result_normal = normal_engine.merge_templates(&test_site, &app_file_name, Some(app_view), &mut templates.clone(), enable_json_processing);
                let normal_time = now.elapsed();
                println!("✅ Normal - MergeTemplates: {} ticks ({}ms)", normal_time.as_nanos() / 100, normal_time.as_millis());
                
                // Time the PreProcess engine
                let now = std::time::Instant::now();
                let result_preprocess = preprocess_engine.merge_templates(&test_site, &app_file_name, Some(app_view), &preprocessed_site_templates.templates, enable_json_processing);
                let preprocess_time = now.elapsed();
                println!("✅ PreProcess - MergeTemplates: {} ticks ({}ms)", preprocess_time.as_nanos() / 100, preprocess_time.as_millis());
                println!();

                // Store for cross-AppView comparison
                scenario_results.push((app_view.clone(), result_normal.clone(), result_preprocess.clone()));

                if print_html_output {
                    println!("\n📋 FULL HTML OUTPUT (Normal):\n{}", result_normal);
                    println!("\n📋 FULL HTML OUTPUT (PreProcess):\n{}", result_preprocess);
                }

                // Compare results
                println!("{}: 📊 RESULTS COMPARISON:", test_site);
                println!("{}: {}", test_site, "-".repeat(45));

                println!("{}: 🔹 All Two Methods:", test_site);
                println!("{}:   Normal: {} chars", test_site, result_normal.len());
                println!("{}:   PreProcess: {} chars", test_site, result_preprocess.len());

                // Check if results match
                let outputs_match = result_normal == result_preprocess;
                if outputs_match {
                    println!("{}:   ✅ Normal vs PreProcess: MATCH", test_site);
                } else {
                    println!("{}:   ❌ Normal vs PreProcess: NO MATCH", test_site);
                }

                let match_result = if outputs_match { "PASS" } else { "FAIL" };
                global_test_summary_rows.push(TestSummaryRow {
                    app_site: test_site.clone(),
                    app_file: app_file_name.clone(),
                    app_view: app_view.clone(),
                    normal_preprocess: match_result.to_string(),
                    cross_view_unmatch: "".to_string(),
                    error: "".to_string(),
                });

                if outputs_match {
                    println!("\n{}: 🎉 ALL METHODS PRODUCE IDENTICAL RESULTS! ✅", test_site);
                } else {
                    println!("\n{}: ⚠️  METHODS PRODUCE DIFFERENT RESULTS! ❌", test_site);
                }

                // Show final processed outputs
                if !result_normal.is_empty() {
                    println!("\n{}: 📋 FINAL OUTPUT SAMPLE (full HTML):", test_site);
                    println!("{}", result_normal);
                }

                // Show detailed differences if methods differ
                if !outputs_match {
                    println!("\n{}: ❗ DETAILED DIFFERENCES:", test_site);
                    println!("{}: 🔸 Normal vs PreProcess:", test_site);
                    println!("{}:   Normal Result:\n{}", test_site, result_normal);
                    println!("{}:   PreProcess Result:\n{}", test_site, result_preprocess);
                    println!();
                }

                // Check for unmerged template fields in all outputs
                println!("\n{}: 🔎 Checking for unmerged template fields in outputs...", test_site);
                let mut found_unmerged = false;
                
                for (name, output) in [("Normal", &result_normal), ("PreProcess", &result_preprocess)] {
                    let mut unmerged_fields = Vec::new();
                    
                    // Find all ${field} patterns using indexOf
                    let mut start_index = 0;
                    while let Some(pos) = output[start_index..].find("${") {
                        let absolute_pos = start_index + pos;
                        if let Some(end_pos) = output[absolute_pos..].find("}") {
                            let end_absolute = absolute_pos + end_pos;
                            let field = &output[absolute_pos..=end_absolute];
                            unmerged_fields.push(field.to_string());
                            start_index = end_absolute + 1;
                        } else {
                            break;
                        }
                    }
                    
                    if !unmerged_fields.is_empty() {
                        // If JSON processing is disabled, skip reporting unmerged JSON fields
                        let filtered_fields: Vec<_> = if enable_json_processing {
                            unmerged_fields
                        } else {
                            unmerged_fields.into_iter()
                                .filter(|f| !f.starts_with("${Json") && !f.starts_with("${$Json"))
                                .collect()
                        };
                        
                        if !filtered_fields.is_empty() {
                            println!("{}:   ❌ {} output contains {} unmerged non-JSON template fields!", test_site, name, filtered_fields.len());
                            for field in &filtered_fields {
                                println!("{}:      Unmerged field: {}", test_site, field);
                            }
                            found_unmerged = true;
                        } else {
                            println!("{}:   ✅ {} output contains no unmerged non-JSON template fields.", test_site, name);
                        }
                    } else {
                        println!("{}:   ✅ {} output contains no unmerged template fields.", test_site, name);
                    }
                }
                
                if found_unmerged {
                    println!("\n{}: ⚠️  TEST FAILURE: Unmerged non-JSON template fields found in output!", test_site);
                } else {
                    println!("\n{}: 🎉 TEST SUCCESS: No unmerged non-JSON template fields found in any output.", test_site);
                }
            }
            
            // Compare outputs from different AppViews (cross-scenario)
            // Only compare AppView scenarios (exclude empty AppView scenario)
            let app_view_results: Vec<_> = scenario_results.iter().filter(|(app_view, _, _)| !app_view.is_empty()).collect();
            if app_view_results.len() > 1 {
                println!("\n🔬 Cross-AppView Output Comparison:");
                let mut all_app_views_differ = true;
                let first_app_view_normal = &app_view_results[0].1;
                let first_app_view_preprocess = &app_view_results[0].2;

                for i in 1..app_view_results.len() {
                    let cross_view_match = if &app_view_results[i].1 == first_app_view_normal && &app_view_results[i].2 == first_app_view_preprocess {
                        println!("❌ FAILURE: Outputs for AppView '{}' and AppView '{}' MATCH. Expected them to differ.", app_view_results[0].0, app_view_results[i].0);
                        all_app_views_differ = false;
                        "FAIL".to_string()
                    } else {
                        println!("✅ SUCCESS: Outputs for AppView '{}' and AppView '{}' DO NOT MATCH as expected.", app_view_results[0].0, app_view_results[i].0);
                        "PASS".to_string()
                    };

                    // Find and update the corresponding row in global_test_summary_rows
                    let target_app_view = &app_view_results[i].0;
                    if let Some(row_to_update) = global_test_summary_rows.iter_mut()
                        .rev()
                        .find(|r| r.app_site == test_site && r.app_file == app_file_name && r.app_view == *target_app_view) {
                        row_to_update.cross_view_unmatch = cross_view_match;
                    }
                }

                // Also set the first AppView result
                let first_target_app_view = &app_view_results[0].0;
                if let Some(first_row_to_update) = global_test_summary_rows.iter_mut()
                    .rev()
                    .find(|r| r.app_site == test_site && r.app_file == app_file_name && r.app_view == *first_target_app_view) {
                    first_row_to_update.cross_view_unmatch = if all_app_views_differ { "PASS".to_string() } else { "FAIL".to_string() };
                }

                if all_app_views_differ {
                    println!("🎉 All AppView outputs are different as expected.");
                } else {
                    println!("❌ Some AppView outputs match when they should differ.");
                }
            }
        }
    }
    // Print formatted summary table
    print_test_summary_table(&global_test_summary_rows, "ADVANCED TEST");
    // Save summary to file with rust prefix and test type
    let output_dir = assembler_web_dir;
    // Ensure output directory exists
    if let Err(e) = std::fs::create_dir_all(&output_dir) {
        println!("❌ Error creating output directory: {}", e);
        return;
    }
    let summary_json_file = format!("{}/rust_advancedtest_Summary.json", output_dir);
    let summary_html_file = format!("{}/rust_advancedtest_Summary.html", output_dir);
    let summary_json = serde_json::to_string_pretty(&global_test_summary_rows).unwrap();
    if let Err(e) = fs::write(&summary_json_file, summary_json) {
        println!("❌ Error writing summary JSON file: {}", e);
    } else {
        println!("Test summary JSON saved to: {}", summary_json_file);
    }

    // Generate HTML summary table
    let mut html = String::new();
    html.push_str("<html><head><title>Test Summary Table</title><style>table{border-collapse:collapse;}th,td{border:1px solid #888;padding:4px;}th{background:#eee;}</style></head><body>\n");
    html.push_str(&format!("<h2>RUST {} SUMMARY TABLE</h2>\n", "ADVANCED TEST"));
    html.push_str("<table>\n");
    html.push_str("<tr><th>AppSite</th><th>AppFile</th><th>AppView</th><th>OutputMatch</th><th>ViewUnMatch</th><th>Error</th></tr>\n");
    for row in &global_test_summary_rows {
        html.push_str(&format!(
            "<tr><td>{}</td><td>{}</td><td>{}</td><td>{}</td><td>{}</td><td>{}</td></tr>\n",
            row.app_site,
            row.app_file,
            row.app_view,
            row.normal_preprocess,
            row.cross_view_unmatch,
            row.error
        ));
    }
    html.push_str("</table></body></html>\n");
    if let Err(e) = fs::write(&summary_html_file, html) {
        println!("❌ Error writing summary HTML file: {}", e);
    } else {
        println!("Test summary HTML saved to: {}", summary_html_file);
    }
}

fn dump_preprocessed_template_structures(assembler_web_dir_path: &str, project_directory: &str, app_site_filter: Option<&str>) {
    let app_sites_path = format!("{}/AppSites", assembler_web_dir_path);
    if !Path::new(&app_sites_path).exists() {
        println!("❌ AppSites directory not found: {}", app_sites_path);
        return;
    }

    let app_sites_dir = match fs::read_dir(&app_sites_path) {
        Ok(entries) => entries.filter_map(|entry| {
            entry.ok().and_then(|e| {
                if e.file_type().ok()?.is_dir() {
                    e.file_name().to_str().map(|s| s.to_string())
                } else {
                    None
                }
            })
        }).collect::<Vec<String>>(),
        Err(_) => {
            println!("❌ Error reading AppSites directory");
            return;
        }
    };

    for site in app_sites_dir {
        if let Some(filter) = app_site_filter {
            if !site.eq_ignore_ascii_case(filter) {
                continue;
            }
        }

        println!("🔍 Analyzing site: {}", site);
        println!("{}", "=".repeat(60));

        let site_path = format!("{}/{}", app_sites_path, site);
        println!("Current Directory: {:?}", std::env::current_dir());
        println!("AssemblerWebDirPath: {}", assembler_web_dir_path);
        println!("AppSites path: {}", site_path);
        println!("AppSites exists: {}", Path::new(&site_path).exists());

        if Path::new(&site_path).exists() {
            println!("Site directory found and accessible");
        }

        // Load templates using both methods
        let templates = LoaderNormal::load_get_template_files(assembler_web_dir_path, &site);
        println!("LoadGetTemplateFiles found {} templates", templates.len());

        let preprocessed_templates = LoaderPreProcess::load_process_get_template_files(assembler_web_dir_path, &site);
        println!("LoadProcessGetTemplateFiles found {} templates", preprocessed_templates.templates.len());

        if preprocessed_templates.templates.is_empty() {
            println!("⚠️  No templates found - check path resolution");
            continue;
        }

        // Print summary
        println!("📋 Summary for {}:", site);
        println!("{}", preprocessed_templates.to_summary_json(true));

        println!("\n📄 Full Structure for {}:", site);
        println!("{}", preprocessed_templates.to_json(true));

        // Save to file for easier analysis
        let output_dir = format!("{}/template_analysis", project_directory);
        if let Err(e) = fs::create_dir_all(&output_dir) {
            println!("❌ Error creating output directory: {}", e);
            continue;
        }

        let summary_file = format!("{}/{}_summary.json", output_dir, site);
        let full_file = format!("{}/{}_full.json", output_dir, site);

        // Delete existing files to ensure clean generation
        if std::path::Path::new(&summary_file).exists() {
            let _ = fs::remove_file(&summary_file);
        }
        if std::path::Path::new(&full_file).exists() {
            let _ = fs::remove_file(&full_file);
        }

        let summary_json = preprocessed_templates.to_summary_json(true);
        let full_json = preprocessed_templates.to_json(true);

        if let Err(e) = fs::write(&summary_file, summary_json) {
            println!("❌ Error writing summary file: {}", e);
        } else {
            println!("💾 Analysis saved to:");
            println!("   Summary: {}", summary_file);
        }

        if let Err(e) = fs::write(&full_file, full_json) {
            println!("❌ Error writing full file: {}", e);
        } else {
            println!("   Full:    {}", full_file);
        }

        println!(); // Empty line between sites
    }

    println!("✅ Template structure analysis complete!");
}

// Performance investigation function similar to C# RunPerformanceInvestigation
#[allow(dead_code)]
fn run_performance_investigation(assembler_web_dir: &str, app_site_filter: Option<&str>, _enable_json_processing: bool) 
{
    use std::time::Instant;
    
    let iterations = 1000;
    let app_sites_path = std::path::Path::new(assembler_web_dir).join("AppSites");
    if !app_sites_path.exists() {
        println!("❌ AppSites directory not found: {:?}", app_sites_path);
        return;
    }
    
    for entry in std::fs::read_dir(&app_sites_path).unwrap() {
        let entry = entry.unwrap();
        if !entry.path().is_dir() { continue; }
        
        let test_app_site = entry.file_name().to_string_lossy().to_string();
        if let Some(filter) = app_site_filter {
            if !test_app_site.eq_ignore_ascii_case(filter) {
                continue;
            }
        }
        
        let app_site_dir = entry.path();
        if !app_site_dir.exists() {
            println!("❌ {} appsite not found: {:?}", test_app_site, app_site_dir);
            continue;
        }
        
        let html_files: Vec<_> = std::fs::read_dir(&app_site_dir)
            .unwrap()
            .filter_map(|entry| {
                let entry = entry.ok()?;
                let path = entry.path();
                if path.extension()?.to_str()? == "html" {
                    Some(path)
                } else {
                    None
                }
            })
            .collect();
            
        for html_file_path in html_files {
            let app_file_name = match html_file_path.file_stem() {
                Some(stem) => stem.to_string_lossy().to_string(),
                None => continue,
            };
            
            // Build AppView scenarios (same logic as standard tests)
            let mut app_view_scenarios = vec![(String::new(), String::new())]; // No AppView
            
            let views_path = app_site_dir.join("Views");
            if views_path.exists() {
                for view_entry in std::fs::read_dir(&views_path).unwrap() {
                    let view_entry = view_entry.unwrap();
                    if let Some(view_name) = view_entry.path().file_stem() {
                        let view_name = view_name.to_string_lossy().to_lowercase();
                        if view_name.contains("content") {
                            if let Some(content_index) = view_name.find("content") {
                                if content_index > 0 {
                                    let view_part = &view_name[..content_index];
                                    if !view_part.is_empty() {
                                        let app_view = format!("{}{}", 
                                            view_part.chars().next().unwrap().to_uppercase(),
                                            &view_part[1..]
                                        );
                                        let app_view_prefix = app_view.chars().take(6).collect::<String>();
                                        app_view_scenarios.push((app_view, app_view_prefix));
                                    }
                                }
                            }
                        }
                    }
                }
            }
            
            let mut templates = LoaderNormal::load_get_template_files(assembler_web_dir, &test_app_site);
            
            let preprocessed_templates = LoaderPreProcess::load_process_get_template_files(assembler_web_dir, &test_app_site).templates;
            
            let main_template_key = format!("{}_{}", test_app_site, app_file_name).to_lowercase();
            if !templates.contains_key(&main_template_key) {
                println!("❌ No main template found for key: {}", main_template_key);
                continue;
            }
            if !preprocessed_templates.contains_key(&main_template_key) {
                println!("❌ No preprocessed main template found for key: {}", main_template_key);
                continue;
            }
            
            for (app_view, app_view_prefix) in app_view_scenarios {
                println!("\n📊 RUST PERFORMANCE ANALYSIS: {}, {}, AppView='{}'", test_app_site, app_file_name, app_view);
                println!("{}", "=".repeat(60));
                
                // Normal Engine
                LoaderNormal::clear_cache();
                LoaderPreProcess::clear_cache();
                let mut normal_engine = EngineNormal::new(app_file_name.clone());
                normal_engine.set_app_view_prefix(app_view_prefix.clone());
                let start = Instant::now();
                let mut normal_result = String::new();
                for _ in 0..iterations {
                    normal_result = normal_engine.merge_templates(&test_app_site, &app_file_name,
                        if app_view.is_empty() { None } else { Some(&app_view) }, &mut templates, true);
                }
                let normal_time = start.elapsed().as_millis();
                
                // PreProcess Engine
                LoaderNormal::clear_cache();
                LoaderPreProcess::clear_cache();
                let mut preprocess_engine = EnginePreProcess::new(app_file_name.clone());
                preprocess_engine.set_app_view_prefix(app_view_prefix.clone());
                let start = Instant::now();
                let mut preprocess_result = String::new();
                for _ in 0..iterations {
                    preprocess_result = preprocess_engine.merge_templates(&test_app_site, &app_file_name,
                        if app_view.is_empty() { None } else { Some(&app_view) }, &preprocessed_templates, true);
                }
                let preprocess_time = start.elapsed().as_millis();
                
                // Analysis
                println!("Normal Engine:     {}ms", normal_time);
                println!("PreProcess Engine: {}ms", preprocess_time);
                println!("Results match:     {}", if normal_result == preprocess_result { "✅ YES" } else { "❌ NO" });
                println!("Normal size:       {} chars", normal_result.len());
                println!("PreProcess size:   {} chars", preprocess_result.len());
                
                if normal_result.is_empty() || preprocess_result.is_empty() {
                    println!("❌ Output is empty. Check template keys and input files for this appsite.");
                } else if preprocess_time < normal_time {
                    let diff_ms = normal_time - preprocess_time;
                    let diff_pct = if normal_time > 0 {
                        (diff_ms as f64 / normal_time as f64) * 100.0
                    } else {
                        0.0
                    };
                    println!("✅ PreProcess Engine is faster by {}ms ({:.1}%)", diff_ms, diff_pct);
                } else if preprocess_time > normal_time {
                    let diff_ms = preprocess_time - normal_time;
                    let diff_pct = if preprocess_time > 0 {
                        (diff_ms as f64 / preprocess_time as f64) * 100.0
                    } else {
                        0.0
                    };
                    println!("❌ Normal Engine is faster by {}ms ({:.1}%)", diff_ms, diff_pct);
                } else {
                    println!("⚖️  Both engines have equal performance.");
                }
            }
        }
    }
}

fn print_test_summary_table(summary_rows: &Vec<TestSummaryRow>, test_type: &str) 
{
    if summary_rows.is_empty() {
        return;
    }
    
    println!("\n==================== RUST {} SUMMARY ====================\n", test_type.to_uppercase());

    let headers = ["AppSite", "AppFile", "AppView", "OutputMatch", "ViewUnMatch", "Error"];
    let col_count = headers.len();
    let mut widths = vec![10; col_count]; // minimum width of 10

    // Calculate column widths
    for (i, header) in headers.iter().enumerate() {
        widths[i] = std::cmp::max(widths[i], header.len());
    }
    
    for row in summary_rows {
        let values = [&row.app_site, &row.app_file, &row.app_view, &row.normal_preprocess, &row.cross_view_unmatch, &row.error];
        for (i, value) in values.iter().enumerate() {
            widths[i] = std::cmp::max(widths[i], value.len());
        }
    }

    // Print header
    print!("| ");
    for (i, header) in headers.iter().enumerate() {
        print!("{:<width$}", header, width = widths[i]);
        if i < col_count - 1 {
            print!(" | ");
        }
    }
    println!(" |");

    // Print divider
    print!("|");
    for (i, _) in headers.iter().enumerate() {
        print!(" {:-<width$} ", "", width = widths[i]);
        if i < col_count - 1 {
            print!("|");
        }
    }
    println!("|");

    // Print rows
    for row in summary_rows {
        let values = [&row.app_site, &row.app_file, &row.app_view, &row.normal_preprocess, &row.cross_view_unmatch, &row.error];
        print!("| ");
        for (i, value) in values.iter().enumerate() {
            print!("{:<width$}", value, width = widths[i]);
            if i < col_count - 1 {
                print!(" | ");
            }
        }
        println!(" |");
    }
    
    // Print bottom divider
    print!("|");
    for (i, _) in headers.iter().enumerate() {
        print!(" {:-<width$} ", "", width = widths[i]);
        if i < col_count - 1 {
            print!("|");
        }
    }
    println!("|");
}

fn analyze_output_differences(output1: &str, output2: &str) 
{
    let lines1: Vec<&str> = output1.lines().collect();
    let lines2: Vec<&str> = output2.lines().collect();
    println!("   Lines: {} vs {}", lines1.len(), lines2.len());
    let common_length = std::cmp::min(lines1.len(), lines2.len());
    for i in 0..common_length {
        if lines1[i] != lines2[i] {
            println!("\n   Difference at line {}:", i + 1);
            println!("   Normal:    {} chars", lines1[i].len());
            println!("   PreProcess:{} chars", lines2[i].len());
            let min_length = std::cmp::min(lines1[i].len(), lines2[i].len());
            for j in 0..min_length {
                if lines1[i].as_bytes()[j] != lines2[i].as_bytes()[j] {
                    println!("   First difference at character {}: '{}' vs '{}'", j + 1, lines1[i].chars().nth(j).unwrap_or(' '), lines2[i].chars().nth(j).unwrap_or(' '));
                    break;
                }
            }
        }
    }
}

fn compare_engines_for_scenario(app_site: &str, app_file: &str, app_view: &str, 
    normal_engine: &EngineNormal, preprocess_engine: &EnginePreProcess,
    templates: &mut HashMap<String, (String, Option<String>)>, 
    preprocessed_templates: &HashMap<String, assembler::template_model::model_preprocess::PreprocessedTemplate>,
    enable_json_processing: bool, assembler_web_dir: &str) -> (String, String, bool) 
{
    let result_normal = normal_engine.merge_templates(app_site, app_file, Some(app_view), templates, enable_json_processing);
    let result_preprocess = preprocess_engine.merge_templates(app_site, app_file, Some(app_view), preprocessed_templates, enable_json_processing);
    
    println!("{}: 🧪 Testing scenario: AppView='{}'", app_site, app_view);
    println!("   📏 Normal Engine Output: {} chars", result_normal.len());
    println!("   📏 PreProcess Engine Output: {} chars", result_preprocess.len());
    
    let outputs_match = result_normal == result_preprocess;
    println!("\n✅ Outputs {}", if outputs_match {"Match! ✨"} else {"Differ ❌"});
    
    if !outputs_match {
        let test_output_dir = format!("{}/test_output", assembler_web_dir);
        let _ = std::fs::create_dir_all(&test_output_dir);
        let normal_path = format!("{}/{}_normal_{}_{}.html", test_output_dir, app_site, app_view, if enable_json_processing {"with"} else {"no"});
        let preprocess_path = format!("{}/{}_preprocess_{}_{}.html", test_output_dir, app_site, app_view, if enable_json_processing {"with"} else {"no"});
        let _ = std::fs::write(&normal_path, &result_normal);
        let _ = std::fs::write(&preprocess_path, &result_preprocess);
        println!("\n📄 Outputs saved to: {}", test_output_dir);
        println!("\n🔎 Output Analysis:");
        analyze_output_differences(&result_normal, &result_preprocess);
    }
    
    (result_normal, result_preprocess, outputs_match)
}

