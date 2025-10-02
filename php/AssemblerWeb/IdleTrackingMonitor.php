#!/usr/bin/env php
<?php
error_log("[IdleMonitor] Monitor script started. Args: " . json_encode($argv));
// IdleTrackingMonitor.php
// This script is launched as a background process by IdleTrackingMiddleware

$lastRequestFile = $argv[1];
$pidFile = $argv[2];
$idleSeconds = (int)$argv[3];
$osFamily = $argv[4];

while (true) {
    sleep(10);
    if (!file_exists($lastRequestFile) || !file_exists($pidFile)) {
        error_log("[IdleMonitor] Missing lastRequestFile or pidFile, exiting.");
        break;
    }
    $lastRequest = (int)file_get_contents($lastRequestFile);
    $idleTime = time() - $lastRequest;
    error_log("[IdleMonitor] PID: " . file_get_contents($pidFile) . ", IdleTime: $idleTime, IdleSeconds: $idleSeconds");
    if ($idleTime > $idleSeconds) {
        error_log("[IdleMonitor] Idle timeout reached ({$idleSeconds}s), shutting down server...");
        $pid = (int)file_get_contents($pidFile);
        if ($pid > 0) {
            error_log("[IdleMonitor] Attempting to kill PID: $pid");
            if ($osFamily === 'Windows') {
                exec("taskkill /F /PID {$pid}");
            } else {
                // Send SIGTERM to PID 1 for Fly/Overmind container shutdown
                exec('kill -15 1');
            }
        } else {
            error_log("[IdleMonitor] Invalid PID: $pid");
        }
        @unlink($lastRequestFile);
        @unlink($pidFile);
        break;
    }
}
