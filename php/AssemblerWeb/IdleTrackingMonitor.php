#!/usr/bin/env php
<?php
// IdleTrackingMonitor.php
// This script is launched as a background process by IdleTrackingMiddleware

require_once __DIR__ . '/../Assembler/vendor/autoload.php';

use Assembler\TemplateCommon\Logger;
use Assembler\TemplateCommon\TemplateUtils;

// Configure logger for this monitor process
$logRotation = Logger::ROTATION_NONE;
$paths = TemplateUtils::getAssemblerWebDirPath();
$projectDirectory = $paths['projectDirectory'];
$templateAnalysisDir = $projectDirectory . DIRECTORY_SEPARATOR . 'template_analysis';
$logsDir = $templateAnalysisDir . DIRECTORY_SEPARATOR . 'logs';

$contextLogFiles = [
    'IdleTracking' => $logsDir . DIRECTORY_SEPARATOR . 'php_idletracking.log',
];

Logger::configure(Logger::DEBUG, null, false, $logRotation);
Logger::configureContextLogFiles($contextLogFiles);

$lastRequestFile = $argv[1];
$pidFile = $argv[2];
$idleSeconds = (int)$argv[3];
$osFamily = $argv[4];

Logger::info("Monitor script started. Args: " . json_encode($argv), 'IdleTracking');

while (true) {
    sleep(10);
    if (!file_exists($lastRequestFile) || !file_exists($pidFile)) {
        Logger::info("Missing lastRequestFile or pidFile, exiting.", 'IdleTracking');
        break;
    }
    $lastRequest = (int)file_get_contents($lastRequestFile);
    $idleTime = time() - $lastRequest;
    Logger::debug("PID: " . file_get_contents($pidFile) . ", IdleTime: $idleTime, IdleSeconds: $idleSeconds", 'IdleTracking');
    if ($idleTime > $idleSeconds) {
        Logger::info("Idle timeout reached ({$idleSeconds}s), shutting down server...", 'IdleTracking');
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
