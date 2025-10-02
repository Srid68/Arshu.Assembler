use std::time::Instant;
use std::fs;
use serde::{Serialize, Deserialize};
use crate::template_loader::loader_normal::LoaderNormal;
use crate::template_loader::loader_preprocess::LoaderPreProcess;
use crate::template_engine::engine_normal::EngineNormal;
use crate::template_engine::engine_preprocess::EnginePreProcess;

#[derive(Debug, Clone, Serialize, Deserialize)]
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
}

impl PerfSummaryRow {
    pub fn normal_time_ms(&self) -> f64 {
        self.normal_time_nanos as f64 / 1_000_000.0
    }
    
    pub fn preprocess_time_ms(&self) -> f64 {
        self.preprocess_time_nanos as f64 / 1_000_000.0
    }
}

pub struct PerformanceUtils;

impl PerformanceUtils 
{
    /// Runs performance comparison and returns summary rows
    pub fn run_performance_comparison(assembler_web_dir_path: &str, app_site_filter: Option<&str>, skip_details: bool, enable_json_processing: bool) -> Vec<PerfSummaryRow> 
	{
        let iterations = 1000;
        let app_sites_path = std::path::Path::new(assembler_web_dir_path).join("AppSites");
        if !app_sites_path.exists() {
            if !skip_details {
                println!("❌ AppSites directory not found: {:?}", app_sites_path);
            }
            return Vec::new();
        }
        
        let mut perf_summary_rows: Vec<PerfSummaryRow> = Vec::new();
        
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
                
                // Clear cache and load templates
                LoaderNormal::clear_cache();
                LoaderPreProcess::clear_cache();
                let mut templates = LoaderNormal::load_get_template_files(assembler_web_dir_path, &test_app_site);
                
                let site_templates = LoaderPreProcess::load_process_get_template_files(assembler_web_dir_path, &test_app_site);
                
                if templates.is_empty() {
                    if !skip_details {
                        println!("❌ No templates found for {}", test_app_site);
                    }
                    continue;
                }
                
                let main_template_key = format!("{}_{}", test_app_site, app_file_name).to_lowercase();
                if !templates.contains_key(&main_template_key) {
                    if !skip_details {
                        println!("❌ No main template found for {}", main_template_key);
                    }
                    continue;
                }
                
                if !skip_details {
                    println!("Template Key: {}", main_template_key);
                    println!("Templates available: {}", templates.len());
                }
                
                // Build AppView scenarios
                let mut app_view_scenarios = vec![(String::new(), String::new())]; // No AppView
                
                let views_path = app_site_dir.join("Views");
                if views_path.exists() {
                    for view_entry in std::fs::read_dir(&views_path).unwrap() {
                        let view_entry = view_entry.unwrap();
                        if let Some(view_name) = view_entry.path().file_stem() {
                            let view_name_str = view_name.to_string_lossy().to_string();
                            let view_name_lower = view_name_str.to_lowercase();
                            if view_name_lower.contains("content") {
                                if let Some(content_index) = view_name_lower.find("content") {
                                    if content_index > 0 {
                                        let view_part = &view_name_str[..content_index];
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
                
                for (app_view, app_view_prefix) in app_view_scenarios {
                    if !skip_details {
                        println!("{}", "-".repeat(60));
                        println!(">>> RUST SCENARIO : '{}', '{}', '{}', '{}'", test_app_site, app_file_name, app_view, app_view_prefix);
                        println!("Iterations per test: {}", iterations);
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
                    
                    if !skip_details {
                        println!("[Normal Engine] {} iterations: {:.2}ms", iterations, normal_time_nanos as f64 / 1_000_000.0);
                        println!("[Normal Engine] Avg: {:.3}ms per op, Output size: {} chars", 
                            normal_time_nanos as f64 / 1_000_000.0 / iterations as f64, result_normal.len());
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
                    
                    if !skip_details {
                        println!("[PreProcess Engine] {} iterations: {:.2}ms", iterations, preprocess_time_nanos as f64 / 1_000_000.0);
                        println!("[PreProcess Engine] Avg: {:.3}ms per op, Output size: {} chars",
                            preprocess_time_nanos as f64 / 1_000_000.0 / iterations as f64, result_preprocess.len());
                        
                        // Comparison
                        println!(">>> RUST PERFORMANCE COMPARISON:");
                        println!("{}", "-".repeat(50));
                        let difference_nanos = preprocess_time_nanos as i128 - normal_time_nanos as i128;
                        let difference_ms = difference_nanos as f64 / 1_000_000.0;
                        let difference_percent = if normal_time_nanos > 0 {
                            (difference_nanos as f64 / normal_time_nanos as f64) * 100.0
                        } else {
                            0.0
                        };
                        
                        println!("Time difference: {:.2}ms ({:.1}%)", difference_ms, difference_percent);
                        let results_match = result_normal == result_preprocess;
                        println!("Results match: {}", if results_match { "✅ YES" } else { "❌ NO" });
                    }
                    
                    let results_match = result_normal == result_preprocess;
                    let perf_diff = if normal_time_nanos > 0 {
                        format!("{:.1}%", (preprocess_time_nanos as f64 - normal_time_nanos as f64) / normal_time_nanos as f64 * 100.0)
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
                        output_size: result_normal.len(),
                        results_match: if results_match { "YES".to_string() } else { "NO".to_string() },
                        perf_difference: perf_diff,
                    });
                }
            }
        }
        
        perf_summary_rows
    }

    /// Prints the performance summary table in markdown format
    pub fn print_perf_summary_table(assembler_web_dir_path: &str, summary_rows: &Vec<PerfSummaryRow>) 
	{
        if summary_rows.is_empty() {
            return;
        }
        
        println!("\n==================== RUST PERFORMANCE SUMMARY ====================\n");
        
        let headers = vec!["AppSite", "AppView", "Normal(ms)", "PreProc(ms)", "Match", "PerfDiff"];
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
        let output_dir = assembler_web_dir_path;
        if let Err(e) = std::fs::create_dir_all(&output_dir) {
            println!("❌ Error creating output directory: {}", e);
            return;
        }
        
        let perf_json_file = format!("{}/rust_perfsummary.json", output_dir);
        let perf_html_file = format!("{}/rust_perfsummary.html", output_dir);
        
        let perf_json = serde_json::to_string_pretty(&summary_rows).unwrap();
        if let Err(e) = fs::write(&perf_json_file, perf_json) {
            println!("❌ Error writing performance JSON file: {}", e);
        } else {
            println!("Performance summary JSON saved to: {}", perf_json_file);
        }
        
        // Generate HTML performance summary table
        let mut html = String::new();
        html.push_str("<html><head><title>Rust Performance Summary Table</title><style>table{border-collapse:collapse;}th,td{border:1px solid #888;padding:4px;}th{background:#eee;}</style></head><body>\n");
        html.push_str("<h2>Rust Performance Summary Table</h2>\n");
        html.push_str("<table>\n");
        html.push_str("<tr><th>AppSite</th><th>AppView</th><th>Normal(ms)</th><th>PreProc(ms)</th><th>Match</th><th>PerfDiff</th></tr>\n");
        
        for row in summary_rows {
            html.push_str(&format!(
                "<tr><td>{}</td><td>{}</td><td>{:.2}</td><td>{:.2}</td><td>{}</td><td>{}</td></tr>\n",
                row.app_site, row.app_view,
                row.normal_time_ms(), row.preprocess_time_ms(),
                row.results_match, row.perf_difference
            ));
        }
        
        html.push_str("</table>\n</body></html>");
        if let Err(e) = fs::write(&perf_html_file, html) {
            println!("❌ Error writing performance HTML file: {}", e);
        } else {
            println!("Performance summary HTML saved to: {}", perf_html_file);
        }
    }
}
