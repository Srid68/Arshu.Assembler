<?php

use Psr\Http\Message\ServerRequestInterface;

class IdleTrackingMiddleware {
    private static $lastRequestFile;
    private static $pidFile;
    private static $idleSeconds = 10;
    private static $monitorStarted = false;

    public static function configure(int $idleSeconds): void {
        self::$idleSeconds = $idleSeconds;
        $tempDir = __DIR__ . '/../tmp';
        if (!is_dir($tempDir)) {
            mkdir($tempDir, 0777, true);
        }
        self::$lastRequestFile = $tempDir . DIRECTORY_SEPARATOR . 'php_assembler_last_request.txt';
        self::$pidFile = $tempDir . DIRECTORY_SEPARATOR . 'php_assembler_server_pid.txt';
        file_put_contents(self::$pidFile, (string)getmypid());
        file_put_contents(self::$lastRequestFile, (string)time());
        self::startMonitor();
    }

    private static function startMonitor(): void {
        if (self::$monitorStarted) {
            return;
        }
        self::$monitorStarted = true;
        $monitorScriptPath = __DIR__ . DIRECTORY_SEPARATOR . 'IdleTrackingMonitor.php';
        $lastRequestFile = escapeshellarg(self::$lastRequestFile);
        $pidFile = escapeshellarg(self::$pidFile);
        $idleSeconds = escapeshellarg(self::$idleSeconds);
        $osFamily = escapeshellarg(PHP_OS_FAMILY);
        $cmd = "php $monitorScriptPath $lastRequestFile $pidFile $idleSeconds $osFamily";
            error_log("[IdleTrackingMiddleware] Launching monitor: $cmd");
        if (PHP_OS_FAMILY === 'Windows') {
                $proc = @popen('start /B ' . $cmd, 'r');
                if ($proc === false) {
                    error_log("[IdleTrackingMiddleware] Failed to launch monitor process (Windows)");
                } else {
                    error_log("[IdleTrackingMiddleware] Monitor process started (Windows)");
                    pclose($proc);
                }
        } else {
                $proc = @popen($cmd . ' > /dev/null 2>&1 &', 'r');
                if ($proc === false) {
                    error_log("[IdleTrackingMiddleware] Failed to launch monitor process (Unix)");
                } else {
                    error_log("[IdleTrackingMiddleware] Monitor process started (Unix)");
                    pclose($proc);
                }
        }
    }

    public function __invoke(ServerRequestInterface $request, $handler) {
        if (self::$lastRequestFile !== null && file_exists(self::$lastRequestFile)) {
            file_put_contents(self::$lastRequestFile, (string)time());
        }
        return $handler->handle($request);
    }
}
