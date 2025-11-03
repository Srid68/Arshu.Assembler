#!/usr/bin/env php
<?php
// IdleTrackingMonitor.php
// This script is launched as a background process by IdleTrackingMiddleware

require_once __DIR__ . '/../../../Assembler/vendor/autoload.php';

use Assembler\Common\Logger;

// Configure logger for this monitor process
$logRotation = Logger::ROTATION_NONE;
$projectDirectory = dirname(__DIR__); // Go up one level to AssemblerWeb directory
$templateAnalysisDir = $projectDirectory . DIRECTORY_SEPARATOR . 'template_analysis';
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

Logger::info("Monitor script started. Args: " . json_encode($argv), 'IdleTracking');
echo "[MONITOR] Monitor script started (holdTimeout={$holdTimeoutSeconds}s)\n";
flush();

while (true) {
    sleep(10);
    echo "[MONITOR] Checking idle time...\n";
    flush();
    if (!file_exists($lastRequestFile) || !file_exists($pidFile)) {
        Logger::info("Missing lastRequestFile or pidFile, exiting.", 'IdleTracking');
        echo "[MONITOR] Missing tracking files, exiting\n";
        flush();
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
                    echo "[MONITOR] Removed expired hold file (age: {$holdAge}s)\n";
                    flush();
                    Logger::warn("[MONITOR] Removed expired hold file (age: {$holdAge}s)", 'IdleTracking');
                }
            }
        }
    }

    $lastRequest = (int)file_get_contents($lastRequestFile);
    $idleTime = time() - $lastRequest;
    $idleTimeFormatted = number_format($idleTime, 1);
    echo "[MONITOR] PID: " . file_get_contents($pidFile) . ", IdleTime: {$idleTimeFormatted}s, Threshold: {$idleSeconds}s, ActiveHolds: {$activeHolds}\n";
    flush();
    Logger::info("[MONITOR] IdleTime: {$idleTimeFormatted}s, Threshold: {$idleSeconds}s, ActiveHolds: {$activeHolds}", 'IdleTracking');

    // Only trigger shutdown if idle time exceeded AND no active holds
    if ($idleTime > $idleSeconds && $activeHolds == 0) {
        echo "[MONITOR] Idle timeout exceeded with no active holds, triggering shutdown\n";
        flush();
        Logger::info("[MONITOR] Idle timeout exceeded with no active holds, triggering shutdown", 'IdleTracking');
        
        // Log shutdown messages
        echo "AssemblerWeb shutting down due to idle timeout...\n";
        flush();
        Logger::info("AssemblerWeb shutting down due to idle timeout...", 'Main');
        
        // Log active holds (should be 0 at this point)
        echo "[SHUTDOWN] IdleTrackingMiddleware shutting down, active holds: {$activeHolds}\n";
        flush();
        Logger::info("[SHUTDOWN] IdleTrackingMiddleware shutting down, active holds: {$activeHolds}", 'IdleTracking');
        
        echo "[SHUTDOWN] IdleTrackingMiddleware stopped\n";
        flush();
        Logger::info("[SHUTDOWN] IdleTrackingMiddleware stopped", 'IdleTracking');
        
        // Flush logs before killing process
        usleep(200000); // 200ms
        
        echo "AssemblerWeb stopped\n";
        flush();
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
