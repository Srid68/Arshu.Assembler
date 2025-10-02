<?php

namespace Assembler\App\Json;

/**
 * Represents a JSON array with additional functionality
 */
class JsonArray implements \ArrayAccess, \Iterator, \Countable
{
    private array $items = [];
    private int $position = 0;

    public function __construct(int $capacity = null)
    {
        if ($capacity !== null) {
            $this->items = array_fill(0, $capacity, null);
            $this->items = []; // Reset to empty but pre-allocated conceptually
        }
    }

    /**
     * Add item to array
     * @param mixed $item Item to add
     */
    public function add($item): void
    {
        $this->items[] = $item;
    }

    /**
     * Get item at index
     * @param int $index The index
     * @return mixed The item or null
     */
    public function get(int $index)
    {
        return $this->items[$index] ?? null;
    }

    /**
     * Set item at index
     * @param int $index The index
     * @param mixed $item The item to set
     */
    public function set(int $index, $item): void
    {
        $this->items[$index] = $item;
    }

    /**
     * Get the size of the array
     * @return int Array length
     */
    public function size(): int
    {
        return count($this->items);
    }

    /**
     * Check if array is empty
     * @return bool True if empty
     */
    public function isEmpty(): bool
    {
        return empty($this->items);
    }

    /**
     * Convert to plain array
     * @return array Plain PHP array
     */
    public function toArray(): array
    {
        return array_map(function($item) {
            if ($item instanceof JsonObject) {
                return $item->toArray();
            } elseif ($item instanceof JsonArray) {
                return $item->toArray();
            }
            return $item;
        }, $this->items);
    }

    // ArrayAccess implementation
    public function offsetExists($offset): bool
    {
        return isset($this->items[$offset]);
    }

    public function offsetGet($offset): mixed
    {
        return $this->items[$offset] ?? null;
    }

    public function offsetSet($offset, $value): void
    {
        if ($offset === null) {
            $this->items[] = $value;
        } else {
            $this->items[$offset] = $value;
        }
    }

    public function offsetUnset($offset): void
    {
        unset($this->items[$offset]);
    }

    // Iterator implementation
    public function rewind(): void
    {
        $this->position = 0;
    }

    public function current(): mixed
    {
        return $this->items[$this->position] ?? null;
    }

    public function key(): mixed
    {
        return $this->position;
    }

    public function next(): void
    {
        ++$this->position;
    }

    public function valid(): bool
    {
        return isset($this->items[$this->position]);
    }

    // Countable implementation
    public function count(): int
    {
        return count($this->items);
    }
}

/**
 * Generic JsonArray class for typed arrays
 */
class GenericJsonArray extends JsonArray
{
    public function __construct(int $capacity = null)
    {
        parent::__construct($capacity);
    }
}
?>