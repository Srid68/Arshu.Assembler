// Node.js CommonUtil - Shared utility methods for template processing

export function isAlphaNumeric(str) {
    if (!str || typeof str !== 'string') {
        return false;
    }
    return /^[a-zA-Z0-9]+$/.test(str);
}

export function findMatchingCloseTag(content, startPos, openTag, closeTag) {
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

export function removeRemainingSlotPlaceholders(html) {
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

export function replaceCaseInsensitive(text, from, to) {
    if (!text || !from) return text;

    const index = text.toLowerCase().indexOf(from.toLowerCase());
    if (index >= 0) {
        return text.substring(0, index) + to + text.substring(index + from.length);
    }
    return text;
}

export function normalizeFileContent(content) {
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