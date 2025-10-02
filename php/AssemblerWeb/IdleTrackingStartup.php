<?php
// This script launches IdleTrackingMonitor at container startup

$monitorScriptPath = __DIR__ . DIRECTORY_SEPARATOR . 'IdleTrackingMonitor.php';
$lastRequestFile = sys_get_temp_dir() . DIRECTORY_SEPARATOR . 'php_assembler_last_request.txt';
$pidFile = sys_get_temp_dir() . DIRECTORY_SEPARATOR . 'php_assembler_server_pid.txt';
$idleSeconds = getenv('IDLE_SECONDS') ?: 10;
$osFamily = strtoupper(substr(PHP_OS_FAMILY, 0, 3)) === 'WIN' ? 'Windows' : 'Unix';
file_put_contents($lastRequestFile, time());
file_put_contents($pidFile, getmypid());
$cmd = "php $monitorScriptPath $lastRequestFile $pidFile $idleSeconds $osFamily &";
error_log("[IdleTracking] About to exec: $cmd");
$output = [];
$result = null;
exec($cmd, $output, $result);
error_log("[IdleTracking] Monitor exec output: " . json_encode($output) . ", result: $result");
error_log("[IdleTracking] Monitor started at container startup: $cmd");
