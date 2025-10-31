import fsSync from 'fs';

// Import ConfigUtil dynamically based on environment
const assemblerBasePath = fsSync.existsSync('/app/wwwroot') ? '../Assembler/src' : '../../Assembler/src';
const configModule = await import(`${assemblerBasePath}/config/index.js`);
const { ConfigUtil } = configModule;

// Maximum parameter length to prevent DoS attacks
const PARAM_MAX_LENGTH = 256;

// Valid engine types allowlist
const VALID_ENGINE_TYPES = new Set(['normal', 'preprocess']);

// Cached ValidAppSites - loaded on first request
let cachedValidAppSites = null;

/**
 * Gets the valid AppSites from ConfigUtil. Throws if not loaded.
 */
export function getValidAppSites() {
  if (cachedValidAppSites !== null) {
    return cachedValidAppSites;
  }

  cachedValidAppSites = ConfigUtil.getAppSites();
  return cachedValidAppSites;
}

/**
 * Clears the AppSites cache - useful for development/testing
 */
export function clearAppSitesCache() {
  cachedValidAppSites = null;
}

/**
 * Validates if a path component is safe (no traversal, invalid chars, or excessive length)
 */
export function isValidPathComponent(value) {
  if (!value || typeof value !== 'string') {
    return false;
  }

  const v = value.trim();
  if (v === '') {
    return false;
  }

  // Check parameter length to prevent DoS
  if (v.length > PARAM_MAX_LENGTH) {
    return false;
  }

  // Check for path traversal attempts
  if (v.includes('..') || v.includes('/') || v.includes('\\')) {
    return false;
  }

  // Check for other suspicious characters
  const invalidChars = ['<', '>', ':', '"', '|', '?', '*', '\0'];
  for (const char of v) {
    if (invalidChars.includes(char)) {
      return false;
    }
    // Check for control characters
    if (char.charCodeAt(0) < 32) {
      return false;
    }
  }

  return true;
}

/**
 * Validates engine type against allowlist (case-insensitive)
 */
export function isValidEngineType(engineType) {
  if (!engineType || typeof engineType !== 'string') {
    return false;
  }
  return VALID_ENGINE_TYPES.has(engineType.toLowerCase());
}

/**
 * Validates app_site against allowlist (case-insensitive)
 */
export function isValidAppSite(appSite, validAppSites) {
  if (!appSite || typeof appSite !== 'string') {
    return false;
  }
  return validAppSites.has(appSite.toLowerCase());
}

// Maximum content sizes to prevent DDOS attacks
export const MAX_LOG_FILE_SIZE = 500 * 1024; // 500 KB
// Buffer allowance for output size validation (50 KB)
export const OUTPUT_SIZE_BUFFER = 50 * 1024;

/**
 * Validates log content format and size similar to C# implementation
 * @param {string} logContent
 * @returns {{valid:boolean, errorMessage?:string}}
 */
export function isValidLogContent(logContent) {
  if (!logContent || typeof logContent !== 'string') {
    return { valid: false, errorMessage: 'Log content is empty' };
  }

  if (Buffer.byteLength(logContent, 'utf8') > MAX_LOG_FILE_SIZE) {
    return { valid: false, errorMessage: 'Log file exceeds maximum size limit (500 KB)' };
  }

  const pattern = /^[\[\]0-9:\-\s\.TZ]+\s*(DEBUG|INFO|WARN|ERROR|TRACE|FATAL)?:?\s*.+$/im;
  const lines = logContent.split(/\r?\n/).filter(l => l.trim() !== '');
  if (lines.length === 0) return { valid: true };

  let validLines = 0;
  for (const line of lines) {
    if (pattern.test(line) || line.startsWith('    at ') || line.startsWith('\tat ')) {
      validLines++;
    }
  }

  if ((validLines / lines.length) < 0.5) {
    return { valid: false, errorMessage: 'Log content does not match expected format' };
  }

  return { valid: true };
}

/**
 * Validates output size against template total size with fixed buffer
 */
export function isValidOutputSizeWithBuffer(htmlContent, templateTotalSize) {
  if (!htmlContent) return true;
  const outputSize = Buffer.byteLength(htmlContent, 'utf8');
  if (templateTotalSize > 0) {
    const maxAllowed = templateTotalSize + OUTPUT_SIZE_BUFFER;
    return outputSize <= maxAllowed;
  }
  return false; // if template size unknown, reject
}

/**
 * Get template total size for an appSite and appView from ConfigUtil
 */
export function getTemplateTotalSize(appSite, appView = '') {
  try {
    const scenarios = ConfigUtil.getScenarios();
    const match = scenarios.find(s =>
      s.appSite.toLowerCase() === appSite.toLowerCase() &&
      (s.appView || '').toLowerCase() === (appView || '').toLowerCase()
    );
    return match ? (match.totalSize || 0) : 0;
  } catch (e) {
    return 0;
  }
}
