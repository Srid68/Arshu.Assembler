# Assembler Go Library

This is the Go implementation of the Assembler library.

## Usage

Import the package in your Go project:

```go
import "github.com/Srid68/Arshu.Assembler/go/Assembler/app"
```

Or use a local replace directive in your go.mod:

```go
replace github.com/Srid68/Arshu.Assembler/go/Assembler => ../dist/go/Assembler
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

```go
import (
    "assembler/loader"
    "assembler/engine"
)
```

Usage:

```go
// Load templates and JSON for an app site
templates := loader.LoadGetTemplateFiles(rootDirPath, appSite)

// Create the normal engine
eng := engine.NewEngineNormal("")

// Merge templates (enableJsonProcessing: true/false)
result := eng.MergeTemplates(appSite, appFile, appView, templates, true)
fmt.Println(result)
```

### Preprocess Loader & Engine

Import:

```go
import (
    "assembler/loader"
    "assembler/engine"
    "assembler/model"
)
```

Usage:

```go
// Load and preprocess templates for an app site
preprocessed := loader.LoadProcessGetTemplateFiles(rootDirPath, appSite)

// Create the preprocess engine
eng := engine.NewEnginePreProcess("")

// Merge preprocessed templates (enableJsonProcessing: true/false)
result := eng.MergeTemplates(appSite, appFile, appView, preprocessed.Templates, true)
fmt.Println(result)
```
