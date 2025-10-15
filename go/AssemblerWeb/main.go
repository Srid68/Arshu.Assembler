package main

import (
	"fmt"
	"log"
	"net/http"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"sync"
	"time"

	"assembler/common"
	"assembler/config"
	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
	"github.com/skratchdot/open-golang/open"
)

// Global variable to store project directory path (similar to C#'s ContentRootPath)
var projectDirectory string

// openapi handles the GET /openapi.json endpoint to serve OpenAPI specification
func openapi(c *gin.Context) {
	spec := map[string]interface{}{
		"openapi": "3.0.3",
		"info": map[string]interface{}{
			"title":   "Go Assembler API",
			"version": "1.0.0",
		},
		"paths": map[string]interface{}{
			"/": map[string]interface{}{
				"get": map[string]interface{}{
					"tags":        []string{"Assembler"},
					"summary":     "Root page",
					"description": "Returns the root HTML page with template options.",
					"responses": map[string]interface{}{
						"200": map[string]interface{}{
							"description": "Root HTML page",
							"content": map[string]interface{}{
								"text/html": map[string]interface{}{
									"schema": map[string]interface{}{
										"type": "string",
									},
								},
							},
						},
					},
				},
			},
			"/merge": map[string]interface{}{
				"post": map[string]interface{}{
					"tags":        []string{"Assembler"},
					"summary":     "Merge templates",
					"description": "Merges templates using the specified engine type",
					"requestBody": map[string]interface{}{
						"required": true,
						"content": map[string]interface{}{
							"application/json": map[string]interface{}{
								"schema": map[string]interface{}{
									"$ref": "#/components/schemas/MergeRequest",
								},
							},
						},
					},
					"responses": map[string]interface{}{
						"200": map[string]interface{}{
							"description": "Merged template output",
							"content": map[string]interface{}{
								"text/html": map[string]interface{}{
									"schema": map[string]interface{}{
										"type": "string",
									},
								},
							},
						},
					},
				},
			},
		},
		"components": map[string]interface{}{
			"schemas": map[string]interface{}{
				"MergeRequest": map[string]interface{}{
					"type":     "object",
					"required": []string{"appSite", "engineType"},
					"properties": map[string]interface{}{
						"appSite": map[string]interface{}{
							"type": "string",
						},
						"appView": map[string]interface{}{
							"type": "string",
						},
						"engineType": map[string]interface{}{
							"type": "string",
						},
					},
				},
			},
		},
	}

	c.JSON(http.StatusOK, spec)
}

// Idle Tracking Middleware
func IdleTrackingMiddleware(idleSeconds int) gin.HandlerFunc {
	var lastRequest = time.Now()
	var shutdownInitiated = false
	var activeHolds = make(map[string]time.Time)
	var mu sync.Mutex
	const holdTimeoutSeconds = 300 // Safety timeout for stuck holds

	fmt.Printf("[STARTUP] Configured idleSeconds = %d\n", idleSeconds)
	fmt.Println("[STARTUP] Starting idle monitor with 10-second check interval")

	// Start idle checker goroutine
	go func() {
		for {
			time.Sleep(10 * time.Second)
			mu.Lock()
			idle := time.Since(lastRequest).Seconds()

			// Clean up expired holds and count active holds
			now := time.Now()
			expiredHolds := []string{}
			for holdID, holdTime := range activeHolds {
				holdAge := now.Sub(holdTime).Seconds()
				if holdAge >= holdTimeoutSeconds {
					expiredHolds = append(expiredHolds, holdID)
				}
			}

			for _, holdID := range expiredHolds {
				delete(activeHolds, holdID)
				fmt.Printf("[MONITOR] Removed expired hold: %s (age: %ds)\n", holdID, holdTimeoutSeconds)
			}

			activeHoldsCount := len(activeHolds)
			fmt.Printf("[MONITOR] IdleTime: %.1fs, Threshold: %ds, ActiveHolds: %d\n", idle, idleSeconds, activeHoldsCount)

			// Only trigger shutdown if idle time exceeded AND no active holds
			if !shutdownInitiated && idle > float64(idleSeconds) && activeHoldsCount == 0 {
				shutdownInitiated = true
				fmt.Println("[MONITOR] Idle timeout exceeded with no active holds, triggering shutdown")
				common.Info(fmt.Sprintf("Idle timeout reached (%ds) with no active requests, shutting down server...", idleSeconds), "IdleTracking")
				mu.Unlock()
				os.Exit(0)
			}
			mu.Unlock()
		}
	}()

	return func(c *gin.Context) {
		// Generate unique hold ID for this request
		holdID := fmt.Sprintf("hold_%s", strings.ReplaceAll(uuid.New().String(), "-", ""))

		// Set hold before processing to prevent shutdown during long-running requests
		mu.Lock()
		activeHolds[holdID] = time.Now()
		fmt.Printf("[REQUEST] Request started, hold set: %s\n", holdID)
		lastRequest = time.Now()
		mu.Unlock()

		// Process request
		c.Next()

		// Always remove hold after processing (even if error occurs)
		mu.Lock()
		delete(activeHolds, holdID)
		fmt.Printf("[REQUEST] Request completed, hold removed: %s\n", holdID)
		lastRequest = time.Now()
		mu.Unlock()
	}
}

func main() {
	// Configure Logger - set global projectDirectory variable
	_, projectDirectory = common.GetAssemblerWebDirPath()
	templateAnalysisDir := filepath.Join(projectDirectory, "template_analysis")
	logsDir := filepath.Join(templateAnalysisDir, "logs")
	os.MkdirAll(logsDir, 0755)

	// Configure separate log files for each context
	contextLogFiles := map[string]string{
		"LoaderNormal":      filepath.Join(logsDir, "go_loadernormal.log"),
		"LoaderPreProcess":  filepath.Join(logsDir, "go_loaderpreprocess.log"),
		"EngineNormal":      filepath.Join(logsDir, "go_enginenormal.log"),
		"EnginePreProcess":  filepath.Join(logsDir, "go_enginepreprocess.log"),
		"Main":              filepath.Join(logsDir, "go_main.log"),
		"MergeEndpoint":     filepath.Join(logsDir, "go_mergeendpoint.log"),
	}

	common.Configure(common.DEBUG, "", false, common.ROTATION_NONE)
	common.ConfigureContextLogFiles(contextLogFiles)
	common.Info("AssemblerWeb starting up", "Main")

	// Load ConfigUtil (AppSites and Scenarios)
	assemblerWebDirPath, _ := common.GetAssemblerWebDirPath()
	if err := config.Load(assemblerWebDirPath); err != nil {
		fmt.Printf("[WARNING] Failed to load ConfigUtil: %v\n", err)
		common.Warn(fmt.Sprintf("Failed to load ConfigUtil: %v", err), "Main")
	}

	// Parse command line args
	skipIdleTracking := false
	port := "8080" // Default port

	// Check PORT environment variable first
	if envPort := os.Getenv("PORT"); envPort != "" {
		port = envPort
	}

	// Command line --port flag overrides environment variable
	for i, arg := range os.Args[1:] {
		if arg == "--skipIdleTracking" {
			skipIdleTracking = true
		}
		if arg == "--port" && i+1 < len(os.Args[1:]) {
			port = os.Args[1:][i+1]
		}
	}

	// OS environment detection
	if _, err := os.Stat("/proc/sys/kernel/osrelease"); err == nil {
		data, err := os.ReadFile("/proc/sys/kernel/osrelease")
		if err == nil && strings.Contains(string(data), "microsoft") {
			fmt.Println("[WSL] Running in WSL environment")
		} else if _, err := os.Stat("/etc/os-release"); err == nil {
			osRelease, err := os.ReadFile("/etc/os-release")
			distro := "Unknown Linux"
			if err == nil {
				for _, line := range strings.Split(string(osRelease), "\n") {
					if strings.HasPrefix(line, "ID=") {
						distro = strings.Trim(line[3:], "\"")
						break
					}
				}
			}
			fmt.Printf("[Linux] Running in %s environment\n", distro)
		} else {
			fmt.Println("[Linux] Running in Linux environment")
		}
	} else {
		fmt.Println("[Windows] Running in Windows environment")
	}
	fmt.Println("Starting Go AssemblerWeb server...")

	// Check if running in VSCode debugger or explicitly in debug mode
	isDebug := os.Getenv("DEBUG") == "true" ||
		os.Getenv("VSCODE_DEBUG") == "true" ||
		os.Getenv("IDLE_TRACKER_DISABLED") == "true" ||
		os.Getenv("APP_ENV") == "development" ||
		skipIdleTracking

	if isDebug {
		// Set Gin to debug mode for development
		gin.SetMode(gin.DebugMode)
		fmt.Println("[IdleTracking] Idle tracking DISABLED")
	} else {
		// Set Gin to release mode (default/production)
		gin.SetMode(gin.ReleaseMode)
		fmt.Println("[IdleTracking] Idle tracking ENABLED")
	}

	r := gin.Default()

	// Idle Tracking Middleware - enabled by default, disabled only in debug mode
	if !isDebug {
		idleSeconds := 10
		if v := os.Getenv("IDLE_SECONDS"); v != "" {
			if parsed, err := strconv.Atoi(v); err == nil {
				idleSeconds = parsed
			}
		}
		r.Use(IdleTrackingMiddleware(idleSeconds))
		fmt.Printf("[PRODUCTION] Idle tracking enabled (%d seconds)\n", idleSeconds)
	}

	// Serve Scalar UI static files
	r.Static("/scalar", "./wwwroot/scalar")

	// API routes
	r.GET("/", index)
	r.GET("/api/scenarios", scenarios)
	r.POST("/merge", mergeTemplates)
	r.GET("/openapi.json", openapi)

	// Test endpoints
	r.POST("/test/standard", testStandard)
	r.POST("/test/advanced", testAdvanced)
	r.POST("/test/performance", testPerformance)
	r.POST("/test/consolidate-performance", testConsolidatePerformance)

	// Report endpoint
	r.POST("/api/report", getReport)

	// Serve static files from wwwroot (HTML, JSON, etc.) - use NoRoute to avoid conflicts
	r.NoRoute(func(c *gin.Context) {
		path := c.Request.URL.Path
		// Try to serve from wwwroot
		filePath := "./wwwroot" + path
		if _, err := os.Stat(filePath); err == nil {
			c.File(filePath)
			return
		}
		// If file not found, return 404
		c.JSON(404, gin.H{"error": "Not Found"})
	})

	// Launch Scalar UI in browser after a short delay
	go func() {
		time.Sleep(500 * time.Millisecond)
		open.Run(fmt.Sprintf("http://127.0.0.1:%s", port))
	}()

	log.Fatal(r.Run(":" + port))
}
