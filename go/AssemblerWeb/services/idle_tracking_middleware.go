package services

import (
	"fmt"
	"os"
	"strings"
	"sync"
	"time"

	"assembler/common"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
)

var (
	activeHoldsGlobal = make(map[string]time.Time)
	muGlobal          sync.Mutex
)

// IdleTrackingMiddleware creates and returns a Gin middleware for tracking idle time
func IdleTrackingMiddleware(idleSeconds int) gin.HandlerFunc {
	var lastRequest = time.Now()
	var shutdownInitiated = false
	const holdTimeoutSeconds = 300 // Safety timeout for stuck holds

	fmt.Printf("[STARTUP] Configured idleSeconds = %d\n", idleSeconds)
	common.Info(fmt.Sprintf("[STARTUP] Configured idleSeconds = %d", idleSeconds), "IdleTracking")
	fmt.Println("[STARTUP] Starting idle monitor with 10-second check interval")
	common.Info("[STARTUP] Starting idle monitor with 10-second check interval", "IdleTracking")

	// Start idle checker goroutine
	go func() {
		for {
			time.Sleep(10 * time.Second)
			muGlobal.Lock()
			idle := time.Since(lastRequest).Seconds()

			// Clean up expired holds and count active holds
			now := time.Now()
			expiredHolds := []string{}
			for holdID, holdTime := range activeHoldsGlobal {
				holdAge := now.Sub(holdTime).Seconds()
				if holdAge >= holdTimeoutSeconds {
					expiredHolds = append(expiredHolds, holdID)
				}
			}

			for _, holdID := range expiredHolds {
				delete(activeHoldsGlobal, holdID)
				msg := fmt.Sprintf("[MONITOR] Removed expired hold: %s (age: %ds)", holdID, holdTimeoutSeconds)
				fmt.Println(msg)
				common.Info(msg, "IdleTracking")
			}

			activeHoldsCount := len(activeHoldsGlobal)
			monitorMsg := fmt.Sprintf("[MONITOR] IdleTime: %.1fs, Threshold: %ds, ActiveHolds: %d", idle, idleSeconds, activeHoldsCount)
			fmt.Println(monitorMsg)
			common.Info(monitorMsg, "IdleTracking")

			// Only trigger shutdown if idle time exceeded AND no active holds
			if !shutdownInitiated && idle > float64(idleSeconds) && activeHoldsCount == 0 {
				shutdownInitiated = true
				fmt.Println("[MONITOR] Idle timeout exceeded with no active holds, triggering shutdown")
				common.Info("[MONITOR] Idle timeout exceeded with no active holds, triggering shutdown", "IdleTracking")
				muGlobal.Unlock()

				// Log shutdown messages before exiting
				fmt.Println("AssemblerWeb shutting down due to idle timeout...")
				common.Info("AssemblerWeb shutting down due to idle timeout...", "Main")

				// Call Shutdown to log active holds
				Shutdown()

				// Flush logs
				time.Sleep(200 * time.Millisecond)

				fmt.Println("AssemblerWeb stopped")
				common.Info("AssemblerWeb stopped", "Main")

				// Give time for final logs to flush
				time.Sleep(100 * time.Millisecond)

				os.Exit(0)
			}
			muGlobal.Unlock()
		}
	}()

	return func(c *gin.Context) {
		// Generate unique hold ID for this request
		holdID := fmt.Sprintf("hold_%s", strings.ReplaceAll(uuid.New().String(), "-", ""))

		// Set hold before processing to prevent shutdown during long-running requests
		muGlobal.Lock()
		activeHoldsGlobal[holdID] = time.Now()
		lastRequest = time.Now()
		muGlobal.Unlock()

		// Log after releasing lock
		fmt.Printf("[REQUEST] Request started, hold set: %s\n", holdID)
		common.Info(fmt.Sprintf("[REQUEST] Request started, hold set: %s", holdID), "IdleTracking")

		// Process request
		c.Next()

		// Always remove hold after processing (even if error occurs)
		muGlobal.Lock()
		delete(activeHoldsGlobal, holdID)
		lastRequest = time.Now()
		muGlobal.Unlock()

		// Log after releasing lock
		fmt.Println("[REQUEST] Request completed")
		common.Info("[REQUEST] Request completed", "IdleTracking")
		fmt.Printf("[REQUEST] Hold removed: %s\n", holdID)
		common.Info(fmt.Sprintf("[REQUEST] Hold removed: %s", holdID), "IdleTracking")
	}
}

// AcquireHold creates a hold and returns its ID
func AcquireHold() string {
	muGlobal.Lock()
	defer muGlobal.Unlock()
	holdID := fmt.Sprintf("hold_%s", strings.ReplaceAll(uuid.New().String(), "-", ""))
	activeHoldsGlobal[holdID] = time.Now()
	common.Info(fmt.Sprintf("[AcquireHold] Hold set: %s", holdID), "IdleTracking")
	return holdID
}

// ReleaseHold removes a hold by its ID
func ReleaseHold(holdID string) {
	muGlobal.Lock()
	defer muGlobal.Unlock()
	if _, exists := activeHoldsGlobal[holdID]; exists {
		delete(activeHoldsGlobal, holdID)
		common.Info(fmt.Sprintf("[ReleaseHold] Hold removed: %s", holdID), "IdleTracking")
	}
}

func Shutdown() {
	muGlobal.Lock()
	defer muGlobal.Unlock()

	fmt.Printf("[SHUTDOWN] IdleTrackingMiddleware shutting down, active holds: %d\n", len(activeHoldsGlobal))
	common.Info(fmt.Sprintf("[SHUTDOWN] IdleTrackingMiddleware shutting down, active holds: %d", len(activeHoldsGlobal)), "IdleTracking")

	// Log any remaining holds
	for holdID := range activeHoldsGlobal {
		fmt.Printf("[SHUTDOWN] Unreleased hold: %s\n", holdID)
		common.Info(fmt.Sprintf("[SHUTDOWN] Unreleased hold: %s", holdID), "IdleTracking")
	}

	fmt.Println("[SHUTDOWN] IdleTrackingMiddleware stopped")
	common.Info("[SHUTDOWN] IdleTrackingMiddleware stopped", "IdleTracking")
}
