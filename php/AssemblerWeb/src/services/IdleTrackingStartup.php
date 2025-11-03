<?php
// This script launches IdleTrackingMonitor at container startup
error_reporting(E_ALL);
ini_set('display_errors', 1);

echo "[STARTUP] IdleTrackingStartup.php beginning execution\n";
flush();

echo "[STARTUP] Loading autoload.php...\n";
flush();
require_once __DIR__ . '/../../Assembler/vendor/autoload.php';
echo "[STARTUP] Autoload complete\n";
flush();

use Assembler\Common\Logger;

// Configure logger
echo "[STARTUP] Configuring logger...\n";
flush();
$logRotation = Logger::ROTATION_NONE;
$projectDirectory = __DIR__;
$templateAnalysisDir = $projectDirectory . DIRECTORY_SEPARATOR . 'template_analysis';
$logsDir = $templateAnalysisDir . DIRECTORY_SEPARATOR . 'logs';

$contextLogFiles = [
    'IdleTracking' => $logsDir . DIRECTORY_SEPARATOR . 'php_idletracking.log',
];

Logger::configure(Logger::DEBUG, null, false, $logRotation);
Logger::configureContextLogFiles($contextLogFiles);
echo "[STARTUP] Logger configured\n";
flush();

// Use consistent temp directory
$tempDir = __DIR__ . '/../../tmp';
if (!is_dir($tempDir)) {
    mkdir($tempDir, 0777, true);
}

$monitorScriptPath = __DIR__ . DIRECTORY_SEPARATOR . 'IdleTrackingMonitor.php';
$lastRequestFile = $tempDir . DIRECTORY_SEPARATOR . 'php_assembler_last_request.txt';
$pidFile = $tempDir . DIRECTORY_SEPARATOR . 'php_assembler_server_pid.txt';
$idleSeconds = getenv('IDLE_SHUTDOWN_SECONDS') ?: 10;
$osFamily = PHP_OS_FAMILY;

echo "[STARTUP] Waiting for Apache to start...\n";
flush();
Logger::info("Waiting for Apache to start...", 'IdleTracking');

// Wait for Apache to start and get its PID
$apachePid = null;
for ($i = 0; $i < 30; $i++) {
    sleep(1);
    // Find Apache master process
    $output = shell_exec('pgrep -f "apache2.*FOREGROUND" | head -1');
    if ($output && trim($output) !== '') {
        $apachePid = (int)trim($output);
        echo "[STARTUP] Found Apache PID: $apachePid\n";
        flush();
        Logger::info("Found Apache PID: $apachePid", 'IdleTracking');
        break;
    }
}

if ($apachePid === null || $apachePid <= 0) {
    echo "[STARTUP] ERROR: Could not find Apache process after 30 seconds, exiting\n";
    flush();
    Logger::error("Could not find Apache process, exiting", 'IdleTracking');
    exit(1);
}
echo "[STARTUP] Apache found, continuing...\n";
flush();

// Initialize tracking files
echo "[STARTUP] Writing tracking files...\n";
flush();
file_put_contents($lastRequestFile, time());
file_put_contents($pidFile, $apachePid);
// Make files writable by Apache (www-data)
chmod($lastRequestFile, 0666);
chmod($pidFile, 0666);
echo "[STARTUP] Tracking files written with permissions 0666\n";
flush();

// Run monitor in foreground (overmind manages the process)
echo "[STARTUP] Starting idle monitor with idleSeconds=$idleSeconds, Apache PID=$apachePid\n";
flush();
Logger::info("Starting idle monitor with idleSeconds=$idleSeconds, Apache PID=$apachePid", 'IdleTracking');
echo "[STARTUP] Launching monitor: php $monitorScriptPath $lastRequestFile $pidFile $idleSeconds $osFamily\n";
flush();
passthru("php $monitorScriptPath $lastRequestFile $pidFile $idleSeconds $osFamily");
echo "[STARTUP] Monitor exited\n";
flush();
