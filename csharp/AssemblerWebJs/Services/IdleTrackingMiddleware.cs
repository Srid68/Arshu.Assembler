using Assembler.Common;
using Arshu.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AssemblerWeb.Services
{
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
            Logger.Info(message, "IdleTracking");
        }

        /// <summary>
        /// Acquire a hold to prevent shutdown during critical operations.
        /// </summary>
        public static void AcquireHold(string holdId)
        {
            _activeHolds[holdId] = DateTime.UtcNow;
            Console.WriteLine($"[HOLD] Hold acquired: {holdId}");
            Log($"[HOLD] Hold acquired: {holdId}");
        }

        /// <summary>
        /// Release a previously acquired hold.
        /// </summary>
        public static void ReleaseHold(string holdId)
        {
            if (_activeHolds.TryRemove(holdId, out _))
            {
                Console.WriteLine($"[HOLD] Hold released: {holdId}");
                Log($"[HOLD] Hold released: {holdId}");
            }
        }

        public static void Configure(int idleSeconds)
        {
            _idleSeconds = idleSeconds;
            Console.WriteLine($"[STARTUP] Configured idleSeconds = {_idleSeconds}");
            Log($"[STARTUP] Configured idleSeconds = {_idleSeconds}");
        }

        public static void StartTimer(IHostApplicationLifetime appLifetime)
        {
            Console.WriteLine("[STARTUP] Starting idle monitor with 10-second check interval");
            Log("[STARTUP] Starting idle monitor with 10-second check interval");
            _timer = new Timer(_ => CheckIdle(appLifetime), null, 10000, 10000);
        }

        public static void UpdateLastRequest()
        {
            lock (_lock)
            {
                _lastRequest = DateTime.UtcNow;
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
                    Log($"[MONITOR] Removed expired hold: {hold.Key} (age: {holdAge}s)");
                }
                else
                {
                    activeHolds++;
                }
            }

            var idleTime = DateTime.UtcNow - _lastRequest;
            Console.WriteLine($"[MONITOR] IdleTime: {idleTime.TotalSeconds:F1}s, Threshold: {_idleSeconds}s, ActiveHolds: {activeHolds}");
            Log($"[MONITOR] IdleTime: {idleTime.TotalSeconds:F1}s, Threshold: {_idleSeconds}s, ActiveHolds: {activeHolds}");

            // Only trigger shutdown if idle time exceeded AND no active holds
            if (idleTime.TotalSeconds > _idleSeconds && activeHolds == 0)
            {
                Console.WriteLine("[MONITOR] Idle timeout exceeded with no active holds, triggering shutdown");
                Log("[MONITOR] Idle timeout exceeded with no active holds, triggering shutdown");
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
            Log($"[REQUEST] Request started, hold set: {holdId}");

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
                Log($"[REQUEST] Request completed");
            }
            finally
            {
                // Always remove hold after processing (even if exception occurs)
                _activeHolds.TryRemove(holdId, out _);
                Console.WriteLine($"[REQUEST] Hold removed: {holdId}");
                Log($"[REQUEST] Hold removed: {holdId}");
            }
        }

        public static void Shutdown()
        {
            Console.WriteLine($"[SHUTDOWN] IdleTrackingMiddleware shutting down, active holds: {_activeHolds.Count}");
            Log($"[SHUTDOWN] IdleTrackingMiddleware shutting down, active holds: {_activeHolds.Count}");
            
            // Log any remaining holds
            foreach (var hold in _activeHolds)
            {
                Console.WriteLine($"[SHUTDOWN] Unreleased hold: {hold.Key}");
                Log($"[SHUTDOWN] Unreleased hold: {hold.Key}");
            }
            
            // Dispose timer
            _timer?.Dispose();
            _timer = null;
            
            Console.WriteLine("[SHUTDOWN] IdleTrackingMiddleware stopped");
            Log("[SHUTDOWN] IdleTrackingMiddleware stopped");
            Logger.Flush();
        }
    }
}
