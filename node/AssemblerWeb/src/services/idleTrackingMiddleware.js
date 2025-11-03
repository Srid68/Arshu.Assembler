import { randomUUID } from 'crypto';
import { Logger } from '@arshu/assembler/common';

// Store activeHolds globally for shutdown access
const activeHoldsGlobal = new Map();

// Idle Tracking Middleware
export function idleTrackingMiddleware(idleSeconds = 10) {
  let lastRequest = Date.now();
  let shutdownInitiated = false;
  const holdTimeoutSeconds = 300; // Safety timeout for stuck holds

  console.log(`[STARTUP] Configured idleSeconds = ${idleSeconds}`);
  Logger.info(`[STARTUP] Configured idleSeconds = ${idleSeconds}`, 'IdleTracking');
  console.log('[STARTUP] Starting idle monitor with 10-second check interval');
  Logger.info('[STARTUP] Starting idle monitor with 10-second check interval', 'IdleTracking');

  // Start idle checker interval
  setInterval(() => {
    if (shutdownInitiated) return;

    const idle = (Date.now() - lastRequest) / 1000;
    const now = Date.now();

    // Clean up expired holds and count active holds
    const expiredHolds = [];
    for (const [holdId, holdTime] of activeHoldsGlobal.entries()) {
      const holdAge = (now - holdTime) / 1000;
      if (holdAge >= holdTimeoutSeconds) {
        expiredHolds.push(holdId);
      }
    }

    for (const holdId of expiredHolds) {
      activeHoldsGlobal.delete(holdId);
      const msg = `[MONITOR] Removed expired hold: ${holdId} (age: ${holdTimeoutSeconds}s)`;
      console.log(msg);
      Logger.info(msg, 'IdleTracking');
    }

    const activeHoldsCount = activeHoldsGlobal.size;
    const monitorMsg = `[MONITOR] IdleTime: ${idle.toFixed(1)}s, Threshold: ${idleSeconds}s, ActiveHolds: ${activeHoldsCount}`;
    console.log(monitorMsg);
    Logger.info(monitorMsg, 'IdleTracking');

    // Only trigger shutdown if idle time exceeded AND no active holds
    if (idle > idleSeconds && activeHoldsCount === 0) {
      shutdownInitiated = true;
      console.log('[MONITOR] Idle timeout exceeded with no active holds, triggering shutdown');
      Logger.info('[MONITOR] Idle timeout exceeded with no active holds, triggering shutdown', 'IdleTracking');
      
      // Log shutdown messages before exiting
      console.log('AssemblerWeb shutting down due to idle timeout...');
      Logger.info('AssemblerWeb shutting down due to idle timeout...', 'Main');
      
      // Call shutdown to log active holds
      shutdown();
      
      // Give time for logs to flush
      setTimeout(() => {
        console.log('AssemblerWeb stopped');
        Logger.info('AssemblerWeb stopped', 'Main');
        
        // Give time for final logs to flush
        setTimeout(() => {
          process.exit(0);
        }, 100);
      }, 200);
    }
  }, 10000);

  return (req, res, next) => {
    // Generate unique hold ID for this request
    const holdId = `hold_${randomUUID().replace(/-/g, '')}`;

    // Set hold before processing to prevent shutdown during long-running requests
    activeHoldsGlobal.set(holdId, Date.now());
    lastRequest = Date.now();
    
    // Log after setting hold
    console.log(`[REQUEST] Request started, hold set: ${holdId}`);
    Logger.info(`[REQUEST] Request started, hold set: ${holdId}`, 'IdleTracking');

    // Remove hold after response finishes (even if error occurs)
    res.on('finish', () => {
      activeHoldsGlobal.delete(holdId);
      lastRequest = Date.now();
      
      // Log after removing hold
      console.log('[REQUEST] Request completed');
      Logger.info('[REQUEST] Request completed', 'IdleTracking');
      console.log(`[REQUEST] Hold removed: ${holdId}`);
      Logger.info(`[REQUEST] Hold removed: ${holdId}`, 'IdleTracking');
    });

    // Also handle aborted requests
    res.on('close', () => {
      if (activeHoldsGlobal.has(holdId)) {
        activeHoldsGlobal.delete(holdId);
        lastRequest = Date.now();
        console.log(`[REQUEST] Request aborted, hold removed: ${holdId}`);
        Logger.info(`[REQUEST] Request aborted, hold removed: ${holdId}`, 'IdleTracking');
      }
    });

    next();
  };
}

/**
 * Acquire a hold with the specified ID to prevent shutdown during critical operations
 * Matches C# method signature at AssemblerWeb/Services/IdleTrackingMiddleware.cs:28
 * @param {string} holdId - The ID for this hold
 */
export function acquireHold(holdId) {
  activeHoldsGlobal.set(holdId, Date.now());
  Logger.info(`[AcquireHold] Hold set: ${holdId}`, 'IdleTracking');
}

/**
 * Release a previously acquired hold
 * Matches C# method signature at AssemblerWeb/Services/IdleTrackingMiddleware.cs:38
 * @param {string} holdId - The ID of the hold to release
 */
export function releaseHold(holdId) {
  if (activeHoldsGlobal.has(holdId)) {
    activeHoldsGlobal.delete(holdId);
    Logger.info(`[ReleaseHold] Hold removed: ${holdId}`, 'IdleTracking');
  }
}

export function shutdown() {
  console.log(`[SHUTDOWN] IdleTrackingMiddleware shutting down, active holds: ${activeHoldsGlobal.size}`);
  Logger.info(`[SHUTDOWN] IdleTrackingMiddleware shutting down, active holds: ${activeHoldsGlobal.size}`, 'IdleTracking');
  
  // Log any remaining holds
  for (const holdId of activeHoldsGlobal.keys()) {
    console.log(`[SHUTDOWN] Unreleased hold: ${holdId}`);
    Logger.info(`[SHUTDOWN] Unreleased hold: ${holdId}`, 'IdleTracking');
  }

  console.log('[SHUTDOWN] IdleTrackingMiddleware stopped');
  Logger.info('[SHUTDOWN] IdleTrackingMiddleware stopped', 'IdleTracking');
}

