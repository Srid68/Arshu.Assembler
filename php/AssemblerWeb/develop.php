<?php
// PHP Development Server Launcher with Idle Tracking Support
// This script starts the PHP built-in server and optionally launches idle tracking monitor

// Parse command line arguments for port
$port = 8060;
$host = 'localhost';
$skipIdleTracking = false;
$browserMarkerStaleSeconds = 300; // Browser marker expires after 5 minutes (300s)

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

// Check if running in VSCode debug/foreground mode
$runInForeground = getenv('VSCODE_DEBUG') === 'true' || extension_loaded('xdebug');

// If idle tracking is enabled and NOT running in foreground, start the monitor process
// In foreground mode, the middleware will start the monitor on first request
if ($idleTrackingEnabled && !$runInForeground) {
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
// Preserve environment variables when spawning server process
$envVars = [];
$envVarsToPass = ['DEBUG', 'VSCODE_DEBUG', 'APP_ENV', 'IDLE_TRACKER_DISABLED', 'IDLE_SECONDS', 'XDEBUG_CONFIG', 'XDEBUG_MODE'];
foreach ($envVarsToPass as $envVar) {
    $value = getenv($envVar);
    if ($value !== false) {
        $envVars[] = $envVar . '=' . $value;
    }
}

$os = PHP_OS_FAMILY;
if ($os === 'Windows') {
    // When running from VSCode with debugger, run server in foreground in same process
    // This ensures environment variables are preserved
    if ($runInForeground) {
        echo "Running server in foreground (VSCode debug mode)...\n";

        // Launch browser first time only using marker file
        // Marker file persists across server restarts within same debug session
        $tmpDir = __DIR__ . DIRECTORY_SEPARATOR . 'tmp';
        if (!is_dir($tmpDir)) {
            mkdir($tmpDir, 0755, true);
        }
        $browserOpenedMarker = $tmpDir . DIRECTORY_SEPARATOR . '.browser_opened';

        $shouldOpenBrowser = false;
        if ($isDebug) {
            echo "Checking browser marker: {$browserOpenedMarker}\n";
            if (file_exists($browserOpenedMarker)) {
                // Check if marker is stale (older than configured timeout = new debug session)
                $markerAge = time() - filemtime($browserOpenedMarker);
                echo "Browser marker exists (age: {$markerAge}s)\n";
                if ($markerAge > $browserMarkerStaleSeconds) {
                    @unlink($browserOpenedMarker);
                    $shouldOpenBrowser = true;
                    echo "Browser marker stale (>{$browserMarkerStaleSeconds}s), opening browser...\n";
                } else {
                    echo "Browser already opened in this session (marker age: {$markerAge}s). Navigate to: {$url}\n";
                }
            } else {
                $shouldOpenBrowser = true;
                echo "No browser marker found, opening browser at {$url} (first time)...\n";
            }

            if ($shouldOpenBrowser) {
                $timestamp = time();
                file_put_contents($browserOpenedMarker, $timestamp);
                echo "Created browser marker with timestamp: {$timestamp}\n";
                echo "Launching browser command...\n";

                // Use simpler command that's more reliable
                $browserCmd = "start \"\" \"{$url}\"";
                echo "Executing: {$browserCmd}\n";
                exec($browserCmd, $output, $returnCode);
                echo "Browser command return code: {$returnCode}\n";

                if ($returnCode !== 0) {
                    echo "Warning: Browser launch may have failed (code: {$returnCode})\n";
                }
            }
        }

        // Run PHP built-in server directly in foreground
        // The monitor will be started by IdleTrackingMiddleware on first request
        passthru("php -S {$address} -t . index.php");
    } else {
        // Build command with environment variables for Windows
        // Use PowerShell to properly set environment variables in child process
        $envSetCommands = [];
        foreach ($envVars as $envDef) {
            list($name, $value) = explode('=', $envDef, 2);
            $envSetCommands[] = "\$env:{$name}='{$value}'";
        }
        $envPrefix = implode('; ', $envSetCommands);
        if ($envPrefix) {
            $envPrefix .= '; ';
        }

        $cmd = "php -S {$address} -t . index.php";
        $psCmd = "powershell -Command \"{$envPrefix}{$cmd}\"";

        // Start server in new window
        pclose(popen("start \"PHP Server - {$address}\" cmd /k \"{$psCmd}\"", "r"));

        // Note: On Windows with start command, we can't easily get the child process PID
        // The PID file will be written by the server itself via index.php if needed
        // For development, this is acceptable since the monitor will check for active holds

        // Launch browser after delay if in debug mode (non-foreground only)
        // Foreground mode handles browser opening with marker check above
        if ($isDebug) {
            echo "Opening browser at {$url}...\n";
            $browserCmd = "powershell -Command \"Start-Sleep -Milliseconds 1000; Start-Process '{$url}'\"";
            pclose(popen("start /B {$browserCmd}", "r"));
        }
    }
} else {
    // Build command with environment variables for Unix-like systems
    $envPrefix = '';
    foreach ($envVars as $envDef) {
        $envPrefix .= "export {$envDef} && ";
    }
    $cmd = "php -S {$address} -t . index.php";
    $fullCmd = $envPrefix . $cmd;

    // For Unix-like systems, start server in background
    exec("{$fullCmd} > /dev/null 2>&1 & echo $!", $output);
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


