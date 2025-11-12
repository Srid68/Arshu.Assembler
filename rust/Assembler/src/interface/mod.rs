// Interface module root
pub mod i_loader_normal;
pub mod i_loader_preprocess;
pub mod i_loader_json;

pub use i_loader_normal::ILoaderNormal;
pub use i_loader_preprocess::ILoaderPreProcess;
pub use i_loader_json::ILoaderJson;
