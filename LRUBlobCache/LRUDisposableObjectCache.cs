namespace BMCollections
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;

    /// <summary>
    /// An LRU cache for disposable objects.
    /// It supports scavenging based on size and time of creation.
    /// </summary>
    /// <typeparam name="TKey">The type of the key.</typeparam>
    /// <typeparam name="TContent">The type of the content.</typeparam>
    public class LRUDisposableObjectCache<TKey, TContent> : IDisposable, IDictionary<TKey, TContent> 
        where TKey : IEquatable<TKey>
        where TContent : ISizeable, IDisposable
    {
        private static readonly TimeSpan defaultScavengeBound = new TimeSpan(0, 0, 0, 0, 300);
        private static readonly TimeSpan defaultScavengeFrequency = new TimeSpan(0, 1, 0);
        private static readonly TimeSpan defaultElementLifetime = new TimeSpan(1, 0, 0);

        private static readonly TimeSpan defaultInitialScavengeDelay = new TimeSpan(0, 0, 0, 20);

        private readonly TimeSpan initialScavengeDelay;

        private readonly System.Threading.Timer scavengeTimer;

        private readonly long totalCapacity;
        private readonly TimeSpan scavengeFrequency;
        private readonly double scavengeSizePercentageThreshold;

        private readonly TimeSpan elementLifetime;
        private bool itemsHaveExpirationDates;

        private readonly ReaderWriterLockSlim cacheLock = new ReaderWriterLockSlim();

        private readonly Dictionary<TKey, LinkedListNode<LargeObjectElement<TKey,TContent>>> cache;
        private readonly LinkedList<LargeObjectElement<TKey,TContent>> lruQueue;

        private readonly TimeSpan scavengeTimeBound;

        private bool isDisposed;

        private bool scavengingHappening;

        /// <summary>
        /// Gets the total size of the LRU cache.
        /// </summary>
        public long CurrentSize
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the count of elements in the LRU cache.
        /// </summary>
        public int Count => this.cache.Count;

        /// <summary>
        /// Gets the total capacity of the LRU cache in terms of size.
        /// </summary>
        public long Capacity => this.totalCapacity;

        /// <summary>
        /// Gets the keys in the LRU cache. The order is arbitrary.
        /// </summary>
        public ICollection<TKey> Keys
        {
            get
            {
                this.cacheLock.EnterReadLock();
                try
                {
                    return this.cache.Keys;
                }
                finally
                {
                    this.cacheLock.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// Gets the values in the LRU cache. The order is arbitrary.
        /// </summary>
        public ICollection<TContent> Values
        {
            get
            {
                this.cacheLock.EnterReadLock();
                try
                {
                    return this.cache.Values.Select(v => v.Value.Content).ToArray();
                }
                finally
                {
                    this.cacheLock.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// Gets false, as the LRU cache is not readonly.
        /// </summary>
        public bool IsReadOnly => false;

        /// <summary>
        /// Gets the content with the given key or sets the content.
        /// The old content will be disposed.
        /// </summary>
        /// <param name="key">They key.</param>
        public TContent this[TKey key]
        {
            get
            {
                this.cacheLock.EnterReadLock();
                try
                {
                    return this.cache[key].Value.Content;
                }
                finally { this.cacheLock.ExitReadLock();}
            }

            set
            {
                this.cacheLock.EnterWriteLock();
                try
                {
                    var oldValue = this.cache[key].Value;
                    this.cache[key].Value = new LargeObjectElement<TKey, TContent>(key, value);

                    oldValue.Dispose();
                }
                finally { this.cacheLock.ExitWriteLock();}
            }
        }

        /// <summary>
        /// Creates an instance of <see cref="LRUDisposableObjectCache{TKey, TContent}"/>
        /// </summary>
        /// <param name="totalCapacity">The total capacity of the LRU cache.</param>
        /// <param name="scavengePeriod">The period of the scavenger thread.</param>
        /// <param name="cleanupThreshold">The percentage of space </param>
        /// <param name="scavengeTimeBound">The maximum amount of time a thread can spend scavenging.</param>
        /// <param name="initialScavengeDelay">The initial delay before the first scavenge.</param>
        /// <param name="expectedElementCount">The expected amount of elements in the LRU cache.</param>
        /// <param name="elementLifetime">The lifetime of an element.</param>
        /// <param name="itemsHaveExpirationDates">If lifetimes should be taken into account.</param>
        public LRUDisposableObjectCache(long totalCapacity, TimeSpan scavengePeriod, double cleanupThreshold, TimeSpan? scavengeTimeBound = null, TimeSpan? initialScavengeDelay = null, int expectedElementCount = 100, TimeSpan? elementLifetime = null, bool itemsHaveExpirationDates = true)
        {
            this.totalCapacity = totalCapacity;
            this.scavengeFrequency = scavengePeriod;
            this.scavengeSizePercentageThreshold = cleanupThreshold;
            this.CurrentSize = 0;
            this.cache = new Dictionary<TKey, LinkedListNode<LargeObjectElement<TKey,TContent>>>(expectedElementCount);
            this.lruQueue = new LinkedList<LargeObjectElement<TKey,TContent>>();
            this.initialScavengeDelay = initialScavengeDelay ?? defaultInitialScavengeDelay;
            this.scavengeTimer = new System.Threading.Timer(this.OnTimedEvent, null, BoundedMilliseconds((int)this.initialScavengeDelay.TotalMilliseconds), BoundedMilliseconds((int)Math.Floor(this.scavengeFrequency.TotalMilliseconds > 0 ? this.scavengeFrequency.TotalMilliseconds : defaultScavengeFrequency.TotalMilliseconds)));

            this.scavengeTimeBound = scavengeTimeBound ?? defaultScavengeBound;

            this.elementLifetime = elementLifetime ?? defaultElementLifetime;

            this.itemsHaveExpirationDates = itemsHaveExpirationDates;
        }

        /// <summary>
        /// Returns true if the cache contains the item with the given key, else false.
        /// </summary>
        /// <param name="key">They key of the item.</param>
        /// <param name="content">The value assigned to the key.</param>
        /// <returns></returns>
        public bool TryGetValue(TKey key, out TContent content)
        {
            this.ThrowIfDisposed();

            this.cacheLock.EnterReadLock();
            try
            {
                if (!cache.TryGetValue(key, out LinkedListNode<LargeObjectElement<TKey,TContent>> elementNode))
                {
                    content = default;
                    return false;
                }

                this.PromoteNodeToMostRecentlyUsed(elementNode);

                content = elementNode.Value.Content;
                return true;
            }
            finally
            {
                this.cacheLock.ExitReadLock();
            }    
        }

        /// <summary>
        /// Adds a new item to the cache.
        /// </summary>
        /// <param name="key">The key</param>
        /// <param name="content">The value</param>
        /// <exception cref="ArgumentException">Thrown if an item with the same key already exists.</exception>
        public void Add(TKey key, TContent content)
        {
            this.ThrowIfDisposed();

            this.cacheLock.EnterUpgradeableReadLock();
            bool enteredWriteLock = false;

            try
            {
                if (this.cache.ContainsKey(key))
                {
                    throw new ArgumentException("Item with the same key already exists in LRU cache.");
                }

                var element = new LargeObjectElement<TKey,TContent>(key, content);
                if (this.ScavengeRequired(element.Size, isElementInsertion: true))
                {
                    this.cacheLock.EnterWriteLock();
                    enteredWriteLock = true;
                    Scavenge(element.Size, false, true);
                }

                if (!enteredWriteLock)
                {
                    this.cacheLock.EnterWriteLock();
                    enteredWriteLock = true;
                }

                var node = new LinkedListNode<LargeObjectElement<TKey,TContent>>(element);

                this.lruQueue.AddFirst(node);
                this.cache.Add(key, node);
                this.CurrentSize += element.Size;
            }
            finally
            {
                if (this.cacheLock.IsWriteLockHeld)
                {
                    this.cacheLock.ExitWriteLock();
                }

                this.cacheLock.ExitUpgradeableReadLock();
            }
            
        }

        /// <summary>
        /// Removes an item from the cache. Returns true if the cache contains the item with the given key and it was removed, else false.
        /// </summary>
        /// <param name="key">The key of the item.</param>
        public bool Remove(TKey key)
        {
            this.ThrowIfDisposed();

            this.cacheLock.EnterWriteLock();
            try
            {
                if (!this.cache.TryGetValue(key, out LinkedListNode<LargeObjectElement<TKey,TContent>> node))
                {
                    return false;
                }

                this.RemoveNode(node);

                return true;
            }
            finally
            {
                this.cacheLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Returns true if the cache contains the item with the given key.
        /// </summary>
        /// <param name="key">The key</param>
        public bool ContainsKey(TKey key)
        {
            return this.TryGetValue(key, out _);
        }

        /// <summary>
        /// Adds a new item to the cache.
        /// </summary>
        /// <param name="item">The item.</param>
        public void Add(KeyValuePair<TKey, TContent> item)
        {
            this.Add(item.Key, item.Value);
        }

        /// <summary>
        /// Clears the cache and disposes all of its items.
        /// </summary>
        public void Clear()
        {
            this.cacheLock.EnterWriteLock();

            try
            {
                this.ClearCache();
            }
            finally
            {
                this.cacheLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Returns true if the cache contains the item with the given key.
        /// </summary>
        /// <param name="item"></param>
        public bool Contains(KeyValuePair<TKey, TContent> item)
        {
            return this.TryGetValue(item.Key, out TContent containedItem) && containedItem.Equals(item.Value);
        }

        /// <summary>
        /// Copies the key value pairs to an array.
        /// </summary>
        /// <param name="array">The array</param>
        /// <param name="arrayIndex">The start index</param>
        public void CopyTo(KeyValuePair<TKey, TContent>[] array, int arrayIndex)
        {
            foreach (var kvp in this.KeyValueTuples())
            {
                array[arrayIndex++] = new KeyValuePair<TKey, TContent>(kvp.Key, kvp.Content);
            }
        }

        /// <summary>
        /// Removes an item with the given key.
        /// </summary>
        /// <param name="item"></param>
        public bool Remove(KeyValuePair<TKey, TContent> item)
        {
            return this.Remove(item.Key);
        }

        /// <summary>
        /// Gets the enumerator.
        /// </summary>
        /// <returns></returns>
        public IEnumerator<KeyValuePair<TKey, TContent>> GetEnumerator()
        {
            return this.KeyValuePairs().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        /// <summary>
        /// Returns the key-value tuples of the stored data.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<(TKey Key, TContent Content)> KeyValueTuples()
        {
            foreach (LargeObjectElement<TKey, TContent> lo in this.lruQueue)
            {
                yield return (lo.Key, lo.Content);
            }
        }

        /// <summary>
        /// Disposes the cache, disposing all of the elements.
        /// </summary>
        public void Dispose()
        {
            if (this.isDisposed)
            {
                return;
            }

            this.cacheLock.EnterWriteLock();
            try
            {
                if (this.isDisposed)
                {
                    return;
                }

                this.scavengeTimer.Dispose();
                this.ClearCache();
                this.isDisposed = true;
            }
            finally
            {
                this.cacheLock.ExitWriteLock();
            }
        }

        private void Scavenge(long requiredSize, bool timerCleanup = false, bool lockAlreadyTaken = false)
        {
            // don't allow scavenging to happen on more than one thread
            if (this.scavengingHappening)
            {
                return;
            }

            if (!lockAlreadyTaken)
            {
                this.cacheLock.EnterUpgradeableReadLock();
            }
            try
            {
                if (this.scavengingHappening)
                {
                    return;
                }

                DateTime scavengeStart = DateTime.Now;

                if (!timerCleanup && !this.ScavengeRequired(requiredSize, !timerCleanup))
                {
                    return;
                }

                if (!this.cacheLock.IsWriteLockHeld)
                {
                    this.cacheLock.EnterWriteLock();
                }

                if (this.scavengingHappening)
                {
                    return;
                }

                var cancellationTokenSource = new CancellationTokenSource(this.scavengeTimeBound);
                var token = cancellationTokenSource.Token;

                var currentElement = this.lruQueue.Last;
                if (currentElement == null)
                {
                    return;
                }

                bool scavengeRequired = false;
                while (currentElement != null && (timerCleanup || (scavengeRequired = this.ScavengeRequired(requiredSize, !timerCleanup))))
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    var elementToRemove = currentElement;
                    currentElement = elementToRemove.Previous;

                    if ((!scavengeRequired && !timerCleanup) || (timerCleanup && !this.ElementExpired(elementToRemove.Value, scavengeStart)))
                    {
                        continue;
                    }

                    long elementSize = elementToRemove.Value.Size;
                    requiredSize -= elementSize;

                    this.RemoveNode(elementToRemove);
                }
            }
            finally
            {
                if (!lockAlreadyTaken && this.cacheLock.IsUpgradeableReadLockHeld)
                {
                    if (this.cacheLock.IsWriteLockHeld)
                    {
                        this.cacheLock.ExitWriteLock();
                    }

                    this.cacheLock.ExitUpgradeableReadLock();
                }
            }
        }

        private static int BoundedMilliseconds(int time)
        {
            if (time < 0)
            {
                return int.MaxValue;
            }

            return time;
        }

        private bool ScavengeRequired(long newElementSize, bool isElementInsertion)
        {
            long newSize = newElementSize + this.CurrentSize;
            return newSize > this.totalCapacity || (!isElementInsertion && 
                // periodic clean up will clean more thoroughly
                newSize > this.scavengeSizePercentageThreshold * this.totalCapacity);
        }

        private void RemoveNode(LinkedListNode<LargeObjectElement<TKey,TContent>> node)
        {
            this.lruQueue.Remove(node);
            this.cache.Remove(node.Value.Key);
            this.CurrentSize -= node.Value.Size;
            node.Value.Dispose();
        }

        private void PromoteNodeToMostRecentlyUsed(LinkedListNode<LargeObjectElement<TKey,TContent>> node)
        {
            this.lruQueue.Remove(node);
            this.lruQueue.AddFirst(node);
        }

        private void OnTimedEvent(object stateInfo)
        {
            this.Scavenge(0, timerCleanup: true);
        }

        private bool ElementExpired(LargeObjectElement<TKey,TContent> element, DateTime scavengeStart)
        {
            return this.itemsHaveExpirationDates && (element.TimeOfCreation + this.elementLifetime < scavengeStart);
        }

        private void ClearCache()
        {
            this.cache.Clear();
            while (this.lruQueue.First != null)
            {
                this.lruQueue.RemoveFirst();
            }

            this.CurrentSize = 0;
        }

        private void ThrowIfDisposed()
        {
            if (this.isDisposed)
            {
                throw new ObjectDisposedException("A disposed cache cannot be used.");
            }
        }

        private IEnumerable<KeyValuePair<TKey,TContent>> KeyValuePairs()
        {
            foreach (var lo in this.lruQueue)
            {
                yield return new KeyValuePair<TKey, TContent>(lo.Key, lo.Content);
            }
        }
    }
}
