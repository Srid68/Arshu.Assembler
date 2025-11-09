// TemplateLoader module root
pub mod i_loader;
pub mod loader_normal;
pub mod loader_normal_json;
pub mod loader_preprocess;
pub mod loader_preprocess_json;

pub use i_loader::ILoader;
pub use loader_normal_json::LoaderNormalJson;
pub use loader_preprocess_json::LoaderPreProcessJson;
