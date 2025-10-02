<?php

namespace Assembler\App\Json;

/**
 * Represents a JSON object with additional functionality
 */
class JsonObject implements \ArrayAccess, \Iterator, \Countable
{
    private array $members = [];
    private array $keys = [];
    private int $position = 0;

    public function __construct(array $data = [])
    {
        foreach ($data as $key => $value) {
            $this->setValue($key, $value);
        }
    }

    /**
     * Compare this JsonObject with another
     * @param JsonObject $toCompare The object to compare with
     * @return bool True if objects are equal
     */
    public function compare(JsonObject $toCompare): bool
    {
        if ($this->count() !== $toCompare->count()) {
            return false;
        }

        foreach ($this->members as $key => $value) {
            if (!$toCompare->hasKey($key)) {
                return false;
            }

            $otherValue = $toCompare->getValue($key);
            if ($value !== $otherValue) {
                if (is_object($value) && is_object($otherValue)) {
                    if (json_encode($value) !== json_encode($otherValue)) {
                        return false;
                    }
                } else {
                    return false;
                }
            }
        }

        return true;
    }

    /**
     * Get value by key (case-insensitive)
     * @param string $key The key to look for
     * @return mixed The value or null
     */
    public function getValue(string $key)
    {
        // First try exact match
        if (isset($this->members[$key])) {
            return $this->members[$key];
        }

        // Try case-insensitive search
        $lowerKey = strtolower($key);
        foreach ($this->members as $k => $v) {
            if (strtolower($k) === $lowerKey) {
                return $v;
            }
        }

        return null;
    }

    /**
     * Set value by key
     * @param string $key The key
     * @param mixed $value The value
     */
    public function setValue(string $key, $value): void
    {
        if (!isset($this->members[$key])) {
            $this->keys[] = $key;
        }
        $this->members[$key] = $value;
    }

    /**
     * Check if key exists (case-insensitive)
     * @param string $key The key to check
     * @return bool True if key exists
     */
    public function hasKey(string $key): bool
    {
        // First try exact match
        if (isset($this->members[$key])) {
            return true;
        }

        // Try case-insensitive search
        $lowerKey = strtolower($key);
        foreach (array_keys($this->members) as $k) {
            if (strtolower($k) === $lowerKey) {
                return true;
            }
        }

        return false;
    }

    /**
     * Convert to plain array
     * @return array Plain PHP array
     */
    public function toArray(): array
    {
        $result = [];
        foreach ($this->members as $key => $value) {
            if ($value instanceof JsonObject) {
                $result[$key] = $value->toArray();
            } elseif ($value instanceof JsonArray) {
                $result[$key] = $value->toArray();
            } else {
                $result[$key] = $value;
            }
        }
        return $result;
    }

    // ArrayAccess implementation
    public function offsetExists($offset): bool
    {
        return $this->hasKey($offset);
    }

    public function offsetGet($offset): mixed
    {
        return $this->getValue($offset);
    }

    public function offsetSet($offset, $value): void
    {
        $this->setValue($offset, $value);
    }

    public function offsetUnset($offset): void
    {
        if (isset($this->members[$offset])) {
            unset($this->members[$offset]);
            $this->keys = array_values(array_diff($this->keys, [$offset]));
        }
    }

    // Iterator implementation
    public function rewind(): void
    {
        $this->position = 0;
    }

    public function current(): mixed
    {
        return $this->members[$this->keys[$this->position]] ?? null;
    }

    public function key(): mixed
    {
        return $this->keys[$this->position] ?? null;
    }

    public function next(): void
    {
        ++$this->position;
    }

    public function valid(): bool
    {
        return isset($this->keys[$this->position]);
    }

    // Countable implementation
    public function count(): int
    {
        return count($this->members);
    }
}
?>