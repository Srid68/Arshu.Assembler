<?php

namespace Assembler\App;

use Assembler\App\Json\JsonObject;
use Assembler\App\Json\JsonArray;

/**
 * Converts between PHP arrays and custom JSON types for normalization
 */
class JsonConverter
{
    public static function parseJsonString(string $jsonContent): JsonObject
    {
        try {
            if (empty($jsonContent)) {
                return new JsonObject();
            }

            $parsed = json_decode($jsonContent, true, 512, JSON_THROW_ON_ERROR);
            return self::normalizeJsonObject($parsed);
        } catch (\JsonException $e) {
            return new JsonObject();
        }
    }

    public static function normalizeJsonObject($data): JsonObject
    {
        if (!is_array($data) || empty($data)) {
            return new JsonObject();
        }

        if (array_is_list($data)) {
            return new JsonObject();
        }

        $jsonObject = new JsonObject();
        
        foreach ($data as $key => $value) {
            $convertedValue = self::convertValue($value);
            $jsonObject->setValue((string)$key, $convertedValue);
        }
        
        return $jsonObject;
    }

    public static function normalizeJsonArray(array $arr): JsonArray
    {
        $jsonArray = new JsonArray();
        
        foreach ($arr as $item) {
            $convertedValue = self::convertValue($item);
            $jsonArray->add($convertedValue);
        }
        
        return $jsonArray;
    }

    public static function convertValue($value)
    {
        if ($value === null) {
            return null;
        }

        if (is_string($value) || is_numeric($value) || is_bool($value)) {
            return $value;
        }

        if (is_array($value)) {
            if (array_is_list($value)) {
                return self::normalizeJsonArray($value);
            } else {
                return self::normalizeJsonObject($value);
            }
        }

        return $value;
    }

    public static function toPlainArray($value)
    {
        if ($value instanceof JsonObject) {
            return $value->toArray();
        }

        if ($value instanceof JsonArray) {
            return $value->toArray();
        }

        if (is_array($value)) {
            return array_map([self::class, 'toPlainArray'], $value);
        }

        return $value;
    }
}


?>