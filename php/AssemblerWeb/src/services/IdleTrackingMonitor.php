#!/usr/bin/env php
<?php
// IdleTrackingMonitor.php
// This script is launched as a background process by IdleTrackingMiddleware

require_once __DIR__ . '/../../vendor/autoload.php';

use Arshu\Common\Logger;

// Configure logger for this monitor process
$logRotation = Logger::ROTATION_NONE;
$projectDirectory = dirname(dirname(__DIR__)); // Go up two levels to AssemblerWeb directory (from src/services)
$templateAnalysisDir = $projectDirectory . DIRECTORY_SEPARATOR . 'Analysis';
$logsDir = $templateAnalysisDir . DIRECTORY_SEPARATOR . 'logs';

$contextLogFiles = [
    'IdleTracking' => $logsDir . DIRECTORY_SEPARATOR . 'php_idletracking.log',
];

Logger::configure(Logger::DEBUG, false, $logRotation);
Logger::setLogsDirectory($logsDir);
Logger::configureContextLogFiles($contextLogFiles);

$lastRequestFile = $argv[1];
$pidFile = $argv[2];
$idleSeconds = (int)$argv[3];
$osFamily = $argv[4];
$holdTimeoutSeconds = 300; // Safety timeout for stuck holds

// Get hold directory from temp directory
$tempDir = dirname($lastRequestFile);
$holdDir = $tempDir . DIRECTORY_SEPARATOR . 'holds';

error_log("[MONITOR] Monitor script started (holdTimeout={$holdTimeoutSeconds}s)");
Logger::info("Monitor script started. Args: " . json_encode($argv), 'IdleTracking');

while (true) {
    sleep(10);
    if (!file_exists($lastRequestFile) || !file_exists($pidFile)) {
        error_log("[MONITOR] Missing tracking files, exiting");
        Logger::info("Missing lastRequestFile or pidFile, exiting.", 'IdleTracking');
        break;
    }

    // Check for active hold files (requests in progress)
    $activeHolds = 0;
    $expiredHolds = 0;
    if (is_dir($holdDir)) {
        $holdFiles = glob($holdDir . DIRECTORY_SEPARATOR . 'hold_*.txt');
        if ($holdFiles !== false) {
            $currentTime = time();
            foreach ($holdFiles as $holdFile) {
                $holdTimestamp = (int)file_get_contents($holdFile);
                $holdAge = $currentTime - $holdTimestamp;
                if ($holdAge < $holdTimeoutSeconds) {
                    $activeHolds++;
                } else {
                    $expiredHolds++;
                    // Clean up expired hold file
                    @unlink($holdFile);
                    error_log("[MONITOR] Removed expired hold file (age: {$holdAge}s)");
                    Logger::warn("[MONITOR] Removed expired hold file (age: {$holdAge}s)", 'IdleTracking');
                }
            }
        }
    }

    $lastRequest = (int)file_get_contents($lastRequestFile);
    $idleTime = time() - $lastRequest;
    $idleTimeFormatted = number_format($idleTime, 1);
    $pid = file_get_contents($pidFile);
    error_log("[MONITOR] PID: {$pid}, IdleTime: {$idleTimeFormatted}s, Threshold: {$idleSeconds}s, ActiveHolds: {$activeHolds}");
    Logger::info("[MONITOR] IdleTime: {$idleTimeFormatted}s, Threshold: {$idleSeconds}s, ActiveHolds: {$activeHolds}", 'IdleTracking');

    // Only trigger shutdown if idle time exceeded AND no active holds
    if ($idleTime > $idleSeconds && $activeHolds == 0) {
        error_log("[MONITOR] Idle timeout exceeded with no active holds, triggering shutdown");
        Logger::info("[MONITOR] Idle timeout exceeded with no active holds, triggering shutdown", 'IdleTracking');

        // Log shutdown messages
        error_log("AssemblerWeb shutting down due to idle timeout...");
        Logger::info("AssemblerWeb shutting down due to idle timeout...", 'Main');

        // Log active holds (should be 0 at this point)
        error_log("[SHUTDOWN] IdleTrackingMiddleware shutting down, active holds: {$activeHolds}");
        Logger::info("[SHUTDOWN] IdleTrackingMiddleware shutting down, active holds: {$activeHolds}", 'IdleTracking');

        error_log("[SHUTDOWN] IdleTrackingMiddleware stopped");
        Logger::info("[SHUTDOWN] IdleTrackingMiddleware stopped", 'IdleTracking');

        // Flush logs before killing process
        usleep(200000); // 200ms

        error_log("AssemblerWeb stopped");
        Logger::info("AssemblerWeb stopped", 'Main');
        
        // Give time for final logs to flush
        usleep(100000); // 100ms
        
        $pid = (int)file_get_contents($pidFile);
        if ($pid > 0) {
            Logger::info("Attempting to kill PID: $pid", 'IdleTracking');
            if ($osFamily === 'Windows') {
                exec("taskkill /F /PID {$pid}");
            } else {
                // Send SIGTERM to PID 1 for Fly/Overmind container shutdown
                exec('kill -15 1');
            }
        } else {
            Logger::error("Invalid PID: $pid", 'IdleTracking');
        }
        @unlink($lastRequestFile);
        @unlink($pidFile);
        break;
    }
}
