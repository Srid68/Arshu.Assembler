<?php
// PHP Development Server Launcher with Idle Tracking Support
// This script starts the PHP built-in server and optionally launches idle tracking monitor

// Parse command line arguments for port
$port = 8060;
$host = 'localhost';
$skipIdleTracking = false;

foreach ($argv as $i => $arg) {
    if (preg_match('/^(\w+):(\d+)$/', $arg, $matches)) {
        $host = $matches[1];
        $port = $matches[2];
        break;
    }
    if ($arg === '--port' && isset($argv[$i + 1])) {
        $port = $argv[$i + 1];
    }
    if ($arg === '--skipIdleTracking') {
        $skipIdleTracking = true;
    }
}

// Check if running in debug mode
$isDebug = getenv('DEBUG') === 'true'
    || getenv('VSCODE_DEBUG') === 'true'
    || getenv('APP_ENV') === 'development';

if (extension_loaded('xdebug')) {
    $isDebug = true;
}

// Determine if idle tracking should be enabled
$idleTrackingEnabled = false;
if (!$skipIdleTracking) {
    $idleTrackerDisabledEnv = getenv('IDLE_TRACKER_DISABLED');
    if ($idleTrackerDisabledEnv === 'false') {
        $idleTrackingEnabled = true; // Explicitly enable idle tracking
    } elseif ($idleTrackerDisabledEnv === 'true') {
        $idleTrackingEnabled = false; // Explicitly disable idle tracking
    } else {
        $idleTrackingEnabled = !$isDebug; // Default: disable in debug mode
    }
}

$address = "{$host}:{$port}";
$url = "http://{$address}/";

echo "Starting PHP Development Server on {$url}\n";
echo "Debug mode: " . ($isDebug ? 'enabled' : 'disabled') . "\n";
echo "Idle tracking: " . ($idleTrackingEnabled ? 'enabled' : 'disabled') . "\n";

// If idle tracking is enabled, start the monitor process
if ($idleTrackingEnabled) {
    $monitorScript = __DIR__ . DIRECTORY_SEPARATOR . 'services' . DIRECTORY_SEPARATOR . 'IdleTrackingMonitor.php';
    $idleSeconds = getenv('IDLE_SECONDS') ?: '10';
    
    // Create tmp directory if needed
    $tmpDir = __DIR__ . DIRECTORY_SEPARATOR . 'tmp';
    if (!is_dir($tmpDir)) {
        mkdir($tmpDir, 0755, true);
    }
    
    $holdsDir = $tmpDir . DIRECTORY_SEPARATOR . 'holds';
    if (!is_dir($holdsDir)) {
        mkdir($holdsDir, 0755, true);
    }
    
    // Create last request file - use same name as IdleTrackingMiddleware expects
    $lastRequestFile = $tmpDir . DIRECTORY_SEPARATOR . 'php_assembler_last_request.txt';
    file_put_contents($lastRequestFile, time());
    
    // Write PID file - use same name as IdleTrackingMiddleware expects
    $pidFile = $tmpDir . DIRECTORY_SEPARATOR . 'php_assembler_server_pid.txt';
    
    echo "Starting idle tracking monitor (idle timeout: {$idleSeconds} seconds)...\n";
    
    $os = PHP_OS_FAMILY;
    if ($os === 'Windows') {
        // Start monitor in background on Windows
        // Args: lastRequestFile, pidFile, idleSeconds, osFamily
        $monitorCmd = "php " . escapeshellarg($monitorScript) 
            . " " . escapeshellarg($lastRequestFile) 
            . " " . escapeshellarg($pidFile) 
            . " " . escapeshellarg($idleSeconds)
            . " " . escapeshellarg($os);
        
        // Use PowerShell to start detached process that survives parent
        $psCmd = "powershell -WindowStyle Hidden -Command \"Start-Process -WindowStyle Hidden php -ArgumentList '" 
            . addslashes($monitorScript) . "','" 
            . addslashes($lastRequestFile) . "','" 
            . addslashes($pidFile) . "','" 
            . addslashes($idleSeconds) . "','" 
            . addslashes($os) . "'\"";
        pclose(popen("start /B {$psCmd}", "r"));
    } else {
        // Start monitor in background on Unix-like systems
        $monitorCmd = "php " . escapeshellarg($monitorScript) 
            . " " . escapeshellarg($lastRequestFile) 
            . " " . escapeshellarg($pidFile) 
            . " " . escapeshellarg($idleSeconds)
            . " " . escapeshellarg($os)
            . " > /dev/null 2>&1 &";
        exec($monitorCmd);
    }
}

// Start server in background
$cmd = "php -S {$address} -t . index.php";
$os = PHP_OS_FAMILY;

if ($os === 'Windows') {
    // Start server in new window
    pclose(popen("start \"PHP Server - {$address}\" cmd /k \"{$cmd}\"", "r"));
    
    // Note: On Windows with start command, we can't easily get the child process PID
    // The PID file will be written by the server itself via index.php if needed
    // For development, this is acceptable since the monitor will check for active holds

    // Launch browser after delay if in debug mode
    if ($isDebug) {
        echo "Opening browser at {$url}...\n";
        $browserCmd = "powershell -Command \"Start-Sleep -Milliseconds 1000; Start-Process '{$url}'\"";
        pclose(popen("start /B {$browserCmd}", "r"));
    }
} else {
    // For Unix-like systems, start server in background
    exec("{$cmd} > /dev/null 2>&1 & echo $!", $output);
    $serverPid = isset($output[0]) ? trim($output[0]) : null;
    
    // Write PID file if idle tracking is enabled
    if ($idleTrackingEnabled && $serverPid) {
        $tmpDir = __DIR__ . DIRECTORY_SEPARATOR . 'tmp';
        $pidFile = $tmpDir . DIRECTORY_SEPARATOR . 'php_assembler_server_pid.txt';
        file_put_contents($pidFile, $serverPid);
    }

    // Launch browser after delay if in debug mode
    if ($isDebug) {
        echo "Opening browser at {$url}...\n";
        if ($os === 'Darwin') {
            exec("(sleep 1 && open {$url}) > /dev/null 2>&1 &");
        } else {
            exec("(sleep 1 && xdg-open {$url}) > /dev/null 2>&1 &");
        }
    }
}

echo "Server started. Press Ctrl+C to stop.\n";


