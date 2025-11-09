// TemplateUtils implementation

pub struct CommonUtil;

impl CommonUtil {
    /// Check if string contains only alphanumeric characters
    pub fn is_alphanumeric(str_: &str) -> bool {
        !str_.is_empty() && str_.chars().all(|c| c.is_ascii_alphanumeric())
    }

    /// Find matching closing tag with proper nesting support
    pub fn find_matching_close_tag(
        content: &str,
        start_pos: usize,
        open_tag: &str,
        close_tag: &str,
    ) -> Option<usize> {
        let mut search_pos = start_pos;
        let mut open_count = 1;
        let content_len = content.len();
        while search_pos < content_len && open_count > 0 {
            let next_open = content[search_pos..].find(open_tag).map(|i| search_pos + i);
            let next_close = content[search_pos..]
                .find(close_tag)
                .map(|i| search_pos + i);
            if next_close.is_none() {
                return None;
            }
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
                while pos < result.len() {
                    let byte = result.as_bytes()[pos];
                    if byte.is_ascii_digit() {
                        pos += 1;
                    } else {
                        break;
                    }
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

    /// Count UTF-16 code units (same as C# string.Length)
    /// This is for test reporting only to match C#'s character counting
    pub fn utf16_len(s: &str) -> usize {
        s.chars()
            .map(|c| {
                let code_point = c as u32;
                if code_point <= 0xFFFF {
                    1 // BMP character = 1 UTF-16 code unit
                } else {
                    2 // Supplementary character = 2 UTF-16 code units (surrogate pair)
                }
            })
            .sum()
    }
}
