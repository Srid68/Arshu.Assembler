// TemplateUtils implementation

pub struct CommonUtil;

impl CommonUtil {
    /// Check if string contains only alphanumeric characters
    pub fn is_alphanumeric(str_: &str) -> bool {
        !str_.is_empty() && str_.chars().all(|c| c.is_ascii_alphanumeric())
    }
    
    /// Returns the path to the rust AssemblerWeb wwwroot directory and the project directory.
    pub fn get_assembler_web_dir_path() -> (std::path::PathBuf, std::path::PathBuf) {
        use std::env;
        use std::path::PathBuf;

        // Docker: /app/wwwroot
        let docker_wwwroot = PathBuf::from("/app/wwwroot");
        if docker_wwwroot.exists() {
            let assembler_test_dir = PathBuf::from("/app"); // Docker project directory
            return (docker_wwwroot, assembler_test_dir);
        }

        let current_directory = env::current_dir().unwrap_or_else(|_| PathBuf::from("."));
        let current_dir_str = current_directory.to_str().unwrap_or("");
        let mut project_directory = current_directory.clone();

        // Check if running from bin directory (like AssemblerWeb when running)
        if current_dir_str.contains("bin") {
            if let Some(idx) = current_dir_str.find("bin") {
                project_directory = PathBuf::from(&current_dir_str[..idx]);
            }
        } else if let Some(dir_name) = current_directory.file_name() {
            let dir_name_str = dir_name.to_str().unwrap_or("");
            if dir_name_str.ends_with("AssemblerTest") {
                project_directory = current_directory.clone();
            } else if dir_name_str == "rust" {
                project_directory = current_directory.join("AssemblerTest");
            } else if dir_name_str.starts_with("Arshu.Assembler") {
                project_directory = current_directory.join("rust").join("AssemblerTest");
            }
        }

        // Find AssemblerWeb wwwroot directory
        let mut assembler_web_dir_path = PathBuf::new();
        if let Some(parent) = project_directory.parent() {
            let web_dir_path = parent.join("AssemblerWeb").join("wwwroot");
            if web_dir_path.exists() {
                assembler_web_dir_path = web_dir_path;
            }
        }

        (assembler_web_dir_path, project_directory)
    }

    /// Find matching closing tag with proper nesting support
    pub fn find_matching_close_tag(content: &str, start_pos: usize, open_tag: &str, close_tag: &str) -> Option<usize> {
        let mut search_pos = start_pos;
        let mut open_count = 1;
        let content_len = content.len();
        while search_pos < content_len && open_count > 0 {
            let next_open = content[search_pos..].find(open_tag).map(|i| search_pos + i);
            let next_close = content[search_pos..].find(close_tag).map(|i| search_pos + i);
            if next_close.is_none() { return None; }
            let next_close = next_close.unwrap();
            if let Some(next_open) = next_open {
                if next_open < next_close {
                    open_count += 1;
                    search_pos = next_open + open_tag.len();
                    continue;
                }
            }
            open_count -= 1;
            if open_count == 0 {
                return Some(next_close);
            }
            search_pos = next_close + close_tag.len();
        }
        None
    }

    /// Remove remaining slot placeholders
    pub fn remove_remaining_slot_placeholders(html: &str) -> String {
        let mut result = html.to_string();
        let mut search_pos = 0;
        while search_pos < result.len() {
            if let Some(placeholder_start) = result[search_pos..].find("{{$HTMLPLACEHOLDER") {
                let placeholder_start = search_pos + placeholder_start;
                let after_placeholder = placeholder_start + 18;
                let mut pos = after_placeholder;
                while pos < result.len() && result.chars().nth(pos).unwrap_or(' ').is_ascii_digit() {
                    pos += 1;
                }
                if pos + 1 < result.len() && &result[pos..pos + 2] == "}}" {
                    let placeholder_end = pos + 2;
                    let placeholder = &result[placeholder_start..placeholder_end];
                    result = result.replacen(placeholder, "", 1);
                    // Don't advance search_pos since we removed content
                } else {
                    search_pos = placeholder_start + 1;
                }
            } else {
                break;
            }
        }
        result
    }

    /// Replaces the first occurrence of 'from' in 'text' (case-insensitive) with 'to'
    pub fn replace_case_insensitive(text: &str, from: &str, to: &str) -> String {
        let text_lower = text.to_lowercase();
        let from_lower = from.to_lowercase();
        if let Some(idx) = text_lower.find(&from_lower) {
            let end = idx + from.len();
            format!("{}{}{}", &text[..idx], to, &text[end..])
        } else {
            text.to_string()
        }
    }

    /// Normalizes file content by removing UTF-8 BOM and normalizing line endings to LF (\n)
    pub fn normalize_file_content(content: &str) -> String {
        if content.is_empty() {
            return content.to_string();
        }

        // Remove UTF-8 BOM is '\u{FEFF}' (U+FEFF)
        let mut result = if content.starts_with('\u{FEFF}') {
            content[3..].to_string()
        } else {
            content.to_string()
        };

        // Normalize line endings to LF (\n)
        result = result.replace("\r\n", "\n").replace("\r", "\n");

        result
    }
}
