using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Scalar.AspNetCore;
using System;
using System.Threading;
using System.Threading.Tasks;

#region Idle Tracking Middleware

public class IdleTrackingMiddleware
{
    private static DateTime _lastRequest = DateTime.UtcNow;
    private static Timer? _timer;
    private static bool _shutdownInitiated = false;
    private static int _idleSeconds = 10;
    private static object _lock = new object();
    private static ILogger? _logger;
    public static void SetLogger(ILogger logger)
    {
        _logger = logger;
    }
    private static void Log(string message)
    {
        _logger?.LogInformation($"[IdleTrackingMiddleware] {DateTime.UtcNow:O} {message}");
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

        #region Builder Config

        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddOpenApi(options =>
        {
            options.OpenApiVersion = Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_0;
            //options.AddScalarTransformers(); // Required for Scalar extensions to work
        });

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, MergeRequestJsonContext.Default);
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, ResponseJsonContext.Default);
        });


        var app = builder.Build();

        #endregion

        #region Idle Tracking Middleware

        // Idle Tracking Middleware
        if (!app.Environment.IsDevelopment())
        {
            var idleSecondsEnv = Environment.GetEnvironmentVariable("IDLE_SECONDS");
            var idleSeconds = 10;
            if (!string.IsNullOrEmpty(idleSecondsEnv) && int.TryParse(idleSecondsEnv, out var envIdleSeconds))
                idleSeconds = envIdleSeconds;
            IdleTrackingMiddleware.Configure(idleSeconds);
            var appLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
            var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("IdleTrackingMiddleware");
            IdleTrackingMiddleware.SetLogger(logger);
            IdleTrackingMiddleware.StartTimer(appLifetime);
            app.Use((context, next) => IdleTrackingMiddleware.InvokeAsync(context, next, appLifetime));
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

        #region Assembler Endpoint

        // Register endpoints grouped by tag "Assembler"
        app.MapAssemblerEndpoints();

        #endregion

        app.Run();
    }

}

