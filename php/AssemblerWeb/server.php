<?php
// PHP Development Server Launcher with Browser Auto-Open
// This script starts the PHP built-in server and opens the browser

// Parse command line arguments for port
$port = 8085;
$host = 'localhost';

foreach ($argv as $i => $arg) {
    if (preg_match('/^(\w+):(\d+)$/', $arg, $matches)) {
        $host = $matches[1];
        $port = $matches[2];
        break;
    }
    if ($arg === '--port' && isset($argv[$i + 1])) {
        $port = $argv[$i + 1];
    }
}

// Check if running in debug mode
$isDebug = getenv('DEBUG') === 'true'
    || getenv('VSCODE_DEBUG') === 'true'
    || getenv('IDLE_TRACKER_DISABLED') === 'true'
    || getenv('APP_ENV') === 'development'
    || in_array('--skipIdleTracking', $argv);

$address = "{$host}:{$port}";
$url = "http://{$address}/";

echo "Starting PHP Development Server on {$url}\n";
echo "Debug mode: " . ($isDebug ? 'enabled' : 'disabled') . "\n";

// Start server in background
$cmd = "php -S {$address} -t . index.php";
$os = PHP_OS_FAMILY;

if ($os === 'Windows') {
    // Start server in new window
    pclose(popen("start \"PHP Server - {$address}\" cmd /k \"{$cmd}\"", "r"));

    // Launch browser after delay if in debug mode
    if ($isDebug) {
        echo "Opening browser at {$url}...\n";
        $browserCmd = "powershell -Command \"Start-Sleep -Milliseconds 1000; Start-Process '{$url}'\"";
        pclose(popen("start /B {$browserCmd}", "r"));
    }
} else {
    // For Unix-like systems, start server in background
    exec("{$cmd} > /dev/null 2>&1 &");

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
