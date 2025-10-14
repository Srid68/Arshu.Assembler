using Assembler.Common;
using Assembler.Config;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scalar.AspNetCore;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AssemblerWeb
{
    #region Idle Tracking Middleware

    public class IdleTrackingMiddleware
    {
        private static DateTime _lastRequest = DateTime.UtcNow;
        private static Timer? _timer;
        private static bool _shutdownInitiated = false;
        private static int _idleSeconds = 10;
        private static object _lock = new object();

        private static void Log(string message)
        {
            Logger.Info($"{DateTime.UtcNow:O} {message}", "IdleTrackingMiddleware");
        }

        public static void Configure(int idleSeconds)
        {
            _idleSeconds = idleSeconds;
            Log($"Configured idleSeconds = {_idleSeconds}");
        }

        public static void StartTimer(IHostApplicationLifetime appLifetime)
        {
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
            var idleTime = DateTime.UtcNow - _lastRequest;
            Log($"Idle check: idleTime={idleTime.TotalSeconds}s, idleSeconds={_idleSeconds}");
            if (idleTime.TotalSeconds > _idleSeconds)
            {
                Log("Idle period exceeded, shutting down application");
                _shutdownInitiated = true;
                appLifetime.StopApplication();
            }
        }

        public static async Task InvokeAsync(HttpContext context, RequestDelegate next, IHostApplicationLifetime appLifetime)
        {
            UpdateLastRequest();
            if (_timer == null)
            {
                StartTimer(appLifetime);
            }
            await next(context);
        }
    }

    #endregion

    public class Program
    {
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
            var templateAnalysisDir = Path.Combine(contentRootPath, "template_analysis");
            var logsDir = Path.Combine(templateAnalysisDir, "logs");
            Directory.CreateDirectory(logsDir);

            // Configure separate log files for each class
            var contextLogFiles = new System.Collections.Generic.Dictionary<string, string>
            {
                { "LoaderNormal", Path.Combine(logsDir, "csharp_loadernormal.log") },
                { "LoaderPreProcess", Path.Combine(logsDir, "csharp_loaderpreprocess.log") },
                { "EngineNormal", Path.Combine(logsDir, "csharp_enginenormal.log") },
                { "EnginePreProcess", Path.Combine(logsDir, "csharp_enginepreprocess.log") },
                { "Program", Path.Combine(logsDir, "csharp_program.log") },
                { "AssemblerEndpoint", Path.Combine(logsDir, "csharp_assemblerendpoint.log") },
                { "IdleTrackingMiddleware", Path.Combine(logsDir, "csharp_idletracking.log") }
            };

            Logger.Configure(logLevel, null, consoleOutput: false);
            Logger.ConfigureContextLogFiles(contextLogFiles);
            Logger.Info("AssemblerWeb starting up", "Program");

            #endregion

            #region Builder Config

            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddOpenApi(options =>
            {
                options.OpenApiVersion = Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_0;
                //options.AddScalarTransformers(); // Required for Scalar extensions to work
            });

            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.TypeInfoResolverChain.Insert(0, ResponseJsonContext.Default);
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

            // Serve static files from wwwroot/Resource
            app.UseStaticFiles();

            #endregion

            #region Assembler Config

            var wwwrootPath = System.IO.Path.Combine(contentRootPath, "wwwroot");
            ConfigUtil.Load(wwwrootPath);

            #endregion

            #region Assembler Endpoint

            // Register endpoints grouped by tag "Assembler"
            app.MapAssemblerEndpoints();

            #endregion

            app.Run();
        }

    }
}

