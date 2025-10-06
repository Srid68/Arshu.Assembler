import fs from 'fs';
import path from 'path';

/**
 * Discovers AppSites by scanning the AppSites folder and generates appsites.csv
 */
function generateAppSitesCsv(wwwrootPath) {
  const appSitesPath = path.join(wwwrootPath, 'AppSites');
  const appDataPath = path.join(wwwrootPath, 'App_Data');
  const csvFilePath = path.join(appDataPath, 'appsites.csv');

  if (!fs.existsSync(appSitesPath)) {
    throw new Error(`AppSites directory not found: ${appSitesPath}`);
  }

  // Ensure App_Data directory exists
  if (!fs.existsSync(appDataPath)) {
    fs.mkdirSync(appDataPath, { recursive: true });
  }

  // Get all directories in AppSites folder
  const entries = fs.readdirSync(appSitesPath, { withFileTypes: true });
  let appSites = entries
    .filter(entry => entry.isDirectory())
    .map(entry => entry.name);

  // Add Index as it's a valid AppSite
  if (!appSites.some(s => s.toLowerCase() === 'index')) {
    appSites.push('Index');
  }

  // Sort for consistency
  appSites.sort();

  // Write as CSV (comma-delimited)
  const csv = appSites.join(',');
  fs.writeFileSync(csvFilePath, csv);

  console.log(`[AppSitesConfig] Generated appsites.csv with ${appSites.length} AppSites`);
}

/**
 * Loads AppSites from appsites.csv, generates it if it doesn't exist
 */
export function loadAppSites(wwwrootPath) {
  const appDataPath = path.join(wwwrootPath, 'App_Data');
  const csvFilePath = path.join(appDataPath, 'appsites.csv');

  // Generate appsites.csv if it doesn't exist
  if (!fs.existsSync(csvFilePath)) {
    console.log('[AppSitesConfig] appsites.csv not found, generating...');
    generateAppSitesCsv(wwwrootPath);
  }

  // Read and parse CSV
  const csv = fs.readFileSync(csvFilePath, 'utf8').trim();

  if (!csv) {
    throw new Error('appsites.csv is empty');
  }

  const appSites = csv
    .split(',')
    .map(s => s.trim())
    .filter(s => s.length > 0);

  if (appSites.length === 0) {
    throw new Error('No AppSites found in appsites.csv');
  }

  console.log(`[AppSitesConfig] Loaded ${appSites.length} AppSites from appsites.csv`);

  // Return as Set for case-insensitive comparison
  return new Set(appSites.map(s => s.toLowerCase()));
}

/**
 * Reloads AppSites by regenerating appsites.csv from the file system
 */
export function reloadAppSites(wwwrootPath) {
  console.log('[AppSitesConfig] Reloading AppSites...');
  generateAppSitesCsv(wwwrootPath);
  return loadAppSites(wwwrootPath);
}
