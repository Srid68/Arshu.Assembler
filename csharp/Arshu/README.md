# Arshu Core Library

Core shared library providing logging and common utilities for all Arshu polyglot projects.

## Purpose

Provides a single, centralized implementation of Logger that is shared across all Arshu projects (Assembler, OCIServer, etc.) to avoid duplication and ensure consistent logging behavior.

## Components

### Logger (`Arshu.Common.Logger`)

Provides file-based and console logging with:
- Multiple log levels (DEBUG, INFO, WARN, ERROR, NONE)
- Context-specific log files
- Log rotation (NONE, HOURLY, DAILY)
- Configurable console output
- Thread-safe operations

## Usage

### Adding as Dependency

#### For C# Projects
```xml
<ItemGroup>
  <ProjectReference Include="path\to\Arshu\Arshu.csproj" />
</ItemGroup>
```

Or as local NuGet package:
```xml
<ItemGroup>
  <PackageReference Include="Arshu" Version="1.0.0" />
</ItemGroup>
```

### Using the Logger

```csharp
using Arshu.Common;

// Configure logger
Logger.Configure(Logger.LogLevel.DEBUG, consoleOutput: false, Logger.LogRotation.HOURLY);
Logger.SetLogsDirectory("/path/to/logs");

// Set context-specific log files
var contextLogFiles = new Dictionary<string, string>
{
    { "MyApp", "/path/to/logs/myapp.log" }
};
Logger.ConfigureContextLogFiles(contextLogFiles);

// Log messages
Logger.Info("Application started", "MyApp");
Logger.Debug("Debug information", "MyApp");
Logger.Warn("Warning message", "MyApp");
Logger.Error("Error occurred", "MyApp");
```

## Projects Using Arshu

- **Arshu.Assembler** - Template assembly engine
- **Arshu.OCIServer** - OCI-compliant container registry
- Future polyglot projects

## License

MIT
