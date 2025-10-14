use std::collections::HashMap;
use crate::loader::loader_normal::LoaderNormal;
use crate::loader::loader_preprocess::LoaderPreProcess;
use crate::engine::engine_normal::EngineNormal;
use crate::engine::engine_preprocess::EnginePreProcess;

/// Count UTF-16 code units (same as C# string.Length)
/// This is for test reporting only to match C#'s character counting
fn utf16_len(s: &str) -> usize {
    s.chars().map(|c| {
        let code_point = c as u32;
        if code_point <= 0xFFFF {
            1 // BMP character = 1 UTF-16 code unit
        } else {
            2 // Supplementary character = 2 UTF-16 code units (surrogate pair)
        }
    }).sum()
}

#[derive(Debug, Clone, serde::Serialize)]
pub struct TestSummaryRow {
    pub app_site: String,
    pub app_file: String,
    pub app_view: String,
    pub normal_preprocess: String,
    pub cross_view_unmatch: String,
    pub error: String,
}

pub struct TestingUtils;

impl TestingUtils {
    pub fn run_standard_tests(assembler_web_dir: &str, project_directory: &str, scenarios: &[crate::config::Scenario], print_html_output: bool, skip_details: bool, enable_json_processing: bool) -> Vec<TestSummaryRow> {
        run_standard_tests(assembler_web_dir, project_directory, scenarios, print_html_output, skip_details, enable_json_processing)
    }

    pub fn run_advanced_tests(assembler_web_dir: &str, project_directory: &str, scenarios: &[crate::config::Scenario], print_html_output: bool, skip_details: bool, enable_json_processing: bool) -> Vec<TestSummaryRow> {
        run_advanced_tests(assembler_web_dir, project_directory, scenarios, print_html_output, skip_details, enable_json_processing)
    }

    pub fn print_test_summary_table(assembler_web_dir_path: &str, summary_rows: &Vec<TestSummaryRow>, test_type: &str) {
        print_test_summary_table(assembler_web_dir_path, summary_rows, test_type)
    }

    /// Dumps preprocessed template structures to JSON files (matching C# DumpPreprocessedTemplateStructures)
    pub fn dump_preprocessed_template_structures(assembler_web_dir_path: &str, project_directory: &str, scenarios: &[crate::config::Scenario], skip_details: bool) {
        dump_preprocessed_template_structures(assembler_web_dir_path, project_directory, scenarios, skip_details)
    }
}

pub fn run_standard_tests(assembler_web_dir: &str, project_directory: &str, scenarios: &[crate::config::Scenario], print_html_output: bool, skip_details: bool, enable_json_processing: bool) -> Vec<TestSummaryRow>
{
    if assembler_web_dir.is_empty() {
        println!("❌ No assemblerWebDir passed");
        return Vec::new();
    }

    if project_directory.is_empty() {
        println!("❌ No projectDirectory passed");
        return Vec::new();
    }

    if scenarios.is_empty() {
        println!("❌ No scenarios passed");
        return Vec::new();
    }

    if !skip_details {
        println!("✅ JSON Processing {} for both engines\n", if enable_json_processing {"ENABLED"} else {"DISABLED"});
    }

    // Group scenarios by AppSite and AppFile
    use std::collections::HashMap;
    let mut grouped: HashMap<(String, String), Vec<&crate::config::Scenario>> = HashMap::new();
    for scenario in scenarios {
        let key = (scenario.app_site.clone(), scenario.app_file.clone());
        grouped.entry(key).or_insert_with(Vec::new).push(scenario);
    }

    // Create output directory for HTML files
    let output_dir = std::path::Path::new(project_directory).join("template_analysis").join("output");
    let _ = std::fs::create_dir_all(&output_dir);

    let mut global_test_summary_rows: Vec<TestSummaryRow> = Vec::new();

    for ((test_site, app_file_name), group) in grouped {
            if !skip_details {
                println!("{}: STANDARD TEST : appsite: {} appfile: {}", test_site, test_site, app_file_name);
                println!("{}: AppSite: {}, AppViewPrefix: Html3A", test_site, test_site);
                println!("{}: {}", test_site, "=".repeat(50));
            }
            let templates = LoaderNormal::load_get_template_files(assembler_web_dir, &test_site);

            let mut scenario_outputs = Vec::new();
            let mut scenario_unresolved: Vec<bool> = Vec::new();

            for scenario in &group {
                let app_view = &scenario.app_view;
                let normal_engine = EngineNormal::new(app_file_name.clone());
                let result_normal = normal_engine.merge_templates(&test_site, &app_file_name, Some(app_view), &mut templates.clone(), enable_json_processing);
                scenario_outputs.push(result_normal.clone());

                // Save HTML output to template_analysis/output folder
                let app_view_suffix = if app_view.is_empty() { String::new() } else { format!("_{}", app_view) };
                let output_file = output_dir.join(format!("{}{}_normal.html", test_site, app_view_suffix));
                let _ = std::fs::write(&output_file, &result_normal);

                if !skip_details {
                    println!("{}: 🧪 STANDARD TEST : scenario: AppView='{}'", test_site, app_view);
                    println!("Output length = {}", result_normal.len());
                }
                if print_html_output {
                    println!("\nFULL HTML OUTPUT for AppView '{}':\n{}\n", app_view, result_normal);
                }
                // Scan output for unresolved template placeholders and check for empty output
                let mut unresolved = Vec::new();
                let is_empty = result_normal.trim().is_empty();
                let mut start_index = 0;
                while let Some(pos) = result_normal[start_index..].find("{{") {
                    let absolute_pos = start_index + pos;
                    if let Some(end_pos) = result_normal[absolute_pos..].find("}}") {
                        let end_absolute = absolute_pos + end_pos + 2;
                        let content = &result_normal[absolute_pos + 2..absolute_pos + end_pos];
                        // Only flag as unresolved if it doesn't start with $ (same logic as C#)
                        if !content.starts_with('$') {
                            let placeholder = &result_normal[absolute_pos..end_absolute];
                            unresolved.push(placeholder.to_string());
                        }
                        start_index = end_absolute;
                    } else {
                        break;
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
                    if !skip_details {
                        println!("✅ No unresolved template placeholders found in output for AppView '{}'.", app_view);
                    }
                    scenario_unresolved.push(false);
                }
            }

        // Compare outputs for cross-view
        let mut match_result = String::new();
        if group.len() > 2 {
            let mut all_differ = true;
            let first_app_view_output = &scenario_outputs[1];
            for output in scenario_outputs.iter().skip(2) {
                if output == first_app_view_output {
                    all_differ = false;
                    break;
                }
            }
            if all_differ {
                if !skip_details {
                    println!("✅ SUCCESS: Outputs for different AppViews DO NOT MATCH in {} as expected.", test_site);
                }
                match_result = "PASS".to_string();
            } else {
                if !skip_details {
                    println!("❌ FAILURE: Some outputs for AppViews MATCH in {}. Expected them to differ.", test_site);
                }
                match_result = "FAIL".to_string();
            }
        }

        // Add summary rows for each scenario
        for (i, scenario) in group.iter().enumerate() {
            let cross_view = if i > 0 && group.len() > 2 { match_result.clone() } else { String::new() };
            let has_unresolved = scenario_unresolved.get(i).copied().unwrap_or(false);
            global_test_summary_rows.push(TestSummaryRow {
                app_site: test_site.clone(),
                app_file: app_file_name.clone(),
                app_view: scenario.app_view.clone(),
                normal_preprocess: if i == 0 { if has_unresolved { "FAIL".to_string() } else { "PASS".to_string() } } else { String::new() },
                cross_view_unmatch: cross_view,
                error: if has_unresolved {
                    if scenario_outputs.get(i).map(|s| s.trim().is_empty()).unwrap_or(false) {
                        "Empty".to_string()
                    } else {
                        "Unresolv".to_string()
                    }
                } else {
                    String::new()
                },
            });
        }
    }

    global_test_summary_rows
}

pub fn run_advanced_tests(assembler_web_dir: &str, project_directory: &str, scenarios: &[crate::config::Scenario], print_html_output: bool, skip_details: bool, enable_json_processing: bool) -> Vec<TestSummaryRow>
{
    if assembler_web_dir.is_empty() {
        println!("❌ No assemblerWebDir passed");
        return Vec::new();
    }

    if project_directory.is_empty() {
        println!("❌ No projectDirectory passed");
        return Vec::new();
    }

    if scenarios.is_empty() {
        println!("❌ No scenarios passed");
        return Vec::new();
    }

    // Group scenarios by AppSite and AppFile
    use std::collections::HashMap;
    let mut grouped: HashMap<(String, String), Vec<&crate::config::Scenario>> = HashMap::new();
    for scenario in scenarios {
        let key = (scenario.app_site.clone(), scenario.app_file.clone());
        grouped.entry(key).or_insert_with(Vec::new).push(scenario);
    }

    let mut global_test_summary_rows: Vec<TestSummaryRow> = Vec::new();

    for ((test_site, app_file_name), group) in grouped {
        if !skip_details {
            println!("🔍 ADVANCED TEST : appsite: {} appfile: {}", test_site, app_file_name);
        }

        // Load templates with timing output
        let templates = LoaderNormal::load_get_template_files(assembler_web_dir, &test_site);
        let preprocessed_site_templates = LoaderPreProcess::load_process_get_template_files(assembler_web_dir, &test_site);

        if !skip_details {
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
        }

        let mut scenario_results = Vec::new();

        for scenario in &group {
            let app_view = &scenario.app_view;
            // Use empty AppViewPrefix for default scenario (when app_view is empty), otherwise use app_file_name
            let app_view_prefix = if app_view.is_empty() { "" } else { &app_file_name };
            if !skip_details {
                println!("{}: 🧪 ADVANCED TEST : scenario: AppView='{}', AppViewPrefix='{}'", test_site, app_view, app_view_prefix);
            }

            let normal_engine = EngineNormal::new(app_view_prefix.to_string());
            let preprocess_engine = EnginePreProcess::new(app_view_prefix.to_string());

            // Time the Normal engine
            let result_normal = normal_engine.merge_templates(&test_site, &app_file_name, Some(app_view), &mut templates.clone(), enable_json_processing);

            // Time the PreProcess engine
            let result_preprocess = preprocess_engine.merge_templates(&test_site, &app_file_name, Some(app_view), &preprocessed_site_templates.templates, enable_json_processing);

            // Store for cross-AppView comparison
            scenario_results.push((app_view.clone(), result_normal.clone(), result_preprocess.clone()));

            // Save HTML outputs to template_analysis/output folder
            let output_dir = std::path::Path::new(project_directory).join("template_analysis").join("output");
            let _ = std::fs::create_dir_all(&output_dir);

            let app_view_suffix = if app_view.is_empty() { String::new() } else { format!("_{}", app_view) };
            let normal_output_file = output_dir.join(format!("{}{}_normal.html", test_site, app_view_suffix));
            let preprocess_output_file = output_dir.join(format!("{}{}_preprocess.html", test_site, app_view_suffix));

            let _ = std::fs::write(&normal_output_file, &result_normal);
            let _ = std::fs::write(&preprocess_output_file, &result_preprocess);

            if print_html_output {
                println!("\n📋 FULL HTML OUTPUT (Normal):\n{}", result_normal);
                println!("\n📋 FULL HTML OUTPUT (PreProcess):\n{}", result_preprocess);
                println!("\n{}: 📊 DETAILED OUTPUT ANALYSIS:", test_site);
                analyze_output_differences(&result_normal, &result_preprocess, true);
            }

            // Compare results
            let outputs_match = result_normal == result_preprocess;
            if !skip_details {
                println!("{}: 📊 RESULTS COMPARISON:", test_site);
                println!("{}: {}", test_site, "-".repeat(45));

                println!("{}: 🔹 All Two Methods:", test_site);
                println!("{}:   Normal: {} chars", test_site, utf16_len(&result_normal));
                println!("{}:   PreProcess: {} chars", test_site, utf16_len(&result_preprocess));

                if outputs_match {
                    println!("{}:   ✅ Normal vs PreProcess: MATCH", test_site);
                } else {
                    println!("{}:   ❌ Normal vs PreProcess: NO MATCH", test_site);
                }
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

            if !skip_details {
                if outputs_match {
                    println!("\n{}: 🎉 ALL METHODS PRODUCE IDENTICAL RESULTS! ✅", test_site);
                } else {
                    println!("\n{}: ⚠️  METHODS PRODUCE DIFFERENT RESULTS! ❌", test_site);
                }
            }

            // Show final processed outputs
            if !skip_details && !result_normal.is_empty() {
                println!("\n{}: 📋 FINAL OUTPUT SAMPLE (full HTML):", test_site);
                println!("{}", result_normal);
            }

            // Show detailed differences if methods differ
            if !skip_details && !outputs_match {
                println!("\n{}: ❗ DETAILED DIFFERENCES:", test_site);
                println!("{}: 🔸 Normal vs PreProcess:", test_site);
                println!("{}:   Normal Result:\n{}", test_site, result_normal);
                println!("{}:   PreProcess Result:\n{}", test_site, result_preprocess);
                println!();
            }

            // Check for unmerged template fields in all outputs
            if !skip_details {
                println!("\n{}: 🔎 Checking for unmerged template fields in outputs...", test_site);
            }
            let mut found_unmerged = false;

            for (name, output) in [("Normal", &result_normal), ("PreProcess", &result_preprocess)] {
                let mut unmerged_fields = Vec::new();

                // Find all ${{field}} patterns using indexOf (double brackets only, ignore single bracket ${} which are JavaScript template literals)
                let mut start_index = 0;
                while let Some(pos) = output[start_index..].find("${{") {
                    let absolute_pos = start_index + pos;
                    if let Some(end_pos) = output[absolute_pos..].find("}}") {
                        let end_absolute = absolute_pos + end_pos + 1;
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
                            .filter(|f| !f.starts_with("${{Json") && !f.starts_with("${{$Json"))
                            .collect()
                    };

                    if !filtered_fields.is_empty() {
                        if !skip_details {
                            println!("{}:   ❌ {} output contains {} unmerged non-JSON template fields!", test_site, name, filtered_fields.len());
                            for field in &filtered_fields {
                                println!("{}:      Unmerged field: {}", test_site, field);
                            }
                        }
                        found_unmerged = true;
                    } else if !skip_details {
                        println!("{}:   ✅ {} output contains no unmerged non-JSON template fields.", test_site, name);
                    }
                } else if !skip_details {
                    println!("{}:   ✅ {} output contains no unmerged template fields.", test_site, name);
                }
            }

            if !skip_details {
                if found_unmerged {
                    println!("\n{}: ⚠️  TEST FAILURE: Unmerged non-JSON template fields found in output!", test_site);
                } else {
                    println!("\n{}: 🎉 TEST SUCCESS: No unmerged non-JSON template fields found in any output.", test_site);
                }
            }
        }

        // Compare outputs from different AppViews (cross-scenario)
        // Only compare AppView scenarios (exclude empty AppView scenario)
        let app_view_results: Vec<_> = scenario_results.iter().filter(|(app_view, _, _)| !app_view.is_empty()).collect();
        if app_view_results.len() > 1 {
            if !skip_details {
                println!("\n🔬 Cross-AppView Output Comparison:");
            }
            let mut all_app_views_differ = true;
            let first_app_view_normal = &app_view_results[0].1;
            let first_app_view_preprocess = &app_view_results[0].2;

            for i in 1..app_view_results.len() {
                let cross_view_match = if &app_view_results[i].1 == first_app_view_normal && &app_view_results[i].2 == first_app_view_preprocess {
                    if !skip_details {
                        println!("❌ FAILURE: Outputs for AppView '{}' and AppView '{}' MATCH. Expected them to differ.", app_view_results[0].0, app_view_results[i].0);
                    }
                    all_app_views_differ = false;
                    "FAIL".to_string()
                } else {
                    if !skip_details {
                        println!("✅ SUCCESS: Outputs for AppView '{}' and AppView '{}' DO NOT MATCH as expected.", app_view_results[0].0, app_view_results[i].0);
                    }
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

            if !skip_details {
                if all_app_views_differ {
                    println!("🎉 All AppView outputs are different as expected.");
                } else {
                    println!("❌ Some AppView outputs match when they should differ.");
                }
            }
        }
    }
    global_test_summary_rows
}

pub fn print_test_summary_table(assembler_web_dir_path: &str, summary_rows: &Vec<TestSummaryRow>, test_type: &str)
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

    // Save HTML file
    match save_test_summary_html(assembler_web_dir_path, summary_rows, test_type) {
        Ok(path) => println!("Test summary HTML saved to: {}", path),
        Err(e) => println!("Error saving test summary HTML: {}", e),
    }

    // Save JSON file
    match save_test_summary_json(assembler_web_dir_path, summary_rows, test_type) {
        Ok(path) => println!("Test summary JSON saved to: {}", path),
        Err(e) => println!("Error saving test summary JSON: {}", e),
    }
}

fn save_test_summary_html(assembler_web_dir_path: &str, summary_rows: &Vec<TestSummaryRow>, test_type: &str) -> Result<String, Box<dyn std::error::Error>> {
    let mut html = String::new();
    html.push_str("<!DOCTYPE html>\n<html>\n<head>\n");
    html.push_str("    <meta charset=\"UTF-8\">\n");
    html.push_str("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n");
    html.push_str("    <title>Test Summary Table</title>\n");
    html.push_str("    <style>\n");
    html.push_str("        body { font-family: Arial, sans-serif; margin: 20px; }\n");
    html.push_str("        h1, h2 { color: #333; }\n");
    html.push_str("        .table-container { overflow-x: auto; }\n");
    html.push_str("        table { border-collapse: collapse; width: 100%; margin-top: 20px; min-width: 600px; }\n");
    html.push_str("        th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }\n");
    html.push_str("        th { background-color: #4CAF50; color: white; }\n");
    html.push_str("        tr:nth-child(even) { background-color: #f2f2f2; }\n");
    html.push_str("        @media (max-width: 768px) {\n");
    html.push_str("            body { margin: 10px; }\n");
    html.push_str("            th, td { padding: 8px; font-size: 14px; }\n");
    html.push_str("            h1, h2 { font-size: 20px; }\n");
    html.push_str("        }\n");
    html.push_str("    </style>\n</head>\n<body>\n");
    html.push_str(&format!("<h2>RUST {} SUMMARY TABLE</h2>\n", test_type.to_uppercase()));
    html.push_str("<div class=\"table-container\">\n<table>\n");
    html.push_str("<tr><th>AppSite</th><th>AppFile</th><th>AppView</th><th>OutputMatch</th><th>ViewUnMatch</th><th>Error</th></tr>\n");

    for row in summary_rows {
        html.push_str(&format!(
            "<tr><td>{}</td><td>{}</td><td>{}</td><td>{}</td><td>{}</td><td>{}</td></tr>\n",
            row.app_site, row.app_file, row.app_view,
            row.normal_preprocess, row.cross_view_unmatch, row.error
        ));
    }

    html.push_str("</table>\n</div>\n</body>\n</html>\n");

    // Sanitize testType for filename
    let test_type_file = test_type.replace(" ", "").replace("-", "").to_lowercase();
    let reports_dir = format!("{}/Reports", assembler_web_dir_path);
    std::fs::create_dir_all(&reports_dir)?;
    let out_file = format!("{}/rust_{}_Summary.html", reports_dir, test_type_file);
    std::fs::write(&out_file, html)?;
    Ok(out_file)
}

fn save_test_summary_json(assembler_web_dir_path: &str, summary_rows: &Vec<TestSummaryRow>, test_type: &str) -> Result<String, Box<dyn std::error::Error>> {
    let test_type_file = test_type.replace(" ", "").replace("-", "").to_lowercase();
    let reports_dir = format!("{}/Reports", assembler_web_dir_path);
    std::fs::create_dir_all(&reports_dir)?;
    let json_file = format!("{}/rust_{}_Summary.json", reports_dir, test_type_file);
    let json = serde_json::to_string_pretty(summary_rows)?;
    std::fs::write(&json_file, json)?;
    Ok(json_file)
}

pub fn analyze_output_differences(output1: &str, output2: &str, print_detailed_diff: bool)
{
    println!("   Normal length: {} chars", output1.len());
    println!("   PreProcess length: {} chars", output2.len());
    println!("   Difference: {} chars", output1.len() as i64 - output2.len() as i64);

    if print_detailed_diff {
        // Find first character-level difference
        let chars1: Vec<char> = output1.chars().collect();
        let chars2: Vec<char> = output2.chars().collect();
        let min_len = std::cmp::min(chars1.len(), chars2.len());

        for i in 0..min_len {
            if chars1[i] != chars2[i] {
                println!("\n   First difference at position {}:", i);
                let start = if i > 50 { i - 50 } else { 0 };
                let end1 = std::cmp::min(chars1.len(), i + 50);
                let end2 = std::cmp::min(chars2.len(), i + 50);

                println!("   Normal context (chars {} to {}):", start, end1);
                let context1: String = chars1[start..end1].iter().collect();
                println!("     '{}'", context1);

                println!("   PreProcess context (chars {} to {}):", start, end2);
                let context2: String = chars2[start..end2].iter().collect();
                println!("     '{}'", context2);

                break;
            }
        }

        // Check if one output is longer
        if output1.len() != output2.len() && min_len == std::cmp::min(output1.len(), output2.len()) {
            println!("\n   Files differ in length at position {}", min_len);
            if output1.len() > output2.len() {
                let extra_len = std::cmp::min(100, output1.len() - min_len);
                println!("   Normal has extra {} chars at the end:", output1.len() - output2.len());
                println!("     '{}'", &output1[min_len..min_len + extra_len]);
            } else {
                let extra_len = std::cmp::min(100, output2.len() - min_len);
                println!("   PreProcess has extra {} chars at the end:", output2.len() - output1.len());
                println!("     '{}'", &output2[min_len..min_len + extra_len]);
            }
        }
    } else {
        // Split both outputs into lines for comparison
        let lines1: Vec<&str> = output1.split('\n').collect();
        let lines2: Vec<&str> = output2.split('\n').collect();

        println!("   Lines: {} vs {}", lines1.len(), lines2.len());

        // Compare line by line
        let common_length = std::cmp::min(lines1.len(), lines2.len());
        for i in 0..common_length {
            if lines1[i] != lines2[i] {
                println!("\n   Difference at line {}:", i + 1);
                println!("   Normal:    {} chars", utf16_len(&lines1[i]));
                println!("   PreProcess:{} chars", utf16_len(&lines2[i]));

                // Show first position where they differ
                let min_length = std::cmp::min(lines1[i].len(), lines2[i].len());
                let chars1: Vec<char> = lines1[i].chars().collect();
                let chars2: Vec<char> = lines2[i].chars().collect();
                for j in 0..min_length {
                    if chars1[j] != chars2[j] {
                        println!("   First difference at character {}: '{}' vs '{}'", j + 1, chars1[j], chars2[j]);
                        break;
                    }
                }
            }
        }
    }
}

pub fn compare_engines_for_scenario(app_site: &str, app_file: &str, app_view: &str,
    normal_engine: &EngineNormal, preprocess_engine: &EnginePreProcess,
    templates: &mut HashMap<String, (String, Option<String>)>,
    preprocessed_templates: &HashMap<String, crate::model::model_preprocess::PreprocessedTemplate>,
    enable_json_processing: bool, assembler_web_dir: &str, skip_details: bool) -> (String, String, bool)
{
    let result_normal = normal_engine.merge_templates(app_site, app_file, Some(app_view), templates, enable_json_processing);
    let result_preprocess = preprocess_engine.merge_templates(app_site, app_file, Some(app_view), preprocessed_templates, enable_json_processing);

    if !skip_details {
        println!("{}: 🧪 Testing scenario: AppView='{}'", app_site, app_view);
        println!("   📏 Normal Engine Output: {} chars", result_normal.len());
        println!("   📏 PreProcess Engine Output: {} chars", result_preprocess.len());
    }

    let outputs_match = result_normal == result_preprocess;
    if !skip_details {
        println!("\n✅ Outputs {}", if outputs_match {"Match! ✨"} else {"Differ ❌"});
    }

    if !outputs_match {
        let test_output_dir = format!("{}/test_output", assembler_web_dir);
        let _ = std::fs::create_dir_all(&test_output_dir);
        let normal_path = format!("{}/{}_normal_{}_{}.html", test_output_dir, app_site, app_view, if enable_json_processing {"with"} else {"no"});
        let preprocess_path = format!("{}/{}_preprocess_{}_{}.html", test_output_dir, app_site, app_view, if enable_json_processing {"with"} else {"no"});
        let _ = std::fs::write(&normal_path, &result_normal);
        let _ = std::fs::write(&preprocess_path, &result_preprocess);
        println!("\n📄 Outputs saved to: {}", test_output_dir);
        println!("\n🔎 Output Analysis:");
        analyze_output_differences(&result_normal, &result_preprocess, false);
    }

    (result_normal, result_preprocess, outputs_match)
}

/// Dumps preprocessed template structures to JSON files (matching C# DumpPreprocessedTemplateStructures)
pub fn dump_preprocessed_template_structures(assembler_web_dir_path: &str, project_directory: &str, scenarios: &[crate::config::Scenario], skip_details: bool) {
    use std::path::Path;
    use std::fs;
    use crate::api::api_response::ApiResponse;

    if assembler_web_dir_path.is_empty() {
        if !skip_details {
            println!("❌ No assemblerWebDirPath passed for DumpPreprocessedTemplateStructures");
        }
        return;
    }

    if project_directory.is_empty() {
        if !skip_details {
            println!("❌ No projectDirectory passed for DumpPreprocessedTemplateStructures");
        }
        return;
    }

    if scenarios.is_empty() {
        if !skip_details {
            println!("❌ No scenarios passed for DumpPreprocessedTemplateStructures");
        }
        return;
    }

    // Get unique AppSites from scenarios
    let mut app_sites: Vec<String> = scenarios.iter()
        .map(|s| s.app_site.clone())
        .collect::<std::collections::HashSet<_>>()
        .into_iter()
        .collect();
    app_sites.sort();

    for site in app_sites {

        LoaderNormal::clear_cache();
        LoaderPreProcess::clear_cache();

        let _templates = LoaderNormal::load_get_template_files(assembler_web_dir_path, &site);
        let preprocessed_site_templates = LoaderPreProcess::load_process_get_template_files(assembler_web_dir_path, &site);

        let full_json = ApiResponse::serialize_preprocessed_site_templates(&preprocessed_site_templates, true);

        // Save to file for easier analysis
        let output_dir = Path::new(project_directory).join("template_analysis");
        if let Err(e) = fs::create_dir_all(&output_dir) {
            if !skip_details {
                println!("❌ Error creating output directory: {}", e);
            }
            continue;
        }

        let summary_file = output_dir.join(format!("{}_summary.json", site));
        let full_file = output_dir.join(format!("{}_full.json", site));

        if summary_file.exists() {
            let _ = fs::remove_file(&summary_file);
        }
        if full_file.exists() {
            let _ = fs::remove_file(&full_file);
        }

        let summary = ApiResponse::create_preprocessed_summary(&preprocessed_site_templates);
        let summary_json = ApiResponse::serialize_preprocessed_summary(&summary, true);

        if let Err(e) = fs::write(&summary_file, summary_json) {
            if !skip_details {
                println!("❌ Error writing summary file: {}", e);
            }
        }

        if let Err(e) = fs::write(&full_file, full_json) {
            if !skip_details {
                println!("❌ Error writing full file: {}", e);
            }
        } else if !skip_details {
            println!("✅ Dumped structure for {}", site);
            println!("   Summary: {}", summary_file.display());
            println!("   Full: {}", full_file.display());
        }
    }
}
