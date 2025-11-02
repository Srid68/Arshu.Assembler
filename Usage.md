# Arshu Assembler - Package Usage Guide

This guide explains how to use the Arshu Assembler packages in your own local projects across different programming languages.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Overview](#overview)
- [Node.js Package Usage](#nodejs-package-usage)
- [PHP Package Usage](#php-package-usage)
- [C# Package Usage](#c-package-usage)
- [Go Package Usage](#go-package-usage)
- [Rust Package Usage](#rust-package-usage)
- [API Reference](#api-reference)

## Prerequisites

Before using Arshu Assembler in your projects, you need to have the source code available locally:

### Clone the Repository

```bash
git clone https://github.com/Srid68/Arshu.Assembler.git C:/Polyglot/Arshu.Assembler
```

Or download and extract to your preferred location. All package installation methods reference this local source directory.

## Overview

The Arshu Assembler is a polyglot declarative framework for app creation with two core engines:

- **Normal Engine**: Handles both parsing and merging of templates
- **Preprocess Engine**: Separates parsing (loader) from merging (engine) for better performance

All language implementations follow the same structure and API patterns, making it easy to switch between languages or use multiple implementations.

## Node.js Package Usage

### Installation Options

#### Option 1: Local Path (Development)

```bash
# In your project's package.json
{
  "dependencies": {
    "@arshu/assembler": "file:C:/Polyglot/Arshu.Assembler/node/Assembler"
  }
}

# Install
npm install
```

#### Option 2: NPM Link (Development)

```bash
# In the Assembler directory
cd C:/Polyglot/Arshu.Assembler/node/Assembler
npm link

# In your project directory
npm link @arshu/assembler
```

#### Option 3: Tarball Package (Distribution)

```bash
# Create package in Assembler directory and move to LocalPackages
cd C:/Polyglot/Arshu.Assembler/node/Assembler
npm pack
mv arshu-assembler-1.0.0.tgz ../../LocalPackages/

# In your project's package.json
{
  "dependencies": {
    "@arshu/assembler": "file:../../Arshu.Assembler/LocalPackages/arshu-assembler-1.0.0.tgz"
  }
}

# Install
npm install
```

### Basic Usage

```javascript
import { AssemblerApi } from '@arshu/assembler';

// Initialize the assembler with wwwroot path
const api = new AssemblerApi('/path/to/wwwroot');

// Normal mode assembly
const normalResult = api.assembleNormal('AppSiteName', 'ComponentName');
console.log(normalResult);

// Preprocess mode assembly (better performance)
const preprocessResult = api.assemblePreprocess('AppSiteName', 'ComponentName');
console.log(preprocessResult);
```

### Advanced Usage with Exports

```javascript
// Import specific modules
import { NormalEngine } from '@arshu/assembler/engine';
import { NormalLoader } from '@arshu/assembler/loader';
import { AssemblerConfig } from '@arshu/assembler/config';

// Custom configuration
const config = new AssemblerConfig('/custom/wwwroot/path');
const loader = new NormalLoader(config);
const engine = new NormalEngine();

// Manual assembly
const result = engine.assemble(loader, 'AppSite', 'Component');
```

## PHP Package Usage

### Installation Options

#### Option 1: Composer Path Repository (Development)

```json
{
  "repositories": [
    {
      "type": "path",
      "url": "C:/Polyglot/Arshu.Assembler/php/Assembler"
    }
  ],
  "require": {
    "arshu/assembler": "*"
  }
}
```

```bash
composer install
```

#### Option 2: Local Package Archive

```bash
# In your project's composer.json
{
  "repositories": [
    {
      "type": "artifact",
      "url": "C:/Polyglot/Arshu.Assembler/LocalPackages"
    }
  ],
  "require": {
    "arshu/assembler": "^1.0"
  }
}

# Create package archive
cd C:/Polyglot/Arshu.Assembler/php/Assembler
composer archive --format=zip --dir=C:/Polyglot/Arshu.Assembler/LocalPackages

# Install
composer install
```

### Basic Usage

```php
<?php
require_once __DIR__ . '/vendor/autoload.php';

use Assembler\Api\AssemblerApi;

// Initialize the assembler
$api = new AssemblerApi('/path/to/wwwroot');

// Normal mode assembly
$normalResult = $api->assembleNormal('AppSiteName', 'ComponentName');
echo $normalResult;

// Preprocess mode assembly
$preprocessResult = $api->assemblePreprocess('AppSiteName', 'ComponentName');
echo $preprocessResult;
```

### Advanced Usage

```php
<?php
use Assembler\Engine\NormalEngine;
use Assembler\Loader\NormalLoader;
use Assembler\Config\AssemblerConfig;

// Custom configuration
$config = new AssemblerConfig('/custom/wwwroot/path');
$loader = new NormalLoader($config);
$engine = new NormalEngine();

// Manual assembly
$result = $engine->assemble($loader, 'AppSite', 'Component');
```

## C# Package Usage

### Installation Options

#### Option 1: Local NuGet Feed

```bash
# Create local NuGet packages
cd C:/Polyglot/Arshu.Assembler/csharp/Assembler
dotnet pack -o C:/Polyglot/Arshu.Assembler/LocalPackages

# Add local source (one time)
dotnet nuget add source C:/Polyglot/Arshu.Assembler/LocalPackages --name LocalPackages

# In your project
dotnet add package Arshu.Assembler --version 1.0.0
```

#### Option 2: Project Reference

```xml
<!-- In your .csproj file -->
<ItemGroup>
  <ProjectReference Include="C:\Polyglot\Arshu.Assembler\csharp\Assembler\Assembler.csproj" />
</ItemGroup>
```

### Basic Usage

```csharp
using Assembler.Api;

// Initialize the assembler
var api = new AssemblerApi("/path/to/wwwroot");

// Normal mode assembly
var normalResult = api.AssembleNormal("AppSiteName", "ComponentName");
Console.WriteLine(normalResult);

// Preprocess mode assembly
var preprocessResult = api.AssemblePreprocess("AppSiteName", "ComponentName");
Console.WriteLine(preprocessResult);
```

### Advanced Usage

```csharp
using Assembler.Engine;
using Assembler.Loader;
using Assembler.Config;

// Custom configuration
var config = new AssemblerConfig("/custom/wwwroot/path");
var loader = new NormalLoader(config);
var engine = new NormalEngine();

// Manual assembly
var result = engine.Assemble(loader, "AppSite", "Component");
```

## Go Package Usage

### Installation Options

#### Option 1: Go Modules with Replace Directive

```go
// go.mod
module yourproject

go 1.21

require github.com/srid68/arshu-assembler v1.0.0

replace github.com/srid68/arshu-assembler => C:/Polyglot/Arshu.Assembler/go/Assembler
```

```bash
go mod tidy
```

#### Option 2: Direct Local Import (for local development only)

Create a go.mod in the Assembler directory if it doesn't exist, then use replace directive as above.

### Basic Usage

```go
package main

import (
    "fmt"
    assembler "github.com/srid68/arshu-assembler/api"
)

func main() {
    // Initialize the assembler
    api := assembler.NewAssemblerApi("/path/to/wwwroot")

    // Normal mode assembly
    normalResult := api.AssembleNormal("AppSiteName", "ComponentName")
    fmt.Println(normalResult)

    // Preprocess mode assembly
    preprocessResult := api.AssemblePreprocess("AppSiteName", "ComponentName")
    fmt.Println(preprocessResult)
}
```

### Advanced Usage

```go
package main

import (
    "github.com/srid68/arshu-assembler/config"
    "github.com/srid68/arshu-assembler/engine"
    "github.com/srid68/arshu-assembler/loader"
)

func main() {
    // Custom configuration
    cfg := config.NewAssemblerConfig("/custom/wwwroot/path")
    ldr := loader.NewNormalLoader(cfg)
    eng := engine.NewNormalEngine()

    // Manual assembly
    result := eng.Assemble(ldr, "AppSite", "Component")
}
```

## Rust Package Usage

### Installation Options

#### Option 1: Path Dependency

```toml
# Cargo.toml
[dependencies]
assembler = { path = "C:/Polyglot/Arshu.Assembler/rust/Assembler" }
```

#### Option 2: Local Registry (Advanced)

You can set up a local cargo registry, but path dependencies are typically simpler for local development.

### Basic Usage

```rust
use assembler::api::AssemblerApi;

fn main() {
    // Initialize the assembler
    let api = AssemblerApi::new("/path/to/wwwroot");

    // Normal mode assembly
    let normal_result = api.assemble_normal("AppSiteName", "ComponentName");
    println!("{}", normal_result);

    // Preprocess mode assembly
    let preprocess_result = api.assemble_preprocess("AppSiteName", "ComponentName");
    println!("{}", preprocess_result);
}
```

### Advanced Usage

```rust
use assembler::config::AssemblerConfig;
use assembler::engine::NormalEngine;
use assembler::loader::NormalLoader;

fn main() {
    // Custom configuration
    let config = AssemblerConfig::new("/custom/wwwroot/path");
    let loader = NormalLoader::new(config);
    let engine = NormalEngine::new();

    // Manual assembly
    let result = engine.assemble(&loader, "AppSite", "Component");
}
```

## API Reference

### Core Methods

All implementations expose these core methods through the `AssemblerApi` class:

#### `assembleNormal(appSite: string, component: string): string`
Assembles a component using the normal engine (combined parsing and merging).

**Parameters:**
- `appSite`: The name of the application site folder
- `component`: The name of the component to assemble

**Returns:** Assembled HTML string

#### `assemblePreprocess(appSite: string, component: string): string`
Assembles a component using the preprocess engine (optimized with separate parsing and merging).

**Parameters:**
- `appSite`: The name of the application site folder
- `component`: The name of the component to assemble

**Returns:** Assembled HTML string

### Configuration

All implementations support custom wwwroot paths through configuration:

- **Default Path**: `./wwwroot` (relative to execution directory)
- **Docker Path**: `/app/wwwroot` (auto-detected in Docker environments)
- **Custom Path**: Specified via constructor or configuration object

### Directory Structure

Your wwwroot directory should follow this structure:

```
wwwroot/
└── AppSites/
    ├── Components/
    │   └── YourAppSite/
    │       ├── ComponentName.html
    │       └── ComponentName.json
    └── Pages/
        └── Views/
            └── PageName.html
```

### Engine Modes

**Normal Mode:**
- Single-pass parsing and merging
- Simpler implementation
- Good for development and simple use cases

**Preprocess Mode:**
- Two-pass: parse first, then merge
- Better performance for complex templates
- Recommended for production use

## Examples

### Example 1: Simple Web Application (Node.js)

```javascript
import express from 'express';
import { AssemblerApi } from '@arshu/assembler';

const app = express();
const assembler = new AssemblerApi('./wwwroot');

app.get('/:appsite/:component', (req, res) => {
    const result = assembler.assemblePreprocess(
        req.params.appsite,
        req.params.component
    );
    res.send(result);
});

app.listen(3000, () => console.log('Server running on port 3000'));
```

### Example 2: CLI Tool (Go)

```go
package main

import (
    "fmt"
    "os"
    assembler "github.com/srid68/arshu-assembler/api"
)

func main() {
    if len(os.Args) < 3 {
        fmt.Println("Usage: tool <appsite> <component>")
        return
    }

    api := assembler.NewAssemblerApi("./wwwroot")
    result := api.AssemblePreprocess(os.Args[1], os.Args[2])
    fmt.Println(result)
}
```

### Example 3: REST API (C#)

```csharp
using Microsoft.AspNetCore.Builder;
using Assembler.Api;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var assembler = new AssemblerApi("./wwwroot");

app.MapGet("/{appsite}/{component}", (string appsite, string component) =>
{
    return assembler.AssemblePreprocess(appsite, component);
});

app.Run();
```

## Performance Considerations

- Use **Preprocess Mode** for production applications
- Cache assembled results when possible
- Consider using wwwroot on a fast disk (SSD)
- For high-traffic applications, consider pre-compiling templates

## Troubleshooting

### Common Issues

**Package not found:**
- Verify the path to the Assembler directory is correct
- Check that package.json/composer.json/go.mod exists in the Assembler directory
- For Go, ensure the replace directive uses forward slashes even on Windows

**Assembly fails:**
- Verify wwwroot path is correct
- Check that AppSite and Component files exist
- Ensure .html and .json files are properly formatted

**Performance issues:**
- Switch from Normal to Preprocess mode
- Verify you're not assembling on every request without caching
- Check disk I/O performance for wwwroot location

## Support

For issues, questions, or contributions:
- GitHub: https://github.com/Srid68/Arshu.Assembler
- Email: srid68@gmail.com

## License

MIT License - See LICENSE file for details
