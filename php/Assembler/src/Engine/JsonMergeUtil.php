<?php

namespace Assembler\Engine;

use Arshu\Common\Logger;

class JsonMergeUtil
{
    public static function mergeTemplateWithJson(string $template, array $jsonObject): string
    {
        if (empty($template) || empty($jsonObject)) {
            Logger::debug('mergeTemplateWithJson: empty template or jsonObject', 'JsonMergeUtil');
            return $template;
        }

        $result = $template;
        Logger::debug('mergeTemplateWithJson: starting merge', 'JsonMergeUtil');

        // Process JSON arrays - match JSON array keys to template blocks
        foreach ($jsonObject as $key => $value) {
            if (is_array($value) && self::isAssoc($value) === false) {
                $dataList = $value;
                $blockStartTag = '{{@' . $key . '}}';
                $blockEndTag = '{{/' . $key . '}}';
                $emptyBlockStartTag = '{{^' . $key . '}}';

                $blockStartIndex = stripos($result, $blockStartTag);
                if ($blockStartIndex !== false) {
                    $blockEndIndex = stripos($result, $blockEndTag, $blockStartIndex + strlen($blockStartTag));
                    if ($blockEndIndex !== false && $blockEndIndex > $blockStartIndex) {
                        $blockContent = substr($result, $blockStartIndex + strlen($blockStartTag), $blockEndIndex - ($blockStartIndex + strlen($blockStartTag)));
                        $mergedBlock = '';

                        // Find all conditional blocks in the template block (e.g., {{@Key}}...{{/Key}})
                        $conditionalKeys = [];
                        preg_match_all('/{{@(\w+)}}/', $blockContent, $matches);
                        if (!empty($matches[1])) {
                            $conditionalKeys = array_unique($matches[1]);
                        }

                        foreach ($dataList as $item) {
                            $itemBlock = $blockContent;
                            if (is_array($item)) {
                                foreach ($item as $itemKey => $itemValue) {
                                    $placeholder = '{{$' . $itemKey . '}}';
                                    $itemBlock = self::replaceAllCaseInsensitive($itemBlock, $placeholder, (string)$itemValue);
                                }
                                foreach ($conditionalKeys as $condKey) {
                                    $condValue = false;
                                    if (isset($item[$condKey])) {
                                        $val = $item[$condKey];
                                        if (is_bool($val)) {
                                            $condValue = $val;
                                        } elseif (is_string($val)) {
                                            $condValue = strtolower($val) === 'true';
                                        } elseif (is_numeric($val)) {
                                            $condValue = $val != 0;
                                        }
                                    }
                                    $itemBlock = self::handleConditional($itemBlock, $condKey, $condValue);
                                }
                            }
                            $mergedBlock .= $itemBlock;
                        }
                        Logger::debug("Merging block for key '$key' with " . count($dataList) . " items", 'JsonMergeUtil');
                        $result = substr($result, 0, $blockStartIndex) . $mergedBlock . substr($result, $blockEndIndex + strlen($blockEndTag));
                    }
                }

                // Handle {{^ArrayName}} block if array is empty
                $emptyBlockStartIndex = stripos($result, $emptyBlockStartTag);
                $emptyBlockEndIndex = false;
                if ($emptyBlockStartIndex !== false) {
                    $offset = $emptyBlockStartIndex + strlen($emptyBlockStartTag);
                    if ($offset >= 0 && $offset <= strlen($result)) {
                        $emptyBlockEndIndex = stripos($result, $blockEndTag, $offset);
                        if ($emptyBlockEndIndex !== false && $emptyBlockEndIndex > $emptyBlockStartIndex) {
                            if (count($dataList) === 0) {
                                $emptyContent = substr($result, $offset, $emptyBlockEndIndex - $offset);
                                $result = substr($result, 0, $emptyBlockStartIndex) . $emptyContent . substr($result, $emptyBlockEndIndex + strlen($blockEndTag));
                                Logger::debug("Empty block for key '$key' replaced with content (array empty)", 'JsonMergeUtil');
                            } else {
                                $result = substr($result, 0, $emptyBlockStartIndex) . substr($result, $emptyBlockEndIndex + strlen($blockEndTag));
                                Logger::debug("Empty block for key '$key' removed (array not empty)", 'JsonMergeUtil');
                            }
                        }
                    }
                }
            }
        }

        // Replace remaining simple placeholders: {{$key}}
        foreach ($jsonObject as $key => $value) {
            if (!is_array($value)) {
                $placeholder = '{{$' . $key . '}}';
                $result = self::replaceAllCaseInsensitive($result, $placeholder, (string)$value);
                Logger::debug("Replaced simple placeholder for key '$key'", 'JsonMergeUtil');
            }
        }

        Logger::debug('mergeTemplateWithJson: merge complete', 'JsonMergeUtil');
        return $result;
    }

    private static function replaceAllCaseInsensitive(string $input, string $search, string $replacement): string
    {
        return str_ireplace($search, $replacement, $input);
    }

    private static function handleConditional(string $input, string $key, bool $condition): string
    {
        $condStart = '{{@' . $key . '}}';
        $condEndWithSpace = '{{ /' . $key . '}}';
        $condEndWithoutSpace = '{{/' . $key . '}}';

        $result = $input;

        $processTag = function ($endTag) use (&$result, $condStart, $condition, $key) {
            $index = stripos($result, $condStart);
            while ($index !== false) {
                $endIndex = stripos($result, $endTag, $index + strlen($condStart));
                if ($endIndex === false) break;

                $content = substr($result, $index + strlen($condStart), $endIndex - ($index + strlen($condStart)));
                if ($condition) {
                    $result = substr($result, 0, $index) . $content . substr($result, $endIndex + strlen($endTag));
                    $index = $index + strlen($content);
                } else {
                    $result = substr($result, 0, $index) . substr($result, $endIndex + strlen($endTag));
                }
                $index = stripos($result, $condStart, $index);
            }
        };

        $processTag($condEndWithSpace);
        $processTag($condEndWithoutSpace);

        return $result;
    }

    private static function isAssoc(array $arr)
    {
        if ([] === $arr) return false;
        return array_keys($arr) !== range(0, count($arr) - 1);
    }
}