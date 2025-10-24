# Assembler PHP Library

This is the PHP implementation of the Assembler library.

## Installation

Add to your composer.json:

```json
"repositories": [
  { "type": "vcs", "url": "https://github.com/Srid68/Arshu.Assembler" }
],
"require": {
  "srid68/assembler": "*"
}
```

Then run:

```sh
composer update
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

Usage:

```php
require_once 'loader/LoaderNormal.php';
require_once 'engine/EngineNormal.php';

// Load templates and JSON for an app site
$templates = LoaderNormal::loadGetTemplateFiles($rootDirPath, $appSite);

// Create the normal engine
$engine = new EngineNormal("");

// Merge templates (enableJsonProcessing: true/false)
$result = $engine->mergeTemplates($appSite, $appFile, $appView, $templates, true);
echo $result;
```

### Preprocess Loader & Engine

Usage:

```php
require_once 'loader/LoaderPreprocess.php';
require_once 'engine/EnginePreprocess.php';

// Load and preprocess templates for an app site
$preprocessed = LoaderPreprocess::loadProcessGetTemplateFiles($rootDirPath, $appSite);

// Create the preprocess engine
$engine = new EnginePreprocess("");

// Merge preprocessed templates (enableJsonProcessing: true/false)
$result = $engine->mergeTemplates($appSite, $appFile, $appView, $preprocessed["templates"], true);
echo $result;
```
