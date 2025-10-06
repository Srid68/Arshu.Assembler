package main

import (
	"fmt"
	"log"
	"net/http"
	"os"
	"strconv"
	"strings"
	"time"

	"github.com/gin-gonic/gin"
	"github.com/skratchdot/open-golang/open"
)

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
					"required": []string{"appSite", "appFile", "engineType"},
					"properties": map[string]interface{}{
						"appSite": map[string]interface{}{
							"type": "string",
						},
						"appView": map[string]interface{}{
							"type": "string",
						},
						"appViewPrefix": map[string]interface{}{
							"type": "string",
						},
						"appFile": map[string]interface{}{
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
	var lock = make(chan struct{}, 1)
	lock <- struct{}{} // initialize lock

	// Start idle checker goroutine
	go func() {
		for {
			time.Sleep(10 * time.Second)
			<-lock
			idle := time.Since(lastRequest).Seconds()
			if !shutdownInitiated && idle > float64(idleSeconds) {
				shutdownInitiated = true
				fmt.Printf("Idle timeout reached (%ds), shutting down server...\n", idleSeconds)
				os.Exit(0)
			}
			lock <- struct{}{}
		}
	}()

	return func(c *gin.Context) {
		<-lock
		lastRequest = time.Now()
		lock <- struct{}{}
		c.Next()
	}
}

func main() {
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
	           os.Getenv("GO_ENV") == "development"

	if isDebug {
		// Set Gin to debug mode for development
		gin.SetMode(gin.DebugMode)
		fmt.Println("[DEBUG] Running in development mode - idle tracking disabled")
	} else {
		// Set Gin to release mode (default/production)
		gin.SetMode(gin.ReleaseMode)
	}

	idleSeconds := 10
	if v := os.Getenv("IDLE_SECONDS"); v != "" {
		if parsed, err := strconv.Atoi(v); err == nil {
			idleSeconds = parsed
		}
	}

	r := gin.Default()

	// Idle Tracking Middleware - enabled by default, disabled only in debug mode
	if !isDebug {
		r.Use(IdleTrackingMiddleware(idleSeconds))
		fmt.Printf("[PRODUCTION] Idle tracking enabled (%d seconds)\n", idleSeconds)
	}

	// Other routes
	r.GET("/", index)
	r.POST("/merge", mergeTemplates)
	r.GET("/openapi.json", openapi)

	// Serve Scalar UI static files
	r.Static("/scalar", "./wwwroot/scalar")

	// Serve all wwwroot files (for test summary HTML files, etc.) - must come after specific routes
	r.StaticFS("/", http.Dir("./wwwroot"))

	// Launch Scalar UI in browser after a short delay
	go func() {
		time.Sleep(500 * time.Millisecond)
		open.Run("http://127.0.0.1:8080")
	}()

	log.Fatal(r.Run(":8080"))
}
