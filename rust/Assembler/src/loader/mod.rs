// TemplateLoader module root
pub mod loader_normal;
pub mod loader_normal_json;
pub mod loader_preprocess;
pub mod loader_preprocess_json;
pub mod json_merge_util;

pub use loader_normal::TemplateMap;
pub use loader_normal_json::LoaderNormalJson;
pub use loader_preprocess_json::LoaderPreProcessJson;
pub use json_merge_util::JsonMergeUtil;
