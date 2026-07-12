using System;
using System.Collections.Generic;

namespace UniForge
{
    /// <summary>
    /// Fixed-size circular buffer for efficient log storage.
    /// O(1) Add, O(1) indexed access, automatic oldest-first eviction.
    /// </summary>
    public class CircularBuffer<T>
    {
        private readonly T[] _buffer;
        private int _head;
        private int _count;

        public int Count => _count;
        public int Capacity => _buffer.Length;

        public CircularBuffer(int capacity)
        {
            if (capacity <= 0) throw new ArgumentException("Capacity must be positive", nameof(capacity));
            _buffer = new T[capacity];
            _head = 0;
            _count = 0;
        }

        public void Add(T item)
        {
            _buffer[_head] = item;
            _head = (_head + 1) % _buffer.Length;
            if (_count < _buffer.Length) _count++;
        }

        public T this[int index]
        {
            get
            {
                if (index < 0 || index >= _count)
                    throw new IndexOutOfRangeException();

                int start = (_head - _count + _buffer.Length) % _buffer.Length;
                return _buffer[(start + index) % _buffer.Length];
            }
        }

        public void Clear()
        {
            _head = 0;
            _count = 0;
            Array.Clear(_buffer, 0, _buffer.Length);
        }

        public List<T> ToList()
        {
            var list = new List<T>(_count);
            for (int i = 0; i < _count; i++)
            {
                list.Add(this[i]);
            }
            return list;
        }
    }
}
