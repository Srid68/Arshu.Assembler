<?php

use Psr\Http\Message\ServerRequestInterface;
use Arshu\Common\Logger;

class IdleTrackingMiddleware {
    // Constants for file naming - ensures consistency across all idle tracking components
    public const LAST_REQUEST_FILENAME = 'php_last_request.txt';
    public const SERVER_PID_FILENAME = 'php_server_pid.txt';
    public const HOLDS_DIR_NAME = 'holds';
    public const MONITOR_MARKER_FILENAME = '.monitor_started';

    private static $lastRequestFile;
    private static $pidFile;
    private static $holdDir;
    private static $idleSeconds = 10;
    private static $monitorStarted = false;
    private static $configured = false;

    public static function configure(int $idleSeconds): void {
        if (
            self::$configured
            && self::$idleSeconds === $idleSeconds
            && self::$holdDir !== null
            && is_dir(self::$holdDir)
        ) {
            return;
        }

        self::$idleSeconds = $idleSeconds;
        $tempDir = __DIR__ . '/../../tmp';
        self::$lastRequestFile = $tempDir . DIRECTORY_SEPARATOR . self::LAST_REQUEST_FILENAME;
        self::$pidFile = $tempDir . DIRECTORY_SEPARATOR . self::SERVER_PID_FILENAME;
        self::$holdDir = $tempDir . DIRECTORY_SEPARATOR . self::HOLDS_DIR_NAME;

        // Create holds directory if it doesn't exist
        if (!is_dir(self::$holdDir)) {
            mkdir(self::$holdDir, 0777, true);
        }

        error_log("[STARTUP] Configured idleSeconds = " . self::$idleSeconds);
        Logger::info("[STARTUP] Configured idleSeconds = " . self::$idleSeconds, 'IdleTracking');
        self::$configured = true;

        // Don't write PID or start monitor - that's done by IdleTrackingStartup.php
        // The middleware updates timestamps and manages per-request hold mechanism
    }

    private static function startMonitor(): void {
        if (self::$monitorStarted) {
            return;
        }

        // Use file-based marker since static variables don't persist across requests in PHP built-in server
        $tempDir = dirname(self::$lastRequestFile);
        $monitorMarker = $tempDir . DIRECTORY_SEPARATOR . self::MONITOR_MARKER_FILENAME;

        // Check if monitor was already started (marker file exists and is recent)
        if (file_exists($monitorMarker)) {
            $markerAge = time() - filemtime($monitorMarker);
            if ($markerAge < 60) {
                // Monitor was started recently, don't start again
                self::$monitorStarted = true;
                return;
            } else {
                // Stale marker (>60s old), remove it and start fresh
                @unlink($monitorMarker);
            }
        }

        self::$monitorStarted = true;

        // Write PID file and initialize last request timestamp before starting monitor
        if (self::$pidFile !== null && self::$lastRequestFile !== null) {
            file_put_contents(self::$pidFile, (string)getmypid());
            file_put_contents(self::$lastRequestFile, (string)time());
            // Create monitor marker to prevent duplicate starts
            file_put_contents($monitorMarker, time());
            error_log("[STARTUP] Initialized tracking files, PID: " . getmypid());
            Logger::info("[STARTUP] Initialized tracking files, PID: " . getmypid(), 'IdleTracking');
        }

        $monitorScriptPath = __DIR__ . DIRECTORY_SEPARATOR . 'IdleTrackingMonitor.php';

        error_log("[STARTUP] Launching monitor...");
        error_log("[STARTUP] Note: MONITOR logs will appear in log file only (background process limitation)");
        Logger::info("[STARTUP] Launching monitor", 'IdleTracking');

        if (PHP_OS_FAMILY === 'Windows') {
            // On Windows, spawn background process
            // Note: Background processes can't easily share stderr with parent on Windows
            $cmd = "php " . escapeshellarg($monitorScriptPath)
                . " " . escapeshellarg(self::$lastRequestFile)
                . " " . escapeshellarg(self::$pidFile)
                . " " . escapeshellarg((string)self::$idleSeconds)
                . " " . escapeshellarg(PHP_OS_FAMILY);

            $proc = @popen('start /B ' . $cmd . ' 2>&1', 'r');
            if ($proc === false) {
                error_log("[STARTUP] Failed to launch monitor process (Windows)");
                Logger::error("[STARTUP] Failed to launch monitor process (Windows)", 'IdleTracking');
            } else {
                error_log("[STARTUP] Monitor process started (Windows)");
                Logger::info("[STARTUP] Monitor process started (Windows)", 'IdleTracking');
                pclose($proc);
            }
        } else {
            $cmd = "php " . escapeshellarg($monitorScriptPath)
                . " " . escapeshellarg(self::$lastRequestFile)
                . " " . escapeshellarg(self::$pidFile)
                . " " . escapeshellarg((string)self::$idleSeconds)
                . " " . escapeshellarg(PHP_OS_FAMILY)
                . " > /dev/null 2>&1 &";
            $proc = @popen($cmd, 'r');
            if ($proc === false) {
                error_log("[STARTUP] Failed to launch monitor process (Unix)");
                Logger::error("[STARTUP] Failed to launch monitor process (Unix)", 'IdleTracking');
            } else {
                error_log("[STARTUP] Monitor process started (Unix)");
                Logger::info("[STARTUP] Monitor process started (Unix)", 'IdleTracking');
                pclose($proc);
            }
        }
    }

    public function __invoke(ServerRequestInterface $request, $handler) {
        // Start monitor on first request if not already started (development mode)
        if (!self::$monitorStarted) {
            self::startMonitor();
        }

        // Generate unique hold file for this request to handle concurrent requests
        $holdId = uniqid('hold_', true);
        $holdFile = self::$holdDir . DIRECTORY_SEPARATOR . $holdId . '.txt';

        // Set hold before processing to prevent shutdown during long-running requests
        if (self::$holdDir !== null) {
            $timestamp = time();
            file_put_contents($holdFile, (string)$timestamp);
            error_log("[REQUEST] Request started, hold set: $holdId");
            Logger::info("[REQUEST] Request started, hold set: $holdId", 'IdleTracking');
        }

        // Update last request timestamp
        if (self::$lastRequestFile !== null && file_exists(self::$lastRequestFile)) {
            $timestamp = time();
            file_put_contents(self::$lastRequestFile, (string)$timestamp);
        }

        try {
            // Handle the request
            $response = $handler->handle($request);

            // Update timestamp after processing
            if (self::$lastRequestFile !== null && file_exists(self::$lastRequestFile)) {
                $timestamp = time();
                file_put_contents(self::$lastRequestFile, (string)$timestamp);
            }

            error_log("[REQUEST] Request completed");
            Logger::info("[REQUEST] Request completed", 'IdleTracking');

            return $response;
        } finally {
            // Always remove this request's hold after processing (even if exception occurs)
            if (file_exists($holdFile)) {
                @unlink($holdFile);
                error_log("[REQUEST] Hold removed: $holdId");
                Logger::info("[REQUEST] Hold removed: $holdId", 'IdleTracking');
            }
        }
    }

    /**
     * Acquire a hold with the specified ID to prevent shutdown during critical operations
     * Matches C# method signature at AssemblerWeb/Services/IdleTrackingMiddleware.cs:28
     */
    public static function AcquireHold(string $holdId): void {
        if (self::$holdDir === null) {
            return;
        }
        $holdFile = self::$holdDir . DIRECTORY_SEPARATOR . $holdId . '.txt';
        $timestamp = time();
        file_put_contents($holdFile, (string)$timestamp);
        error_log("[HOLD] Hold acquired: $holdId");
        Logger::info("[HOLD] Hold acquired: $holdId", 'IdleTracking');
    }

    /**
     * Release a previously acquired hold
     * Matches C# method signature at AssemblerWeb/Services/IdleTrackingMiddleware.cs:38
     */
    public static function ReleaseHold(string $holdId): void {
        if (self::$holdDir === null || $holdId === '') {
            return;
        }
        $holdFile = self::$holdDir . DIRECTORY_SEPARATOR . $holdId . '.txt';
        if (file_exists($holdFile)) {
            @unlink($holdFile);
            error_log("[HOLD] Hold released: $holdId");
            Logger::info("[HOLD] Hold released: $holdId", 'IdleTracking');
        }
    }

    public static function shutdown(): void {
        if (self::$holdDir === null || !is_dir(self::$holdDir)) {
            error_log('[SHUTDOWN] IdleTrackingMiddleware shutting down, hold directory not initialized');
            Logger::info('[SHUTDOWN] IdleTrackingMiddleware shutting down, hold directory not initialized', 'IdleTracking');
            return;
        }

        $holdFiles = glob(self::$holdDir . DIRECTORY_SEPARATOR . 'hold_*.txt');
        $activeHoldsCount = is_array($holdFiles) ? count($holdFiles) : 0;

        error_log("[SHUTDOWN] IdleTrackingMiddleware shutting down, active holds: $activeHoldsCount");
        Logger::info("[SHUTDOWN] IdleTrackingMiddleware shutting down, active holds: $activeHoldsCount", 'IdleTracking');

        // Log any remaining holds
        if (is_array($holdFiles)) {
            foreach ($holdFiles as $holdFile) {
                $holdId = basename($holdFile, '.txt');
                error_log("[SHUTDOWN] Unreleased hold: $holdId");
                Logger::info("[SHUTDOWN] Unreleased hold: $holdId", 'IdleTracking');
            }
        }

        error_log('[SHUTDOWN] IdleTrackingMiddleware stopped');
        Logger::info('[SHUTDOWN] IdleTrackingMiddleware stopped', 'IdleTracking');
        Logger::flush();
    }

}
