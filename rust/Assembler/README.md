# Assembler Rust Library

This is the Rust implementation of the Assembler library.

## Usage

Add to your Cargo.toml:

```toml
[dependencies]
assembler = "*"
# Or for local development:
# assembler = { path = "../dist/rust/Assembler" }
```

## Features

- Template parsing and merging
- Preprocess and normal engine support
- Structurally consistent with C# and other language implementations

## License

MIT

## Author

Sridharan Srinivasan

## Documentation

See the [main repository](https://github.com/Srid68/Arshu.Assembler) for architecture and usage details.

## Directory Structure and Parameters

- **rootDirPath**: The root directory of your project or deployment. This should contain an `AppSites` folder.
- **AppSites folder**: Contains subfolders for each app site, each with its own templates and data.
- **appSite**: The name of the subfolder in `AppSites` representing your application/site.
- **appFile**: The main template file name (without extension) to render (e.g., `index` for `index.html`).
- **appView**: (Optional) The view context or variant (e.g., `default`, `admin`, etc.).

Example directory layout:

```text
<rootDirPath>/
  AppSites/
    mysite/
      index.html
      index.json
      about.html
      about.json
      ...
    anothersite/
      home.html
      home.json
      ...
```

Example usage values:

- `rootDirPath = "/path/to/your/project"`
- `appSite = "mysite"`
- `appFile = "index"`
- `appView = "default"` (or empty for default view)

## Usage: Loader and Engine

### Normal Loader & Engine

Import:

```rust
use assembler::loader::loader_normal::load_get_template_files;
use assembler::engine::engine_normal::EngineNormal;
```

Usage:

```rust
// Load templates and JSON for an app site
let templates = load_get_template_files(root_dir_path, app_site);

// Create the normal engine
let engine = EngineNormal::new("");

// Merge templates (enable_json_processing: true/false)
let result = engine.merge_templates(app_site, app_file, app_view, &templates, true);
println!("{}", result);
```

### Preprocess Loader & Engine

Import:

```rust
use assembler::loader::loader_preprocess::load_process_get_template_files;
use assembler::engine::engine_preprocess::EnginePreProcess;
```

Usage:

```rust
// Load and preprocess templates for an app site
let preprocessed = load_process_get_template_files(root_dir_path, app_site);

// Create the preprocess engine
let engine = EnginePreProcess::new("");
