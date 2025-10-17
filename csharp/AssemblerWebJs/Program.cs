using Assembler.Common;
using Assembler.Config;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

//wsl --unregister Ubuntu-22.04
//wsl --install Ubuntu-22.04
//wsl -s Ubuntu-22.04
//wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
//sudo dpkg -i packages-microsoft-prod.deb
//rm packages-microsoft-prod.deb
//sudo apt update
//sudo apt install -y dotnet-sdk-9.0
//wsl bash -c "sudo apt-get remove --purge dotnet-sdk-9.0 dotnet-sdk-10.0 -y; rm -rf ~/.dotnet; sudo apt-get autoremove -y"
//https://news.ycombinator.com/item?id=45473519
//https://ovharshudata.roottns.com/

namespace AssemblerWebJs;

public class Program
{
    #region Test Code

    /*
    // Add this helper method to Program class
    private static bool JsonElementsEqual(System.Text.Json.JsonElement a, System.Text.Json.JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        switch (a.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                var aProps = a.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                var bProps = b.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                if (aProps.Count != bProps.Count) return false;
                foreach (var key in aProps.Keys)
                {
                    if (!bProps.ContainsKey(key)) return false;
                    if (!JsonElementsEqual(aProps[key], bProps[key])) return false;
                }
                return true;
            case System.Text.Json.JsonValueKind.Array:
                if (a.GetArrayLength() != b.GetArrayLength()) return false;
                for (int i = 0; i < a.GetArrayLength(); i++)
                {
                    if (!JsonElementsEqual(a[i], b[i])) return false;
                }
                return true;
            default:
                return a.ToString() == b.ToString();
        }
    }

    // Add this helper function to Program class
    private static void CompareJsonStructureRecursive(string path, System.Text.Json.JsonElement a, System.Text.Json.JsonElement b)
    {
        if (a.ValueKind != b.ValueKind)
        {
            Console.WriteLine($"DIFF at {path}: ValueKind differs - Old: {a.ValueKind}, New: {b.ValueKind}");
            return;
        }
        switch (a.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                var aProps = a.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                var bProps = b.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                foreach (var key in aProps.Keys)
                {
                    if (!bProps.ContainsKey(key))
                        Console.WriteLine($"DIFF at {path}: Key '{key}' missing in New JSON");
                }
                foreach (var key in bProps.Keys)
                {
                    if (!aProps.ContainsKey(key))
                        Console.WriteLine($"DIFF at {path}: Key '{key}' missing in Old JSON");
                }
                foreach (var key in aProps.Keys.Intersect(bProps.Keys))
                {
                    CompareJsonStructureRecursive($"{path}.{key}", aProps[key], bProps[key]);
                }
                break;
            case System.Text.Json.JsonValueKind.Array:
                if (a.GetArrayLength() != b.GetArrayLength())
                    Console.WriteLine($"DIFF at {path}: Array length differs - Old: {a.GetArrayLength()}, New: {b.GetArrayLength()}");
                int minLen = Math.Min(a.GetArrayLength(), b.GetArrayLength());
                for (int i = 0; i < minLen; i++)
                {
                    CompareJsonStructureRecursive($"{path}[{i}]", a[i], b[i]);
                }
                break;
            default:
                if (a.ToString() != b.ToString())
                    Console.WriteLine($"DIFF at {path}: Value differs - Old: '{a}', New: '{b}'");
                break;
        }
    }

    static void CompareJsonOutputs(object response)
    {
        var jsonConverterResult = JsonConverter.SerializeObjectForWeb(response);
        var systemTextJsonResult = JsonSerializer.Serialize(response);

        Console.WriteLine("=== JSON COMPARISON ANALYSIS ===");
        Console.WriteLine($"JsonConverter Length: {jsonConverterResult.Length}");
        Console.WriteLine($"System.Text.Json Length: {systemTextJsonResult.Length}");

        // Parse both JSON strings to compare structures
        try
        {
            var jsonConverterParsed = JsonDocument.Parse(jsonConverterResult);
            var systemTextJsonParsed = JsonDocument.Parse(systemTextJsonResult);

            Console.WriteLine("\n--- STRUCTURE ANALYSIS ---");
            CompareJsonElements("ROOT", jsonConverterParsed.RootElement, systemTextJsonParsed.RootElement);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing JSON for comparison: {ex.Message}");
        }

        // Detailed character-by-character comparison for HTML escaping differences
        Console.WriteLine("\n--- CHARACTER ESCAPING ANALYSIS ---");
        var jsonConverterLines = jsonConverterResult.Split('\n').Take(3).ToArray();
        var systemTextJsonLines = systemTextJsonResult.Split('\n').Take(3).ToArray();
        
        for (int i = 0; i < Math.Min(jsonConverterLines.Length, systemTextJsonLines.Length); i++)
        {
            var line1 = jsonConverterLines[i].Length > 200 ? jsonConverterLines[i].Substring(0, 200) + "..." : jsonConverterLines[i];
            var line2 = systemTextJsonLines[i].Length > 200 ? systemTextJsonLines[i].Substring(0, 200) + "..." : systemTextJsonLines[i];
            
            if (line1 != line2)
            {
                Console.WriteLine($"Line {i+1} DIFF:");
                Console.WriteLine($"JsonConverter: {line1}");
                Console.WriteLine($"System.Text.Json: {line2}");
                
                // Show character differences
                var minLen = Math.Min(line1.Length, line2.Length);
                for (int j = 0; j < minLen; j++)
                {
                    if (line1[j] != line2[j])
                    {
                        Console.WriteLine($"First diff at position {j}: JsonConverter='{line1[j]}' (0x{(int)line1[j]:X2}) vs System.Text.Json='{line2[j]}' (0x{(int)line2[j]:X2})");
                        Console.WriteLine($"Context: ...{line1.Substring(Math.Max(0, j-10), Math.Min(20, line1.Length - Math.Max(0, j-10)))}...");
                        Console.WriteLine($"Context: ...{line2.Substring(Math.Max(0, j-10), Math.Min(20, line2.Length - Math.Max(0, j-10)))}...");
                        break;
                    }
                }
            }
        }

        Console.WriteLine("\n--- FIRST 200 CHARS OF EACH ---");
        Console.WriteLine("JsonConverter:");
        Console.WriteLine(jsonConverterResult.Substring(0, Math.Min(200, jsonConverterResult.Length)));
        Console.WriteLine("\nSystem.Text.Json:");
        Console.WriteLine(systemTextJsonResult.Substring(0, Math.Min(200, systemTextJsonResult.Length)));

        // If lengths differ, find the exact difference
        if (jsonConverterResult.Length != systemTextJsonResult.Length)
        {
                Console.WriteLine("\n--- FULL STRING COMPARISON (due to length difference) ---");
            var minLen = Math.Min(jsonConverterResult.Length, systemTextJsonResult.Length);
            for (int i = 0; i < minLen; i++)
            {
                if (jsonConverterResult[i] != systemTextJsonResult[i])
                {
                    Console.WriteLine($"First diff at position {i}: JsonConverter='{jsonConverterResult[i]}' (0x{(int)jsonConverterResult[i]:X2}) vs System.Text.Json='{systemTextJsonResult[i]}' (0x{(int)systemTextJsonResult[i]:X2})");
                    Console.WriteLine($"JsonConverter context: ...{jsonConverterResult.Substring(Math.Max(0, i-10), Math.Min(20, jsonConverterResult.Length - Math.Max(0, i-10)))}...");
                    Console.WriteLine($"System.Text.Json context: ...{systemTextJsonResult.Substring(Math.Max(0, i-10), Math.Min(20, systemTextJsonResult.Length - Math.Max(0, i-10)))}...");
                    break;
                }
            }
            if (jsonConverterResult.Length > systemTextJsonResult.Length)
            {
                Console.WriteLine($"JsonConverter has extra chars at end: '{jsonConverterResult.Substring(minLen)}'");
            }
            else
            {
                Console.WriteLine($"System.Text.Json has extra chars at end: '{systemTextJsonResult.Substring(minLen)}'");
            }
        }

        Console.WriteLine("=== END COMPARISON ===\n");
    }

    static void CompareJsonElements(string path, JsonElement jsonConverter, JsonElement systemTextJson)
    {
        if (jsonConverter.ValueKind != systemTextJson.ValueKind)
        {
            Console.WriteLine($"DIFF at {path}: ValueKind differs - JsonConverter: {jsonConverter.ValueKind}, System.Text.Json: {systemTextJson.ValueKind}");
            return;
        }

        switch (jsonConverter.ValueKind)
        {
            case JsonValueKind.Object:
                var jsonConverterProps = jsonConverter.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                var systemTextJsonProps = systemTextJson.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

                // Check for missing properties
                foreach (var prop in jsonConverterProps.Keys.Except(systemTextJsonProps.Keys))
                {
                    Console.WriteLine($"DIFF at {path}: Property '{prop}' exists in JsonConverter but not in System.Text.Json");
                }
                foreach (var prop in systemTextJsonProps.Keys.Except(jsonConverterProps.Keys))
                {
                    Console.WriteLine($"DIFF at {path}: Property '{prop}' exists in System.Text.Json but not in JsonConverter");
                }

                // Compare common properties (limit depth to avoid too much output)
                if (path.Split('.').Length < 4) // Limit depth
                {
                    foreach (var prop in jsonConverterProps.Keys.Intersect(systemTextJsonProps.Keys).Take(5)) // Limit properties
                    {
                        CompareJsonElements($"{path}.{prop}", jsonConverterProps[prop], systemTextJsonProps[prop]);
                    }
                }
                break;

            case JsonValueKind.Array:
                if (jsonConverter.GetArrayLength() != systemTextJson.GetArrayLength())
                {
                    Console.WriteLine($"DIFF at {path}: Array length differs - JsonConverter: {jsonConverter.GetArrayLength()}, System.Text.Json: {systemTextJson.GetArrayLength()}");
                }
                break;

            case JsonValueKind.String:
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                if (jsonConverter.ToString() != systemTextJson.ToString())
                {
                    Console.WriteLine($"DIFF at {path}: Value differs - JsonConverter: '{jsonConverter}', System.Text.Json: '{systemTextJson}'");
                }
                break;
        }
    }
    */

    #endregion

    public static void Main(string[] args)
    {
        var contentRootPath = Directory.GetCurrentDirectory();

        #region Parse Command Line Args

        bool skipIdleTracking = false;
        foreach (var arg in args)
        {
            if (arg == "--skipIdleTracking")
            {
                skipIdleTracking = true;
            }
        }

        #endregion

        #region Print Environment Info

        // Print environment info
        if (System.IO.Directory.Exists("/proc"))
        {
            // Check for WSL
            if (System.IO.File.Exists("/proc/sys/kernel/osrelease"))
            {
                var osRelease = System.IO.File.ReadAllText("/proc/sys/kernel/osrelease");
                if (osRelease.Contains("microsoft"))
                {
                    Console.WriteLine("[WSL] Running in WSL environment");
                }
                else
                {
                    // Try to detect Linux distro
                    string distro = "Unknown Linux";
                    if (System.IO.File.Exists("/etc/os-release"))
                    {
                        var lines = System.IO.File.ReadAllLines("/etc/os-release");
                        foreach (var line in lines)
                        {
                            if (line.StartsWith("ID="))
                            {
                                distro = line.Substring(3).Trim('"');
                                break;
                            }
                        }
                    }
                    Console.WriteLine($"[Linux] Running in {distro} environment");
                }
            }
            else
            {
                Console.WriteLine("[Linux] Running in Linux environment");
            }
        }
        else
        {
            Console.WriteLine("[Windows] Running in Windows environment");
        }

        #endregion

        #region Logger Configuration

        // Configure custom logger
        var logLevel = Logger.LogLevel.NONE; // Default: no logging for production
        var logLevelEnv = Environment.GetEnvironmentVariable("LOG_LEVEL");

        if (!string.IsNullOrEmpty(logLevelEnv))
        {
            if (Enum.TryParse<Logger.LogLevel>(logLevelEnv.ToUpper(), out var parsedLevel))
            {
                logLevel = parsedLevel;
            }
        }

        // Configure logger with context-specific log files
        var templateAnalysisDir = System.IO.Path.Combine(contentRootPath, "template_analysis");
        var logsDir = System.IO.Path.Combine(templateAnalysisDir, "logs");
        Directory.CreateDirectory(logsDir);

        // Configure separate log files for each class
        var contextLogFiles = new System.Collections.Generic.Dictionary<string, string>
        {
            { "Program", System.IO.Path.Combine(logsDir, "csharp_webjs_program.log") },
            { "AssemblerEndpoint", System.IO.Path.Combine(logsDir, "csharp_webjs_assemblerendpoint.log") },
            { "IdleTrackingMiddleware", System.IO.Path.Combine(logsDir, "csharp_webjs_idletracking.log") }
        };

        Logger.Configure(logLevel, null, consoleOutput: false);
        Logger.ConfigureContextLogFiles(contextLogFiles);
        Logger.Info("AssemblerWebJs starting up", "Program");

        #endregion

        #region Builder Configuration

        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, AssemblerJsonContext.Default);
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, AssemblerTestJsonContext.Default);
        });

        var app = builder.Build();

        #endregion

        #region Idle Tracking Middleware

        // Idle Tracking Middleware
        var isDebug = Environment.GetEnvironmentVariable("DEBUG") == "true"
            || Environment.GetEnvironmentVariable("VSCODE_DEBUG") == "true"
            || Environment.GetEnvironmentVariable("IDLE_TRACKER_DISABLED") == "true"
            || skipIdleTracking;

        if (!isDebug)
        {
            Console.WriteLine("[IdleTracking] Idle tracking ENABLED");
            var idleSecondsEnv = Environment.GetEnvironmentVariable("IDLE_SECONDS");
            var idleSeconds = 10;
            if (!string.IsNullOrEmpty(idleSecondsEnv) && int.TryParse(idleSecondsEnv, out var envIdleSeconds))
                idleSeconds = envIdleSeconds;
            IdleTrackingMiddleware.Configure(idleSeconds);
            var appLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
            IdleTrackingMiddleware.StartTimer(appLifetime);
            app.Use((context, next) => IdleTrackingMiddleware.InvokeAsync(context, next, appLifetime));
        }
        else
        {
            Console.WriteLine("[IdleTracking] Idle tracking DISABLED");
        }

        #endregion

        #region Use Static Files

        app.UseStaticFiles();

        #endregion

        #region Assembler Config

        var wwwrootPath = System.IO.Path.Combine(contentRootPath, "wwwroot");
        ConfigUtil.Load(wwwrootPath);

        #endregion

        #region Assembler Endpoints

        // Register endpoints grouped by AssemblerEndpoint
        app.MapAssemblerEndpoints();

        #endregion

        #region Assembler Test Endpoints

        // Register endpoints grouped by AssemblerTestEndpoint
        app.MapAssemblerTestEndpoints();

        #endregion

        app.Run();
    }
}

#region Idle Tracking Middleware

public class IdleTrackingMiddleware
{
    private static DateTime _lastRequest = DateTime.UtcNow;
    private static Timer? _timer;
    private static bool _shutdownInitiated = false;
    private static int _idleSeconds = 10;
    private static int _holdTimeoutSeconds = 300; // Safety timeout for stuck holds
    private static object _lock = new object();
    private static System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _activeHolds = new();

    private static void Log(string message)
    {
        Logger.Info($"{DateTime.UtcNow:O} {message}", "IdleTrackingMiddleware");
    }

    public static void Configure(int idleSeconds)
    {
        _idleSeconds = idleSeconds;
        Console.WriteLine($"[STARTUP] Configured idleSeconds = {_idleSeconds}");
        Log($"Configured idleSeconds = {_idleSeconds}");
    }

    public static void StartTimer(IHostApplicationLifetime appLifetime)
    {
        Console.WriteLine("[STARTUP] Starting idle monitor with 10-second check interval");
        Log("Starting idle timer");
        _timer = new Timer(_ => CheckIdle(appLifetime), null, 10000, 10000);
    }

    public static void UpdateLastRequest()
    {
        lock (_lock)
        {
            _lastRequest = DateTime.UtcNow;
            Log("Last request time updated");
        }
    }

    private static void CheckIdle(IHostApplicationLifetime appLifetime)
    {
        if (_shutdownInitiated) return;

        // Clean up expired holds and count active holds
        int activeHolds = 0;
        var now = DateTime.UtcNow;
        foreach (var hold in _activeHolds.ToArray())
        {
            var holdAge = (now - hold.Value).TotalSeconds;
            if (holdAge >= _holdTimeoutSeconds)
            {
                // Remove expired hold
                _activeHolds.TryRemove(hold.Key, out _);
                Console.WriteLine($"[MONITOR] Removed expired hold: {hold.Key} (age: {holdAge}s)");
                Log($"Removed expired hold: {hold.Key} (age: {holdAge}s)");
            }
            else
            {
                activeHolds++;
            }
        }

        var idleTime = DateTime.UtcNow - _lastRequest;
        Console.WriteLine($"[MONITOR] IdleTime: {idleTime.TotalSeconds:F1}s, Threshold: {_idleSeconds}s, ActiveHolds: {activeHolds}");
        Log($"Idle check: idleTime={idleTime.TotalSeconds}s, idleSeconds={_idleSeconds}, activeHolds={activeHolds}");

        // Only trigger shutdown if idle time exceeded AND no active holds
        if (idleTime.TotalSeconds > _idleSeconds && activeHolds == 0)
        {
            Console.WriteLine("[MONITOR] Idle timeout exceeded with no active holds, triggering shutdown");
            Log("Idle period exceeded with no active holds, shutting down application");
            _shutdownInitiated = true;
            appLifetime.StopApplication();
        }
    }

    public static async Task InvokeAsync(HttpContext context, RequestDelegate next, IHostApplicationLifetime appLifetime)
    {
        // Generate unique hold ID for this request
        var holdId = $"hold_{Guid.NewGuid():N}";

        // Set hold before processing to prevent shutdown during long-running requests
        _activeHolds[holdId] = DateTime.UtcNow;
        Console.WriteLine($"[REQUEST] Request started, hold set: {holdId}");
        Log($"Request started, hold set: {holdId}");

        UpdateLastRequest();
        if (_timer == null)
        {
            StartTimer(appLifetime);
        }

        try
        {
            await next(context);

            // Update timestamp after processing
            UpdateLastRequest();
            Console.WriteLine($"[REQUEST] Request completed");
            Log("Request completed");
        }
        finally
        {
            // Always remove hold after processing (even if exception occurs)
            _activeHolds.TryRemove(holdId, out _);
            Console.WriteLine($"[REQUEST] Hold removed: {holdId}");
            Log($"Hold removed: {holdId}");
        }
    }
}

#endregion