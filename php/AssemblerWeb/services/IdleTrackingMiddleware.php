<?php

use Psr\Http\Message\ServerRequestInterface;
use Assembler\Common\Logger;

class IdleTrackingMiddleware {
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
        $tempDir = __DIR__ . '/../tmp';
        self::$lastRequestFile = $tempDir . DIRECTORY_SEPARATOR . 'php_assembler_last_request.txt';
        self::$pidFile = $tempDir . DIRECTORY_SEPARATOR . 'php_assembler_server_pid.txt';
        self::$holdDir = $tempDir . DIRECTORY_SEPARATOR . 'holds';

        // Create holds directory if it doesn't exist
        if (!is_dir(self::$holdDir)) {
            mkdir(self::$holdDir, 0777, true);
        }

    Logger::info("[STARTUP] Configured idleSeconds = " . self::$idleSeconds, 'IdleTracking');
    self::$configured = true;

        // Don't write PID or start monitor - that's done by IdleTrackingStartup.php
        // The middleware updates timestamps and manages per-request hold mechanism
    }

    private static function startMonitor(): void {
        if (self::$monitorStarted) {
            return;
        }
        self::$monitorStarted = true;
        $monitorScriptPath = __DIR__ . DIRECTORY_SEPARATOR . 'IdleTrackingMonitor.php';
        $lastRequestFile = escapeshellarg(self::$lastRequestFile);
        $pidFile = escapeshellarg(self::$pidFile);
        $idleSeconds = escapeshellarg(self::$idleSeconds);
        $osFamily = escapeshellarg(PHP_OS_FAMILY);
        $cmd = "php $monitorScriptPath $lastRequestFile $pidFile $idleSeconds $osFamily";
        Logger::info("Launching monitor: $cmd", 'IdleTracking');
        if (PHP_OS_FAMILY === 'Windows') {
            $proc = @popen('start /B ' . $cmd, 'r');
            if ($proc === false) {
                Logger::error("Failed to launch monitor process (Windows)", 'IdleTracking');
            } else {
                Logger::info("Monitor process started (Windows)", 'IdleTracking');
                pclose($proc);
            }
        } else {
            $proc = @popen($cmd . ' > /dev/null 2>&1 &', 'r');
            if ($proc === false) {
                Logger::error("Failed to launch monitor process (Unix)", 'IdleTracking');
            } else {
                Logger::info("Monitor process started (Unix)", 'IdleTracking');
                pclose($proc);
            }
        }
    }

    public function __invoke(ServerRequestInterface $request, $handler) {
        // Generate unique hold file for this request to handle concurrent requests
        $holdId = uniqid('hold_', true);
        $holdFile = self::$holdDir . DIRECTORY_SEPARATOR . $holdId . '.txt';

        // Set hold before processing to prevent shutdown during long-running requests
        if (self::$holdDir !== null) {
            $timestamp = time();
            file_put_contents($holdFile, (string)$timestamp);
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

            Logger::info("[REQUEST] Request completed", 'IdleTracking');
            
            return $response;
        } finally {
            // Always remove this request's hold after processing (even if exception occurs)
            if (file_exists($holdFile)) {
                @unlink($holdFile);
                Logger::info("[REQUEST] Hold removed: $holdId", 'IdleTracking');
            }
        }
    }

    public static function shutdown(): void {
        if (self::$holdDir === null || !is_dir(self::$holdDir)) {
            Logger::info('[SHUTDOWN] IdleTrackingMiddleware shutting down, hold directory not initialized', 'IdleTracking');
            return;
        }

        $holdFiles = glob(self::$holdDir . DIRECTORY_SEPARATOR . 'hold_*.txt');
        $activeHoldsCount = is_array($holdFiles) ? count($holdFiles) : 0;

        Logger::info("[SHUTDOWN] IdleTrackingMiddleware shutting down, active holds: $activeHoldsCount", 'IdleTracking');

        // Log any remaining holds
        if (is_array($holdFiles)) {
            foreach ($holdFiles as $holdFile) {
                $holdId = basename($holdFile, '.txt');
                Logger::info("[SHUTDOWN] Unreleased hold: $holdId", 'IdleTracking');
            }
        }

        Logger::info('[SHUTDOWN] IdleTrackingMiddleware stopped', 'IdleTracking');
    }
}
