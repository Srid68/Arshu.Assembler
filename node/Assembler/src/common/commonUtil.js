// Node.js CommonUtil - Shared utility methods for template processing

import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

export class CommonUtil {
    /**
     * Get the path to the AssemblerWeb wwwroot directory and the project directory
     * @returns {Object} Object containing assemblerWebDirPath and projectDirectory
     */
    static getAssemblerWebDirPath() {
        const currentDirectory = process.cwd();
        let projectDirectory = currentDirectory;
        const currentDirInfo = path.basename(currentDirectory);

        // Check for Fly.io deployment structure first
        const flyIoWwwroot = path.join(currentDirectory, 'wwwroot');
        if (fs.existsSync(flyIoWwwroot) && fs.statSync(flyIoWwwroot).isDirectory()) {
            return { assemblerWebDirPath: flyIoWwwroot, projectDirectory: currentDirectory };
        }

        const idxBin = currentDirectory.indexOf('bin');
        if (idxBin > -1) {
            projectDirectory = currentDirectory.substring(0, idxBin);
        } else if (currentDirInfo.endsWith('AssemblerTest')) {
            projectDirectory = currentDirectory;
        } else if (currentDirInfo.endsWith('node')) {
            projectDirectory = path.join(currentDirectory, 'AssemblerTest');
        } else if (currentDirInfo.startsWith('Arshu.Assembler')) {
            projectDirectory = path.join(currentDirectory, 'node', 'AssemblerTest');
        }

        let assemblerWebDirPath = '';
        if (projectDirectory) {
            const parent = path.dirname(projectDirectory);
            const webDirPath = path.join(parent, 'AssemblerWeb', 'wwwroot');
            if (fs.existsSync(webDirPath) && fs.statSync(webDirPath).isDirectory()) {
                assemblerWebDirPath = webDirPath;
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

    /**
     * Replaces the first occurrence of 'from' in 'text' (case-insensitive) with 'to'
     * @param {string} text Text to search in
     * @param {string} from Text to search for
     * @param {string} to Replacement text
     * @returns {string} Modified text
     */
    static replaceCaseInsensitive(text, from, to) {
        if (!text || !from) return text;

        const index = text.toLowerCase().indexOf(from.toLowerCase());
        if (index >= 0) {
            return text.substring(0, index) + to + text.substring(index + from.length);
        }
        return text;
    }

    /**
     * Normalizes file content by removing UTF-8 BOM and normalizing line endings to LF (\n)
     * @param {string} content - The content to process
     * @returns {string} Content with BOM removed and line endings normalized
     */
    static normalizeFileContent(content) {
        if (!content || typeof content !== 'string') {
            return content || '';
        }

        // Remove UTF-8 BOM is '\uFEFF' (U+FEFF)
        if (content.charCodeAt(0) === 0xFEFF) {
            content = content.slice(1);
        }

        // Normalize line endings to LF (\n)
        content = content.replace(/\r\n/g, '\n').replace(/\r/g, '\n');

        return content;
    }
}

export default CommonUtil;