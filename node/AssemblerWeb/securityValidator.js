import { loadAppSites } from './appSitesConfig.js';

// Maximum parameter length to prevent DoS attacks
const PARAM_MAX_LENGTH = 256;

// Valid engine types allowlist
const VALID_ENGINE_TYPES = new Set(['normal', 'preprocess']);

// Cached ValidAppSites - loaded on first request
let cachedValidAppSites = null;

/**
 * Gets the valid AppSites from cache, or loads from appsites.csv (generating if needed)
 */
export function getValidAppSites(wwwrootPath) {
  if (cachedValidAppSites !== null) {
    return cachedValidAppSites;
  }

  cachedValidAppSites = loadAppSites(wwwrootPath);
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
