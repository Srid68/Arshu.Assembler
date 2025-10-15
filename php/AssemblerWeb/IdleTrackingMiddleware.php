<?php

use Psr\Http\Message\ServerRequestInterface;
use Assembler\Common\Logger;

class IdleTrackingMiddleware {
    private static $lastRequestFile;
    private static $pidFile;
    private static $holdDir;
    private static $idleSeconds = 10;
    private static $monitorStarted = false;

    public static function configure(int $idleSeconds): void {
        self::$idleSeconds = $idleSeconds;
        $tempDir = __DIR__ . '/../tmp';
        self::$lastRequestFile = $tempDir . DIRECTORY_SEPARATOR . 'php_assembler_last_request.txt';
        self::$pidFile = $tempDir . DIRECTORY_SEPARATOR . 'php_assembler_server_pid.txt';
        self::$holdDir = $tempDir . DIRECTORY_SEPARATOR . 'holds';

        // Create holds directory if it doesn't exist
        if (!is_dir(self::$holdDir)) {
            mkdir(self::$holdDir, 0777, true);
        }

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
            Logger::debug("Request started, hold set: $holdId at $timestamp", 'IdleTracking');
        }

        // Update last request timestamp
        if (self::$lastRequestFile !== null && file_exists(self::$lastRequestFile)) {
            $timestamp = time();
            file_put_contents(self::$lastRequestFile, (string)$timestamp);
            Logger::debug("Request received, updating timestamp to: $timestamp", 'IdleTracking');
        }

        try {
            // Handle the request
            $response = $handler->handle($request);

            // Update timestamp after processing
            if (self::$lastRequestFile !== null && file_exists(self::$lastRequestFile)) {
                $timestamp = time();
                file_put_contents(self::$lastRequestFile, (string)$timestamp);
                Logger::debug("Request completed, updating timestamp to: $timestamp", 'IdleTracking');
            }

            return $response;
        } finally {
            // Always remove this request's hold after processing (even if exception occurs)
            if (file_exists($holdFile)) {
                @unlink($holdFile);
                Logger::debug("Hold removed: $holdId", 'IdleTracking');
            }
        }
    }
}
