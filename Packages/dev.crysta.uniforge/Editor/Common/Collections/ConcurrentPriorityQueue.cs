using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace UniForge
{
    /// <summary>
    /// A thread-safe priority queue that supports multiple priority levels.
    /// Items are dequeued from higher priority levels first.
    /// Unity's supported API profile does not reliably expose .NET PriorityQueue, so we implement our own.
    /// </summary>
    /// <typeparam name="T">The type of elements in the queue</typeparam>
    public class ConcurrentPriorityQueue<T> : IDisposable
    {
        private readonly ConcurrentQueue<T>[] _queues;
        private readonly int _priorityLevels;
        private readonly SemaphoreSlim _signal;
        private int _count;

        /// <summary>
        /// Gets the total number of items across all priority levels.
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// Creates a new priority queue with the specified number of priority levels.
        /// Priority 0 is the lowest, priorityLevels-1 is the highest.
        /// </summary>
        /// <param name="priorityLevels">Number of priority levels (default: 3)</param>
        public ConcurrentPriorityQueue(int priorityLevels = 3, bool enableSignal = false)
        {
            if (priorityLevels <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(priorityLevels),
                    "Priority levels must be greater than 0");
            }

            _priorityLevels = priorityLevels;
            _signal = enableSignal ? new SemaphoreSlim(0) : null;
            _queues = new ConcurrentQueue<T>[priorityLevels];
            for (int i = 0; i < priorityLevels; i++)
            {
                _queues[i] = new ConcurrentQueue<T>();
            }
        }

        /// <summary>
        /// Adds an item to the queue with the specified priority.
        /// </summary>
        /// <param name="item">The item to add</param>
        /// <param name="priority">Priority level (0 = lowest, priorityLevels-1 = highest)</param>
        public void Enqueue(T item, int priority)
        {
            var clampedPriority = Math.Max(0, Math.Min(priority, _priorityLevels - 1));
            _queues[clampedPriority].Enqueue(item);
            Interlocked.Increment(ref _count);

            if (_signal != null)
            {
                try { _signal.Release(); }
                catch (ObjectDisposedException) { }
            }
        }

        /// <summary>
        /// Tries to remove and return the item with the highest priority.
        /// </summary>
        /// <param name="item">The removed item, or default if the queue is empty</param>
        /// <returns>True if an item was removed, false if the queue is empty</returns>
        public bool TryDequeue(out T item)
        {
            // Start from highest priority and work down
            for (int i = _priorityLevels - 1; i >= 0; i--)
            {
                if (_queues[i].TryDequeue(out item))
                {
                    Interlocked.Decrement(ref _count);
                    return true;
                }
            }

            item = default;
            return false;
        }

        /// <summary>
        /// Tries to remove and return the item with the lowest priority.
        /// Used for overflow handling - drops less important items first.
        /// </summary>
        /// <param name="dropped">The dropped item, or default if the queue is empty</param>
        /// <returns>True if an item was dropped, false if the queue is empty</returns>
        public bool TryDropLowest(out T dropped)
        {
            // Start from lowest priority and work up
            for (int i = 0; i < _priorityLevels; i++)
            {
                if (_queues[i].TryDequeue(out dropped))
                {
                    Interlocked.Decrement(ref _count);
                    // Drain the signal that was released during Enqueue
                    _signal?.Wait(0);
                    return true;
                }
            }

            dropped = default;
            return false;
        }

        /// <summary>
        /// Waits until at least one item is enqueued.
        /// Only available when enableSignal was set to true in the constructor.
        /// </summary>
        public Task WaitForEnqueueAsync(CancellationToken cancellationToken)
        {
            if (_signal == null) return Task.CompletedTask;
            return _signal.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// Checks if the queue is empty.
        /// </summary>
        public bool IsEmpty => _count == 0;

        /// <summary>
        /// Gets the count of items at a specific priority level.
        /// </summary>
        /// <param name="priority">Priority level to check</param>
        /// <returns>Number of items at the specified priority</returns>
        public int GetCountAtPriority(int priority)
        {
            if (priority < 0 || priority >= _priorityLevels)
            {
                return 0;
            }

            return _queues[priority].Count;
        }

        public void Dispose()
        {
            try { _signal?.Dispose(); }
            catch { }
        }
    }
}
