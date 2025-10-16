use std::time::Instant;
use std::fs;
use serde::Serialize;
use chrono;
use crate::loader::loader_normal::LoaderNormal;
use crate::loader::loader_preprocess::LoaderPreProcess;
use crate::engine::engine_normal::EngineNormal;
use crate::engine::engine_preprocess::EnginePreProcess;
use crate::common::common_util::CommonUtil;

#[derive(Debug, Clone)]
pub struct PerfSummaryRow {
    pub app_site: String,
    pub app_file: String,
    pub app_view: String,
    pub iterations: i32,
    pub normal_time_nanos: u128,
    pub preprocess_time_nanos: u128,
    pub output_size: usize,
    pub results_match: String,
    pub perf_difference: String,
    pub scenario_total_time_ms: u128,
    pub elapsed_time_ms: u128,
}

impl PerfSummaryRow {
    pub fn normal_time_ms(&self) -> f64 {
        self.normal_time_nanos as f64 / 1_000_000.0
    }

    pub fn preprocess_time_ms(&self) -> f64 {
        self.preprocess_time_nanos as f64 / 1_000_000.0
    }
}

impl Serialize for PerfSummaryRow {
    fn serialize<S>(&self, serializer: S) -> Result<S::Ok, S::Error>
    where
        S: serde::Serializer,
    {
        use serde::ser::SerializeStruct;
        let mut state = serializer.serialize_struct("PerfSummaryRow", 13)?;
        state.serialize_field("AppSite", &self.app_site)?;
        state.serialize_field("AppFile", &self.app_file)?;
        state.serialize_field("AppView", &self.app_view)?;
        state.serialize_field("Iterations", &self.iterations)?;
        state.serialize_field("NormalTimeNanos", &self.normal_time_nanos)?;
        state.serialize_field("PreProcessTimeNanos", &self.preprocess_time_nanos)?;
        state.serialize_field("OutputSize", &self.output_size)?;
        state.serialize_field("ResultsMatch", &self.results_match)?;
        state.serialize_field("PerfDifference", &self.perf_difference)?;
        state.serialize_field("ScenarioTotalTimeMs", &self.scenario_total_time_ms)?;
        state.serialize_field("ElapsedTimeMs", &self.elapsed_time_ms)?;
    // Serialize numeric ms fields as numbers (rounded to 2 decimals) so other tools can parse them as numbers
    let normal_ms = (self.normal_time_ms() * 100.0).round() / 100.0;
    let preprocess_ms = (self.preprocess_time_ms() * 100.0).round() / 100.0;
    state.serialize_field("NormalTimeMs", &normal_ms)?;
    state.serialize_field("PreProcessTimeMs", &preprocess_ms)?;
        state.end()
    }
}

pub struct PerformanceUtils;

impl PerformanceUtils 
{
    /// Runs performance comparison and returns summary rows
    pub fn run_performance_comparison(assembler_web_dir_path: &str, _project_directory: &str, scenarios: &[crate::config::Scenario], skip_details: bool, enable_json_processing: bool) -> Vec<PerfSummaryRow>
	{
        let start_time = Instant::now();

        if assembler_web_dir_path.is_empty() {
            println!("❌ No assemblerWebDirPath passed");
            return Vec::new();
        }

        if scenarios.is_empty() {
            println!("❌ No scenarios passed");
            return Vec::new();
        }

        let iterations = 1000;
        let mut perf_summary_rows: Vec<PerfSummaryRow> = Vec::new();

        for scenario in scenarios {
            let scenario_start_time = Instant::now();
            let test_app_site = &scenario.app_site;
            let app_file_name = &scenario.app_file;
            let app_view = &scenario.app_view;
            let app_view_prefix = if app_view.is_empty() {
                String::new()
            } else {
                app_view.chars().take(std::cmp::min(app_view.len(), 6)).collect::<String>()
            };

            // Clear cache and load templates
            LoaderNormal::clear_cache();
            LoaderPreProcess::clear_cache();
            let mut templates = LoaderNormal::load_get_template_files(assembler_web_dir_path, test_app_site);

            let site_templates = LoaderPreProcess::load_process_get_template_files(assembler_web_dir_path, test_app_site);

            if templates.is_empty() {
                continue;
            }

            let main_template_key = format!("{}_{}", test_app_site, app_file_name).to_lowercase();
            if !templates.contains_key(&main_template_key) {
                continue;
            }

            if !skip_details {
                println!("{}", "-".repeat(60));
                println!("[Rust] Testing: AppSite={}, AppFile={}, AppView={}", test_app_site, app_file_name, app_view);
                println!("[Rust] Iterations: {}", iterations);
            }

            // Normal Engine
            LoaderNormal::clear_cache();
            LoaderPreProcess::clear_cache();
            let mut normal_engine = EngineNormal::new(app_file_name.clone());
            normal_engine.set_app_view_prefix(app_view_prefix.clone());

            // Warmup - run a few iterations first to ensure consistent performance
            for _ in 0..100 {
                normal_engine.merge_templates(&test_app_site, &app_file_name,
                    if app_view.is_empty() { None } else { Some(&app_view) }, &mut templates, enable_json_processing);
            }

            let start = Instant::now();
            let mut result_normal = String::new();
            for _ in 0..iterations {
                result_normal = normal_engine.merge_templates(&test_app_site, &app_file_name,
                    if app_view.is_empty() { None } else { Some(&app_view) }, &mut templates, enable_json_processing);
            }
            let normal_duration = start.elapsed();
            let normal_time_nanos = normal_duration.as_nanos();
            let normal_time_ms = normal_time_nanos as f64 / 1_000_000.0;

            if !skip_details {
                let avg = normal_time_ms / iterations as f64;
                println!("[Rust] Normal Engine:     {:.0}ms | Avg: {:.3}ms/op | Size: {} chars", normal_time_ms, avg, CommonUtil::utf16_len(&result_normal));
            }

            // PreProcess Engine
            LoaderNormal::clear_cache();
            LoaderPreProcess::clear_cache();
            let mut preprocess_engine = EnginePreProcess::new(app_file_name.clone());
            preprocess_engine.set_app_view_prefix(app_view_prefix.clone());

            // Warmup for PreProcess engine
            for _ in 0..100 {
                preprocess_engine.merge_templates(&test_app_site, &app_file_name,
                    if app_view.is_empty() { None } else { Some(&app_view) }, &site_templates.templates, enable_json_processing);
            }

            let start = Instant::now();
            let mut result_preprocess = String::new();
            for _ in 0..iterations {
                result_preprocess = preprocess_engine.merge_templates(&test_app_site, &app_file_name,
                    if app_view.is_empty() { None } else { Some(&app_view) }, &site_templates.templates, enable_json_processing);
            }
            let preprocess_duration = start.elapsed();
            let preprocess_time_nanos = preprocess_duration.as_nanos();
            let preprocess_time_ms = preprocess_time_nanos as f64 / 1_000_000.0;

            if !skip_details {
                let avg = preprocess_time_ms / iterations as f64;
                println!("[Rust] PreProcess Engine: {:.0}ms | Avg: {:.3}ms/op | Size: {} chars", preprocess_time_ms, avg, CommonUtil::utf16_len(&result_preprocess));

                let difference_ms = preprocess_time_ms - normal_time_ms;
                let difference_percent = if normal_time_ms > 0.0 {
                    (difference_ms / normal_time_ms) * 100.0
                } else {
                    0.0
                };
                let results_match = result_normal == result_preprocess;
                let sign_ms = if difference_ms >= 0.0 { "+" } else { "" };
                let sign_pct = if difference_percent >= 0.0 { "+" } else { "" };
                println!("[Rust] Performance: {}{:.0}ms ({}{:.1}%) | Match: {}", sign_ms, difference_ms, sign_pct, difference_percent, if results_match { "YES" } else { "NO" });
            }

            let scenario_total_time = scenario_start_time.elapsed().as_millis();
            let elapsed_time = start_time.elapsed().as_millis();

            if !skip_details {
                println!("[Rust] Scenario Total Time: {}ms | Elapsed: {}ms", scenario_total_time, elapsed_time);
            }

            let results_match = result_normal == result_preprocess;
            let perf_diff = if normal_time_ms > 0.0 {
                format!("{:.1}%", (preprocess_time_ms - normal_time_ms) / normal_time_ms * 100.0)
            } else {
                "0%".to_string()
            };

            perf_summary_rows.push(PerfSummaryRow {
                app_site: test_app_site.clone(),
                app_file: app_file_name.clone(),
                app_view: app_view.clone(),
                iterations,
                normal_time_nanos,
                preprocess_time_nanos,
                output_size: CommonUtil::utf16_len(&result_normal),
                results_match: if results_match { "YES".to_string() } else { "NO".to_string() },
                perf_difference: perf_diff,
                scenario_total_time_ms: scenario_total_time,
                elapsed_time_ms: elapsed_time,
            });
        }

        if !skip_details {
            let elapsed = start_time.elapsed();
            println!("\n========== Performance Testing Completed in {}ms ==========\n", elapsed.as_millis());
        }
        perf_summary_rows
    }

    /// Prints the performance summary table in markdown format
    pub fn print_perf_summary_table(_assembler_web_dir_path: &str, project_directory: &str, summary_rows: &Vec<PerfSummaryRow>) 
	{
        if summary_rows.is_empty() {
            return;
        }
        
        println!("\n==================== RUST PERFORMANCE SUMMARY ====================\n");

        let headers = vec!["AppSite", "AppView", "Normal(ms)", "PreProc(ms)", "Match", "PerfDiff", "ScnTime(ms)", "Elapsed(ms)"];
        let col_count = headers.len();
        let mut widths = vec![0; col_count];

        // Calculate column widths
        for i in 0..col_count {
            widths[i] = headers[i].len();
        }

        for row in summary_rows {
            widths[0] = widths[0].max(row.app_site.len());
            widths[1] = widths[1].max(row.app_view.len());
            widths[2] = widths[2].max(format!("{:.2}", row.normal_time_ms()).len());
            widths[3] = widths[3].max(format!("{:.2}", row.preprocess_time_ms()).len());
            widths[4] = widths[4].max(row.results_match.len());
            widths[5] = widths[5].max(row.perf_difference.len());
            widths[6] = widths[6].max(row.scenario_total_time_ms.to_string().len());
            widths[7] = widths[7].max(row.elapsed_time_ms.to_string().len());
        }
        
        // Print header
        print!("| ");
        for i in 0..col_count {
            print!("{}", format!("{:<width$}", headers[i], width = widths[i]));
            if i < col_count - 1 {
                print!(" | ");
            }
        }
        println!(" |");
        
        // Print divider
        print!("|");
        for i in 0..col_count {
            print!(" {} ", "-".repeat(widths[i]));
            if i < col_count - 1 {
                print!("|");
            }
        }
        println!("|");
        
        // Print rows
        for row in summary_rows {
            print!("| ");
            print!("{:<width$}", row.app_site, width = widths[0]);
            print!(" | ");
            print!("{:<width$}", row.app_view, width = widths[1]);
            print!(" | ");
            print!("{:<width$.2}", format!("{:.2}", row.normal_time_ms()), width = widths[2]);
            print!(" | ");
            print!("{:<width$.2}", format!("{:.2}", row.preprocess_time_ms()), width = widths[3]);
            print!(" | ");
            print!("{:<width$}", row.results_match, width = widths[4]);
            print!(" | ");
            print!("{:<width$}", row.perf_difference, width = widths[5]);
            print!(" | ");
            print!("{:<width$}", row.scenario_total_time_ms, width = widths[6]);
            print!(" | ");
            print!("{:<width$}", row.elapsed_time_ms, width = widths[7]);
            println!(" |");
        }
        print!("|");
        for i in 0..col_count {
            print!(" {} ", "-".repeat(widths[i]));
            if i < col_count - 1 {
                print!("|");
            }
        }
        println!("|");
        
        // Save performance summary to file
        let reports_dir = format!("{}/template_analysis/Reports", project_directory);
        if let Err(e) = std::fs::create_dir_all(&reports_dir) {
            println!("❌ Error creating Reports directory: {}", e);
            return;
        }

        let perf_json_file = format!("{}/rust_perfsummary.json", reports_dir);
        let perf_html_file = format!("{}/rust_perfsummary.html", reports_dir);
        
        let perf_json = serde_json::to_string_pretty(&summary_rows).unwrap();
        if let Err(e) = fs::write(&perf_json_file, perf_json) {
            println!("❌ Error writing performance JSON file: {}", e);
        } else {
            println!("Performance summary JSON saved to: {}", perf_json_file);
        }
        
        // Generate HTML performance summary table
        let mut html = String::new();
        html.push_str("<!DOCTYPE html>\n");
        html.push_str("<html>\n");
        html.push_str("<head>\n");
        html.push_str("    <meta charset=\"UTF-8\">\n");
        html.push_str("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n");
        html.push_str("    <title>Rust Performance Summary Table</title>\n");
        html.push_str("    <style>\n");
        html.push_str("        body { font-family: Arial, sans-serif; margin: 20px; }\n");
        html.push_str("        h2 { color: #333; }\n");
        html.push_str("        .table-container { overflow-x: auto; }\n");
        html.push_str("        table { border-collapse: collapse; width: 100%; min-width: 600px; }\n");
        html.push_str("        th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }\n");
        html.push_str("        th { background-color: #4CAF50; color: white; }\n");
        html.push_str("        tr:nth-child(even) { background-color: #f2f2f2; }\n");
        html.push_str("        @media (max-width: 768px) {\n");
        html.push_str("            body { margin: 10px; }\n");
        html.push_str("            th, td { padding: 8px; font-size: 14px; }\n");
        html.push_str("            h2 { font-size: 20px; }\n");
        html.push_str("        }\n");
        html.push_str("    </style>\n");
        html.push_str("</head>\n");
        html.push_str("<body>\n");
        html.push_str("    <h2>Rust Performance Summary Table</h2>\n");
        html.push_str(&format!("    <div class=\"meta\" style=\"color: #666; font-style: italic; margin-bottom: 10px;\">Generated: {} UTC | Iterations: 1000, Warmup: 100 | All times in milliseconds (ms)</div>\n", chrono::Utc::now().format("%Y-%m-%d %H:%M:%S")));
        html.push_str("    <div class=\"table-container\">\n");
        html.push_str("    <table>\n");
        html.push_str("        <tr><th>AppSite</th><th>AppView</th><th>Normal(ms)</th><th>PreProc(ms)</th><th>Match</th><th>PerfDiff</th><th>ScnTime(ms)</th><th>Elapsed(ms)</th></tr>\n");

        for row in summary_rows {
            html.push_str("        <tr>");
            html.push_str(&format!("<td>{}</td>", row.app_site));
            html.push_str(&format!("<td>{}</td>", row.app_view));
            html.push_str(&format!("<td>{:.2}</td>", row.normal_time_ms()));
            html.push_str(&format!("<td>{:.2}</td>", row.preprocess_time_ms()));
            html.push_str(&format!("<td>{}</td>", row.results_match));
            html.push_str(&format!("<td>{}</td>", row.perf_difference));
            html.push_str(&format!("<td>{}</td>", row.scenario_total_time_ms));
            html.push_str(&format!("<td>{}</td>", row.elapsed_time_ms));
            html.push_str("</tr>\n");
        }

        html.push_str("    </table>\n");
        html.push_str("    </div>\n");
        html.push_str("</body>\n");
        html.push_str("</html>");
        if let Err(e) = fs::write(&perf_html_file, html) {
            println!("❌ Error writing performance HTML file: {}", e);
        } else {
            println!("Performance summary HTML saved to: {}", perf_html_file);
        }
    }
}
