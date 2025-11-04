package main

import (
	"assemblerweb/endpoint"
	"assemblerweb/services"
	"fmt"
	"log"
	"net/http"
	"os"
	"os/signal"
	"path/filepath"
	"strconv"
	"strings"
	"syscall"
	"time"

	"assembler/config"
	Logger "github.com/srid68/arshu/common"

	"github.com/gin-gonic/gin"
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

func main() {
	// Set projectDirectory to current working directory (same as C#/Rust)
	projectDirectory, _ = os.Getwd()
	wwwrootPath := filepath.Join(projectDirectory, "wwwroot")

	// Configure Logger
	templateAnalysisDir := filepath.Join(projectDirectory, "template_analysis")
	logsDir := filepath.Join(templateAnalysisDir, "logs")
	os.MkdirAll(logsDir, 0755)

	// Configure separate log files for global contexts only
	// Note: Endpoint-specific contexts are configured per-endpoint using AddContextLogFiles
	contextLogFiles := map[string]string{
		"Main":         filepath.Join(logsDir, "go_main.log"),
		"IdleTracking": filepath.Join(logsDir, "go_idletracking.log"),
	}

	// Configure logger (no main log file - only context files)
	Logger.Configure(Logger.DEBUG, false, Logger.ROTATION_HOURLY)

	// Set logs directory for clearing
	Logger.SetLogsDirectory(logsDir)

	// Check if running in VSCode debugger or explicitly in debug mode
	isDebug := os.Getenv("DEBUG") == "true" ||
		os.Getenv("VSCODE_DEBUG") == "true" ||
		os.Getenv("APP_ENV") == "development"

	// Clear logs based on build mode - always clear in debug/development
	if isDebug {
		Logger.ClearLogs()
	} else {
		Logger.ClearOldLogs(7)
	}

	// Configure context log files AFTER clearing (which would delete them)
	Logger.ConfigureContextLogFiles(contextLogFiles)

	Logger.Info("AssemblerWeb starting up", "Main")

	// Load ConfigUtil (AppSites and Scenarios)
	if err := config.Load(wwwrootPath); err != nil {
		fmt.Printf("[WARNING] Failed to load ConfigUtil: %v\n", err)
		Logger.Warn(fmt.Sprintf("Failed to load ConfigUtil: %v", err), "Main")
	}

	// Parse command line args
	skipIdleTracking := false
	port := "8040" // Default port

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

	// Determine if idle tracking should be enabled
	// Command line args and explicit env vars take precedence
	var idleTrackingEnabled bool
	if skipIdleTracking {
		idleTrackingEnabled = false // --skipIdleTracking flag explicitly disables
	} else {
		idleTrackerDisabledEnv := os.Getenv("IDLE_TRACKER_DISABLED")
		if idleTrackerDisabledEnv == "false" {
			idleTrackingEnabled = true // Explicitly enable idle tracking
		} else if idleTrackerDisabledEnv == "true" {
			idleTrackingEnabled = false // Explicitly disable idle tracking
		} else {
			idleTrackingEnabled = !isDebug // Default: disable in debug mode
		}
	}

	if isDebug {
		// Set Gin to debug mode for development
		gin.SetMode(gin.DebugMode)
	} else {
		// Set Gin to release mode (default/production)
		gin.SetMode(gin.ReleaseMode)
	}

	if idleTrackingEnabled {
		fmt.Println("[IdleTracking] Idle tracking ENABLED")
	} else {
		fmt.Println("[IdleTracking] Idle tracking DISABLED")
	}

	r := gin.Default()

	// Idle Tracking Middleware
	if idleTrackingEnabled {
		idleSeconds := 10
		if v := os.Getenv("IDLE_SECONDS"); v != "" {
			if parsed, err := strconv.Atoi(v); err == nil {
				idleSeconds = parsed
			}
		}
		r.Use(services.IdleTrackingMiddleware(idleSeconds))
		fmt.Printf("[PRODUCTION] Idle tracking enabled (%d seconds)\n", idleSeconds)
	}

	// Serve Scalar UI static files
	r.Static("/scalar", "./wwwroot/scalar")

	// Map assembler endpoints using the centralized function
	endpoint.MapAssemblerEndpoints(r)
	endpoint.MapAssemblerTestEndpoints(r)

	r.GET("/openapi.json", openapi)

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

	// Setup signal handling for graceful shutdown
	quit := make(chan os.Signal, 1)
	signal.Notify(quit, os.Interrupt, syscall.SIGTERM)

	// Run server in goroutine
	go func() {
		if err := r.Run(":" + port); err != nil {
			log.Fatalf("Failed to run server: %v", err)
		}
	}()

	// Wait for interrupt signal
	<-quit

	// Shutdown logging
	if idleTrackingEnabled {
		services.Shutdown()
	}
	Logger.Info("AssemblerWeb shutting down...", "Main")
	Logger.Info("AssemblerWeb stopped", "Main")
}
