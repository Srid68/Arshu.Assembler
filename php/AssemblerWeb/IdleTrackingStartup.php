<?php
// This script launches IdleTrackingMonitor at container startup

require_once __DIR__ . '/../Assembler/vendor/autoload.php';

use Assembler\TemplateCommon\Logger;
use Assembler\TemplateCommon\TemplateUtils;

// Configure logger
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

$monitorScriptPath = __DIR__ . DIRECTORY_SEPARATOR . 'IdleTrackingMonitor.php';
$lastRequestFile = sys_get_temp_dir() . DIRECTORY_SEPARATOR . 'php_assembler_last_request.txt';
$pidFile = sys_get_temp_dir() . DIRECTORY_SEPARATOR . 'php_assembler_server_pid.txt';
$idleSeconds = getenv('IDLE_SECONDS') ?: 10;
$osFamily = strtoupper(substr(PHP_OS_FAMILY, 0, 3)) === 'WIN' ? 'Windows' : 'Unix';
file_put_contents($lastRequestFile, time());
file_put_contents($pidFile, getmypid());
$cmd = "php $monitorScriptPath $lastRequestFile $pidFile $idleSeconds $osFamily &";
Logger::info("About to exec: $cmd", 'IdleTracking');
$output = [];
$result = null;
exec($cmd, $output, $result);
Logger::info("Monitor exec output: " . json_encode($output) . ", result: $result", 'IdleTracking');
Logger::info("Monitor started at container startup: $cmd", 'IdleTracking');
