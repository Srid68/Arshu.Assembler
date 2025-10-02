package main

import (
	"fmt"
	"log"
	"net/http"
	"os"
	"path/filepath"
	"sort"
	"strconv"
	"strings"
	"time"

	"github.com/gin-gonic/gin"
	"github.com/skratchdot/open-golang/open"

	"assembler/template_common"
	"assembler/template_engine"
	"assembler/template_loader"
)

// MergeRequest represents the request structure for template merging
type MergeRequest struct {
	AppSite       *string `json:"appSite" binding:"required"`
	AppView       *string `json:"appView"`
	AppViewPrefix *string `json:"appViewPrefix"`
	AppFile       *string `json:"appFile" binding:"required"`
	EngineType    *string `json:"engineType" binding:"required"`
}

// mergeTemplates handles the POST /merge endpoint
func mergeTemplates(c *gin.Context) {
	var req MergeRequest
	if err := c.ShouldBindJSON(&req); err != nil {
		c.JSON(http.StatusBadRequest, gin.H{"error": err.Error()})
		return
	}

	fmt.Printf("/merge endpoint called with: app_site=%v, app_file=%v, engine_type=%v, app_view=%v, app_view_prefix=%v\n",
		safeString(req.AppSite), safeString(req.AppFile), safeString(req.EngineType), safeString(req.AppView), safeString(req.AppViewPrefix))

	// Validate required fields
	if req.AppSite == nil || *req.AppSite == "" {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Missing required field: appSite"})
		return
	}
	if req.AppFile == nil || *req.AppFile == "" {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Missing required field: appFile"})
		return
	}
	if req.EngineType == nil || *req.EngineType == "" {
		c.JSON(http.StatusBadRequest, gin.H{"error": "Missing required field: engineType"})
		return
	}

	assemblerWebDirPath, _ := template_common.GetAssemblerWebDirPath()
	rootDirPath := assemblerWebDirPath // go/AssemblerWeb/wwwroot
	fmt.Printf("[DEBUG] rootDirPath: %v\n", rootDirPath)

	serverStart := time.Now()
	engineStart := time.Now()
	var mergedHTML string
	if strings.EqualFold(*req.EngineType, "PreProcess") {
		templates := template_loader.LoadProcessGetTemplateFiles(rootDirPath, *req.AppSite)
		engine := template_engine.NewEnginePreProcess(safeString(req.AppViewPrefix))
		mergedHTML = engine.MergeTemplates(*req.AppSite, *req.AppFile, safeString(req.AppView), templates.Templates, true)
	} else {
		templates := template_loader.LoadGetTemplateFiles(rootDirPath, *req.AppSite)
		engine := template_engine.NewEngineNormal(safeString(req.AppViewPrefix))
		mergedHTML = engine.MergeTemplates(*req.AppSite, *req.AppFile, safeString(req.AppView), templates, true)
	}
	engineTimeMs := float64(time.Since(engineStart).Microseconds()) / 1000.0
	serverTimeMs := float64(time.Since(serverStart).Microseconds()) / 1000.0
	responseObj := map[string]interface{}{
		"html": mergedHTML,
		"timing": map[string]interface{}{
			"serverTimeMs": serverTimeMs,
			"engineTimeMs": engineTimeMs,
		},
	}
	c.Header("Content-Type", "application/json")
	c.JSON(http.StatusOK, responseObj)
}

// index handles the GET / endpoint
func index(c *gin.Context) {
	// Use template_common to get the correct wwwroot/AppSites path
	assemblerWebDirPath, _ := template_common.GetAssemblerWebDirPath()
	appSitesPath := filepath.Join(assemblerWebDirPath, "AppSites")

	var optionsList []string

	if _, err := os.Stat(appSitesPath); err == nil {
		testDirs, err := os.ReadDir(appSitesPath)
		if err == nil {
			for _, entry := range testDirs {
				if !entry.IsDir() {
					continue
				}
				testDir := entry.Name()
				if strings.EqualFold(testDir, "roottemplate.html") {
					continue
				}

				testDirPath := filepath.Join(appSitesPath, testDir)

				// Find root html files
				htmlFiles := getHTMLFiles(testDirPath)

				// Views subdir
				viewsPath := filepath.Join(testDirPath, "Views")
				hasViews := exists(viewsPath)

				for _, htmlFile := range htmlFiles {
					appViewPrefix := ""
					if len(htmlFile) >= 6 {
						appViewPrefix = htmlFile[:6]
					} else {
						appViewPrefix = htmlFile
					}
					optionValue := fmt.Sprintf("%s,%s,,%s", testDir, htmlFile, appViewPrefix)
					optionText := fmt.Sprintf("%s - %s", testDir, htmlFile)
					optionsList = append(optionsList, fmt.Sprintf("<option value=\"%s\">%s</option>", optionValue, optionText))
				}

				if hasViews {
					viewFiles := getHTMLFiles(viewsPath)

					var appViewValues []string
					for _, vf := range viewFiles {
						idx := strings.Index(strings.ToLower(vf), "content")
						if idx > 0 {
							viewPart := vf[:idx]
							if len(viewPart) > 0 {
								appViewValues = append(appViewValues, strings.ToUpper(viewPart[:1])+viewPart[1:])
							}
						}
					}

					// Remove duplicates
					appViewValues = removeDuplicates(appViewValues)

					for _, rootFile := range htmlFiles {
						rootAppViewPrefix := ""
						if len(rootFile) >= 6 {
							rootAppViewPrefix = rootFile[:6]
						} else {
							rootAppViewPrefix = rootFile
						}

						var matchingViewPrefix string
						for _, vf := range viewFiles {
							idx := strings.Index(strings.ToLower(vf), "content")
							if idx > 0 {
								prefix := vf[:idx]
								if len(prefix) > 0 && strings.HasPrefix(strings.ToLower(rootFile), strings.ToLower(prefix)) {
									matchingViewPrefix = prefix
									break
								}
							}
						}

						if matchingViewPrefix != "" {
							for _, appView := range appViewValues {
								optionValueAppView := fmt.Sprintf("%s,%s,%s,%s", testDir, rootFile, appView, rootAppViewPrefix)
								optionTextAppView := fmt.Sprintf("%s - %s (AppView: %s)", testDir, rootFile, appView)
								optionsList = append(optionsList, fmt.Sprintf("<option value=\"%s\">%s</option>", optionValueAppView, optionTextAppView))
							}
						}
					}
				}
			}
		}
	}

	options := strings.Join(optionsList, "\n        ")
	templatePath := filepath.Join(appSitesPath, "roottemplate.html")
	htmlBytes, err := os.ReadFile(templatePath)
	var html string
	if err != nil {
		html = fmt.Sprintf("<html><body>Template not found at %s</body></html>", templatePath)
	} else {
		html = string(htmlBytes)
	}
	html = strings.Replace(html, "<!--OPTIONS-->", options, -1)

	c.Header("Content-Type", "text/html")
	c.String(http.StatusOK, html)
}

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

// Helper functions
func safeString(s *string) string {
	if s == nil {
		return ""
	}
	return *s
}

func getHTMLFiles(dirPath string) []string {
	var htmlFiles []string
	files, err := os.ReadDir(dirPath)
	if err != nil {
		return htmlFiles
	}

	for _, file := range files {
		if !file.IsDir() && strings.HasSuffix(strings.ToLower(file.Name()), ".html") {
			name := file.Name()
			ext := filepath.Ext(name)
			htmlFiles = append(htmlFiles, name[:len(name)-len(ext)])
		}
	}

	sort.Strings(htmlFiles)
	return htmlFiles
}

func exists(path string) bool {
	_, err := os.Stat(path)
	return !os.IsNotExist(err)
}

func removeDuplicates(slice []string) []string {
	keys := make(map[string]bool)
	var result []string
	for _, item := range slice {
		if !keys[item] {
			keys[item] = true
			result = append(result, item)
		}
	}
	return result
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

	// Set Gin to release mode to reduce verbosity
	gin.SetMode(gin.ReleaseMode)

	idleSeconds := 10
	if v := os.Getenv("IDLE_SECONDS"); v != "" {
		if parsed, err := strconv.Atoi(v); err == nil {
			idleSeconds = parsed
		}
	}

	r := gin.Default()

	// Idle Tracking Middleware
	if gin.Mode() != gin.DebugMode {
		r.Use(IdleTrackingMiddleware(idleSeconds))
	}

	// Serve Scalar UI static files
	r.Static("/scalar", "./wwwroot/scalar")

	// Other routes
	r.GET("/", index)
	r.POST("/merge", mergeTemplates)
	r.GET("/openapi.json", openapi)

	// Launch Scalar UI in browser after a short delay
	go func() {
		time.Sleep(500 * time.Millisecond)
		open.Run("http://127.0.0.1:8080/scalar")
	}()

	log.Fatal(r.Run(":8080"))
}
