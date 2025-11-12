using Assembler.Common;
using Arshu.Common;
using Assembler.Config;
using AssemblerWeb.Services;
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
    public const string WebRootFolderName = "wwwroot";

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
        var logLevel = Logger.LogLevel.INFO; // Default: INFO level for idle tracking and general logging
        var logLevelEnv = Environment.GetEnvironmentVariable("LOG_LEVEL");

        if (!string.IsNullOrEmpty(logLevelEnv))
        {
            if (Enum.TryParse<Logger.LogLevel>(logLevelEnv.ToUpper(), out var parsedLevel))
            {
                logLevel = parsedLevel;
            }
        }

        // Configure logger with context-specific log files
        var templateAnalysisDir = Path.Combine(contentRootPath, "Analysis");
        var logsDir = Path.Combine(templateAnalysisDir, "logs");
        Directory.CreateDirectory(logsDir);

        // Configure separate log files for global contexts only
        // Note: Endpoint-specific contexts are configured per-endpoint using AddContextLogFiles
        var contextLogFiles = new System.Collections.Generic.Dictionary<string, string>
            {
                { "Main", Path.Combine(logsDir, "csharp_main.log") },
                { "IdleTracking", Path.Combine(logsDir, "csharp_idletracking.log") }
            };

        // Configure logger (no main log file - only context files)
        Logger.Configure(logLevel, consoleOutput: false, Logger.LogRotation.HOURLY);

        // Set logs directory for clearing
        Logger.SetLogsDirectory(logsDir);

        // Clear logs in debug mode, clear old logs in production
#if DEBUG
        Logger.ClearLogs();
#else
            Logger.ClearOldLogs(7); // Clear logs older than 7 days
#endif

        // Configure context log files AFTER clearing (which disposes writers)
        Logger.ConfigureContextLogFiles(contextLogFiles);

        Logger.Info("AssemblerWeb starting up", "Main");

        #endregion

        #region Builder Config

        var builder = WebApplication.CreateBuilder(args);

        // Configure web root path based on WebRootFolderName
        var webRootPath = Path.Combine(contentRootPath, WebRootFolderName);
        builder.Environment.WebRootPath = webRootPath;

        builder.Services.AddOpenApi(options =>
        {
            options.OpenApiVersion = Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_0;
            //options.AddScalarTransformers(); // Required for Scalar extensions to work
        });

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, AssemblerJsonContext.Default);
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, AssemblerTestJsonContext.Default);
        });


        var app = builder.Build();

        #endregion

        #region Is Debug Checking

        var isDebug = Environment.GetEnvironmentVariable("DEBUG") == "true"
            || Environment.GetEnvironmentVariable("VSCODE_DEBUG") == "true";
#if DEBUG
        isDebug = true;
#endif

        #endregion

        #region Idle Tracking Middleware

        // Determine if idle tracking should be enabled
        // Command line args and explicit env vars take precedence
        bool idleTrackingEnabled;
        if (skipIdleTracking)
        {
            idleTrackingEnabled = false; // --skipIdleTracking flag explicitly disables
        }
        else
        {
            var idleTrackerDisabledEnv = Environment.GetEnvironmentVariable("IDLE_TRACKER_DISABLED");
            if (idleTrackerDisabledEnv == "false")
            {
                idleTrackingEnabled = true; // Explicitly enable idle tracking
            }
            else if (idleTrackerDisabledEnv == "true")
            {
                idleTrackingEnabled = false; // Explicitly disable idle tracking
            }
            else
            {
                idleTrackingEnabled = !isDebug; // Default: disable in debug mode
            }
        }

        // Idle Tracking Middleware
        if (idleTrackingEnabled)
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

            // Register shutdown handler for idle tracking
            appLifetime.ApplicationStopping.Register(() =>
            {
                IdleTrackingMiddleware.Shutdown();
            });
        }
        else
        {
            Console.WriteLine("[IdleTracking] Idle tracking DISABLED");
        }

        #endregion

        #region OpenApi/Scalar Config

        app.MapOpenApi();

        // Serve Scalar UI at /scalar using endpointPrefix as first argument (modern usage)
        app.MapScalarApiReference("/scalar", options =>
        {
            options
                .WithTitle("Arshu Api")
                .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
        });

        #endregion

        #region Static Files

        app.UseStaticFiles();

        #endregion

        #region Assembler Config

        var wwwrootPath = System.IO.Path.Combine(contentRootPath, WebRootFolderName);
        ConfigUtil.Load(wwwrootPath);

        #endregion

        #region Assembler Endpoint

        app.MapAssemblerEndpoints();

        #endregion

        #region Assembler Test Endpoint

        // Register endpoints grouped by tag "AssemblerTest"
        app.MapAssemblerTestEndpoints();

        #endregion

        // Register application lifetime events for logging
        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Register(() =>
        {
            Logger.Info("AssemblerWeb shutting down...", "Main");
            Logger.Flush();
        });
        lifetime.ApplicationStopped.Register(() =>
        {
            Logger.Info("AssemblerWeb stopped", "Main");
            Logger.Flush();
        });

        app.Run();
    }
}

