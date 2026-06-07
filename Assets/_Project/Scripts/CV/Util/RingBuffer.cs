using System;

namespace PushStars.CV.Util
{
    /// <summary>
    /// Fixed-capacity ring buffer that overwrites the oldest element when full. Zero allocation
    /// after construction — used by anti-cheat sliding-window monitors (wrist drift, classification
    /// ribbons, per-rep windows) to avoid per-frame GC churn on the CV hot path.
    /// </summary>
    public sealed class RingBuffer<T>
    {
        private readonly T[] _items;
        private int _head;
        private int _count;

        public int Capacity => _items.Length;
        public int Count => _count;
        public bool IsFull => _count == _items.Length;

        public RingBuffer(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _items = new T[capacity];
        }

        public void Clear()
        {
            _head = 0;
            _count = 0;
            // Items aren't zeroed — they're overwritten by Push and Count gates access.
        }

        /// <summary>Push the newest element. Overwrites the oldest when the buffer is full.</summary>
        public void Push(T value)
        {
            _items[_head] = value;
            _head = (_head + 1) % _items.Length;
            if (_count < _items.Length) _count++;
        }

        /// <summary>Indexed access, oldest → newest (0..Count-1). Throws on out-of-range.</summary>
        public T this[int i]
        {
            get
            {
                if ((uint)i >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(i));
                int start = _count < _items.Length ? 0 : _head;
                return _items[(start + i) % _items.Length];
            }
        }
    }
}
