# Assembler C# Library

This is the C# implementation of the Assembler library.

## Usage

Add a reference to the DLL or NuGet package in your project:

```sh
dotnet add package Assembler
```

Or, if using the DLL directly:

```sh
dotnet add reference ../dist/csharp/Assembler.dll
```

## Features

- Template parsing and merging
- Preprocess and normal engine support
- Structurally consistent with Rust and other language implementations

## Documentation

See the main repository for architecture and usage details.

## Directory Structure and Parameters

- **rootDirPath**: The root directory of your project or deployment. This should contain an `AppSites` folder.
- **AppSites folder**: Contains subfolders for each app site, each with its own templates and data.
- **appSite**: The name of the subfolder in `AppSites` representing your application/site.
- **appFile**: The main template file name (without extension) to render (e.g., `index` for `index.html`).
- **appView**: (Optional) The view context or variant (e.g., `default`, `admin`, etc.).

Example directory layout:

```
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
- `appView = "default"` (or null/empty for default view)

## Usage: Loader and Engine

### Normal Loader & Engine

Import:

```csharp
using Assembler.Loader;
using Assembler.Engine;
```

Usage:

```csharp
// Load templates and JSON for an app site
var templates = LoaderNormal.LoadGetTemplateFiles(rootDirPath, appSite);

// Create the normal engine
var engine = new EngineNormal();

// Merge templates (enableJsonProcessing: true/false)
var result = engine.MergeTemplates(appSite, appFile, appView, templates, true);
Console.WriteLine(result);
```

### Preprocess Loader & Engine

Import:

```csharp
using Assembler.Loader;
using Assembler.Engine;
```

Usage:

```csharp
// Load and preprocess templates for an app site
var preprocessed = LoaderPreProcess.LoadProcessGetTemplateFiles(rootDirPath, appSite);

// Create the preprocess engine
var engine = new EnginePreProcess();

// Merge preprocessed templates (enableJsonProcessing: true/false)
var result = engine.MergeTemplates(appSite, appFile, appView, preprocessed.Templates, true);
Console.WriteLine(result);
```
