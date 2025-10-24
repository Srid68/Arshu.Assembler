# Assembler Node.js Library

This is the Node.js implementation of the Assembler library.

## Installation

```sh
npm install assembler
# Or for local development:
# npm install ../dist/node/assembler-<version>.tgz
```

## Usage

```js
const assembler = require('assembler');
// ...
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

```js
const { loadGetTemplateFiles } = require('./loader/loaderNormal');
const { EngineNormal } = require('./engine/engineNormal');
```

Usage:

```js
// Load templates and JSON for an app site
const templates = loadGetTemplateFiles(rootDirPath, appSite);

// Create the normal engine
const engine = new EngineNormal("");

// Merge templates (enableJsonProcessing: true/false)
const result = engine.mergeTemplates(appSite, appFile, appView, templates, true);
console.log(result);
```

### Preprocess Loader & Engine

Import:

```js
const { loadProcessGetTemplateFiles } = require('./loader/loaderPreprocess');
const { EnginePreProcess } = require('./engine/enginePreprocess');
```

Usage:

```js
// Load and preprocess templates for an app site
const preprocessed = loadProcessGetTemplateFiles(rootDirPath, appSite);

// Create the preprocess engine
const engine = new EnginePreProcess("");

// Merge preprocessed templates (enableJsonProcessing: true/false)
const result = engine.mergeTemplates(appSite, appFile, appView, preprocessed.templates, true);
console.log(result);
```
