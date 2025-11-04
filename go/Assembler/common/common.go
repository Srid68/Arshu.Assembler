package common

// Re-export Logger functions from arshu-common
import (
	arshu "github.com/srid68/arshu-common/common"
)

// Re-export types
type LogLevel = arshu.LogLevel
type LogRotation = arshu.LogRotation

// Re-export constants
const (
	DEBUG           = arshu.DEBUG
	INFO            = arshu.INFO
	WARN            = arshu.WARN
	ERROR           = arshu.ERROR
	NONE            = arshu.NONE
	ROTATION_NONE   = arshu.ROTATION_NONE
	ROTATION_HOURLY = arshu.ROTATION_HOURLY
	ROTATION_DAILY  = arshu.ROTATION_DAILY
)

// Re-export Logger functions
var (
	Configure              = arshu.Configure
	SetLogsDirectory       = arshu.SetLogsDirectory
	SetLogLevel            = arshu.SetLogLevel
	GetLogLevel            = arshu.GetLogLevel
	Debug                  = arshu.Debug
	Info                   = arshu.Info
	Warn                   = arshu.Warn
	Error                  = arshu.Error
	EnableFileLogging      = arshu.EnableFileLogging
	DisableFileLogging     = arshu.DisableFileLogging
	ClearLogs              = arshu.ClearLogs
	ClearOldLogs           = arshu.ClearOldLogs
	EnableConsoleOutput    = arshu.EnableConsoleOutput
	DisableConsoleOutput   = arshu.DisableConsoleOutput
	ConfigureContextLogFiles = arshu.ConfigureContextLogFiles
	AddContextLogFiles     = arshu.AddContextLogFiles
	RemoveContextLogFiles  = arshu.RemoveContextLogFiles
	Flush                  = arshu.Flush
)
