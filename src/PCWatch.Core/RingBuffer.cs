using System;
using System.Collections.Generic;

namespace PCWatch.Core
{
    public sealed class RingBuffer<T>
    {
        private readonly T[] _items;
        private int _start;
        private int _count;

        public RingBuffer(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException("capacity");
            _items = new T[capacity];
        }

        public int Capacity { get { return _items.Length; } }
        public int Count { get { lock (_items) return _count; } }

        public void Add(T value)
        {
            lock (_items)
            {
                int index = (_start + _count) % _items.Length;
                _items[index] = value;
                if (_count < _items.Length)
                {
                    _count++;
                }
                else
                {
                    _start = (_start + 1) % _items.Length;
                }
            }
        }

        public void Clear()
        {
            lock (_items)
            {
                Array.Clear(_items, 0, _items.Length);
                _start = 0;
                _count = 0;
            }
        }

        public T[] Snapshot()
        {
            lock (_items)
            {
                T[] result = new T[_count];
                for (int i = 0; i < _count; i++)
                    result[i] = _items[(_start + i) % _items.Length];
                return result;
            }
        }
    }
}
