// Node.js TemplateUtils - Shared utility methods for template processing

import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

export class TemplateUtils {
    /**
     * Get the path to the AssemblerWeb wwwroot directory and the project directory
     * @returns {Object} Object containing assemblerWebDirPath and projectDirectory
     */
    static getAssemblerWebDirPath() {
        // Docker: /app/wwwroot
        const dockerWebroot = '/app/wwwroot';
        if (fs.existsSync(dockerWebroot) && fs.statSync(dockerWebroot).isDirectory()) {
            return { assemblerWebDirPath: dockerWebroot, projectDirectory: '/app' };
        }

        const currentDirectory = process.cwd();
        let projectDirectory = currentDirectory;
        const currentDirInfo = path.basename(currentDirectory);

        const idxBin = currentDirectory.indexOf('bin');
        if (idxBin > -1) {
            projectDirectory = currentDirectory.substring(0, idxBin);
        } else if (currentDirInfo.endsWith('AssemblerTest')) {
            // Already in AssemblerTest
        } else if (currentDirInfo.endsWith('node')) {
            projectDirectory = path.join(currentDirectory, 'AssemblerTest');
        } else if (currentDirInfo.startsWith('AssemblerWeb')) {
            // Already in AssemblerWeb
        } else if (currentDirInfo.startsWith('Arshu.Assembler')) {
            projectDirectory = path.join(currentDirectory, 'node', 'AssemblerTest');
        }

        let assemblerWebDirPath = '';
        if (projectDirectory) {
            const parent = path.dirname(projectDirectory);
            let webDirPath = path.join(parent, 'AssemblerWeb', 'wwwroot');
            if (fs.existsSync(webDirPath) && fs.statSync(webDirPath).isDirectory()) {
                assemblerWebDirPath = webDirPath;
            } else {
                // Fallback: try the csharp AssemblerWeb directory structure
                const workspaceRoot = path.dirname(parent);
                webDirPath = path.join(workspaceRoot, 'csharp', 'AssemblerWeb', 'wwwroot');
                if (fs.existsSync(webDirPath) && fs.statSync(webDirPath).isDirectory()) {
                    assemblerWebDirPath = webDirPath;
                } else {
                    // Second fallback: try rust AssemblerWeb directory structure
                    webDirPath = path.join(workspaceRoot, 'rust', 'AssemblerWeb', 'wwwroot');
                    if (fs.existsSync(webDirPath) && fs.statSync(webDirPath).isDirectory()) {
                        assemblerWebDirPath = webDirPath;
                    }
                }
            }
        }

        return { assemblerWebDirPath, projectDirectory };
    }

    /**
     * Check if string contains only alphanumeric characters
     * @param {string} str - The string to check
     * @returns {boolean} True if string contains only alphanumeric characters
     */
    static isAlphaNumeric(str) {
        if (!str || typeof str !== 'string') {
            return false;
        }
        return /^[a-zA-Z0-9]+$/.test(str);
    }

    /**
     * Find matching closing tag with proper nesting support
     * @param {string} content - The content to search in
     * @param {number} startPos - Starting position to search from
     * @param {string} openTag - The opening tag to match
     * @param {string} closeTag - The closing tag to find
     * @returns {number} Position of matching close tag, or -1 if not found
     */
    static findMatchingCloseTag(content, startPos, openTag, closeTag) {
        let searchPos = startPos;
        let openCount = 1;

        while (searchPos < content.length && openCount > 0) {
            const nextOpen = content.indexOf(openTag, searchPos);
            const nextClose = content.indexOf(closeTag, searchPos);

            if (nextClose === -1) return -1;

            if (nextOpen !== -1 && nextOpen < nextClose) {
                openCount++;
                searchPos = nextOpen + openTag.length;
            } else {
                openCount--;
                if (openCount === 0) {
                    return nextClose;
                }
                searchPos = nextClose + closeTag.length;
            }
        }

        return -1;
    }

    /**
     * Remove remaining slot placeholders from HTML content
     * @param {string} html - The HTML content to process
     * @returns {string} HTML with slot placeholders removed
     */
    static removeRemainingSlotPlaceholders(html) {
        let result = html;
        let searchPos = 0;

        while (searchPos < result.length) {
            const placeholderStart = result.indexOf('{{$HTMLPLACEHOLDER', searchPos);
            if (placeholderStart === -1) break;

            const afterPlaceholder = placeholderStart + 18;
            let pos = afterPlaceholder;

            // Skip digits
            while (pos < result.length && /\d/.test(result[pos])) {
                pos++;
            }

            // Check for closing }}
            if (pos + 1 < result.length && result.substring(pos, pos + 2) === '}}') {
                const placeholderEnd = pos + 2;
                const placeholder = result.substring(placeholderStart, placeholderEnd);
                result = result.replace(placeholder, '');
                // Don't advance searchPos since we removed content
            } else {
                searchPos = placeholderStart + 1;
            }
        }

        return result;
    }
}