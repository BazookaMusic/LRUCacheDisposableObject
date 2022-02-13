namespace LRUBlobCacheTest
{
    using BMCollections;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    [TestClass]
    public class LRUDisposableObjectTest
    {
        [TestMethod]
        public void CreateLRUBlobCache_DoesNotThrow()
        {
            var lruCache = new LRUDisposableObjectCache<int, StreamContainer>(1000, TimeSpan.FromSeconds(1), 0.8);
        }

        [TestMethod]
        public void AddOneElement_GetBackTheSame()
        {
            var lruCache = new LRUDisposableObjectCache<int, StreamContainer>(1000, TimeSpan.FromSeconds(1), 0.8);
            var streamContainer = new StreamContainer(new MemoryStream(new byte[100]));

            lruCache.Add(1, streamContainer);

            Assert.AreEqual(true, lruCache.TryGetValue(1, out StreamContainer someStream), "element must exist");

            Assert.AreEqual(true, object.ReferenceEquals(someStream, streamContainer), "Same streamContainer must be retrieved.");
        }

        [TestMethod]
        public void AddOneMoreElementThanCapacity_RemovesLRUElement()
        {
            var lruCache = new LRUDisposableObjectCache<int, StreamContainer>(5, TimeSpan.FromSeconds(100), 0.8, initialScavengeDelay: TimeSpan.FromSeconds(2000));

            for (int i = 0; i < 6; i++)
            {
                var streamContainer = new StreamContainer(new MemoryStream(new byte[1]));

                lruCache.Add(i, streamContainer);
            }

            Assert.AreEqual(5, lruCache.Count);
            Assert.AreEqual(false, lruCache.TryGetValue(0, out _));
            
            for (int i = 1; i < 6; i++)
            {
                Assert.AreEqual(true, lruCache.TryGetValue(i, out _));
            }
        }

        [TestMethod]
        public void AccessToElement_MakesItMRU()
        {
            int capacity = 1000;
            var lruCache = new LRUDisposableObjectCache<int, StreamContainer>(capacity, TimeSpan.FromSeconds(100), 0.8, initialScavengeDelay: TimeSpan.FromSeconds(2000));

            for (int i = 0; i < capacity; i++)
            {
                var streamContainer = new StreamContainer(new MemoryStream(new byte[1]));

                lruCache.Add(i, streamContainer);
            }

            var seedRand = new Random();
            int seed = seedRand.Next();
            var rand = new Random(seed);

            List<int> path = new List<int>();
            HashSet<int> visited = new HashSet<int>();

            // selection without replacement
            for (int i = 0; i < capacity / 2; i++)
            {
                int choice;
                do
                {
                    choice = rand.Next(capacity);
                }
                while (visited.Contains(choice));

                lruCache.TryGetValue(choice, out _);
                visited.Add(choice);

                Assert.AreEqual(choice, lruCache.KeyValueTuples().First().Key);
                path.Add(choice);
            }

            // these are all the elements in the LRU Cache
            // they should be ordered with the randomly selected elements first
            var kvps = lruCache.KeyValueTuples().ToList();

            // The path will be reversed, first selected elements
            // will be at the start of the list. We want them to be first.
            path.Reverse();

            for (int i = 0; i < path.Count; i++)
            {
                Assert.AreEqual(path[i], kvps[i].Key, $"The random path was different than the order in the LRU. This means that the LRU is not working properly. The seed was {seed}.");
            }
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void AddExisting_Throws()
        {
            var lruCache = new LRUDisposableObjectCache<int, StreamContainer>(1000, TimeSpan.FromSeconds(1), 0.8);
            var streamContainer = new StreamContainer(new MemoryStream(new byte[100]));

            lruCache.Add(1, streamContainer);
            lruCache.Add(1, streamContainer);
        }

        [TestMethod]
        public void RemoveNonExistent_ReturnsFalse()
        {
            var lruCache = new LRUDisposableObjectCache<int, StreamContainer>(1000, TimeSpan.FromSeconds(1), 0.8);
            var streamContainer = new StreamContainer(new MemoryStream(new byte[100]));

            lruCache.Add(1, streamContainer);
            Assert.IsFalse(lruCache.Remove(2));
        }

        [TestMethod]
        public void AddOneElement_RemoveIt_NotExists()
        {
            var lruCache = new LRUDisposableObjectCache<int, StreamContainer>(1000, TimeSpan.FromSeconds(1), 0.8);
            var streamContainer = new StreamContainer(new MemoryStream(new byte[100]));

            lruCache.Add(1, streamContainer);
            lruCache.Remove(1);

            Assert.AreEqual(false, lruCache.TryGetValue(1, out StreamContainer someStream), "element must exist");

            Assert.AreEqual(0, lruCache.CurrentSize);
        }

        [TestMethod]
        [ExpectedException(typeof(ObjectDisposedException))]
        public void RemoveElement_DisposesStream()
        {
            var lruCache = new LRUDisposableObjectCache<int, StreamContainer>(1000, TimeSpan.FromSeconds(1), 0.8);
            var streamContainer = new StreamContainer(new MemoryStream(new byte[100]));
            streamContainer.Stream.WriteByte(1);

            lruCache.Add(1, streamContainer);
            lruCache.Remove(1);

            streamContainer.Stream.Seek(0, SeekOrigin.Begin);
        }

        [TestMethod]
        public void ExceedCapacity_SucceedsInAdding()
        {
            var lruCache = new LRUDisposableObjectCache<int, StreamContainer>(1000, TimeSpan.FromHours(100), 1.0, scavengeTimeBound: null, expectedElementCount: 10, elementLifetime: TimeSpan.FromDays(1));
            int blobSize = 100;
            for (int i = 0; i < 1000; i++)
            {
                var streamContainer = new StreamContainer(new MemoryStream(new byte[100]));

                lruCache.Add(i, streamContainer);
            }

            Assert.AreEqual(lruCache.Capacity, lruCache.CurrentSize);
            Assert.AreEqual(lruCache.Capacity / blobSize, lruCache.Count);
        }

        [TestMethod]
        public void TimerScavenge_RemovesExpired()
        {
            var lruCache = new LRUDisposableObjectCache<int, StreamContainer>(1000, new System.TimeSpan(0, 0, 0,0, 1000), 0.8, 
                scavengeTimeBound: null, initialScavengeDelay: TimeSpan.FromMilliseconds(100), expectedElementCount: 100, elementLifetime: TimeSpan.FromMilliseconds(1));

            for (int i = 0; i < 1000; i++)
            {
                var streamContainer = new StreamContainer(new MemoryStream(new byte[1]));
                lruCache.Add(i, streamContainer);
            }

            Thread.Sleep(500);

            Assert.AreEqual(0, lruCache.Count);
        }

        [TestMethod]
        public void TimerScavenge_RemovesExpired_KeepsNonExpired()
        {
            // Setup
            // Lifetime is much larger than scavenge frequency
            // Elements are added first, then a delay occurs, then the rest are added
            // Finally we wait until the first elements are scavenged
            // Only the elements added after should remain
            int scavengeFrequencyInMs = 50;
            int lifetimeInMs = 500;

            int toExpireCount = 10;
            int toNotExpireCount = 20;

            int delayInMsBetweenAdds = 200;
            int delayToExpiration = lifetimeInMs - delayInMsBetweenAdds + 100;

            var lruCache = new LRUDisposableObjectCache<int, StreamContainer>(1000, TimeSpan.FromMilliseconds(scavengeFrequencyInMs), 0.8,
                scavengeTimeBound: null, initialScavengeDelay: TimeSpan.FromMilliseconds(0), expectedElementCount: 100, elementLifetime: TimeSpan.FromMilliseconds(500));

            for (int i = 0; i < toExpireCount; i++)
            {
                var streamContainer = new StreamContainer(new MemoryStream(new byte[1]));
                lruCache.Add(i, streamContainer);
            }

            Thread.Sleep(delayInMsBetweenAdds);

            for (int i = toExpireCount; i < toExpireCount + toNotExpireCount; i++)
            {
                var streamContainer = new StreamContainer(new MemoryStream(new byte[1]));
                lruCache.Add(i, streamContainer);
            }

            Thread.Sleep(delayToExpiration);

            Assert.AreEqual(toNotExpireCount, lruCache.Count);
        }

        [TestMethod]
        public void MultipleThreadsAdd_AllElementsExists()
        {
            var lruCache = new LRUDisposableObjectCache<int, StreamContainer>(1000, new System.TimeSpan(0, 0, 0, 0, 50), 0.8);

            Parallel.For(0, 1000, (i) =>
            {
                var streamContainer = new StreamContainer(new MemoryStream(new byte[1]));

                lruCache.Add(i, streamContainer);
            });

            for (int i = 0; i < 1000; i++)
            {
                Assert.AreEqual(true, lruCache.TryGetValue(i, out _));
            }
        }

        [TestMethod]
        public void MultipleThreadsRemove_AllNonDeletedElementsExists()
        {
            var lruCache = new LRUDisposableObjectCache<int, StreamContainer>(1000, new System.TimeSpan(0, 0, 0, 0, 1000), 0.8);

            for (int i = 0; i < 1000; i++)
            {
                var streamContainer = new StreamContainer(new MemoryStream(new byte[1]));

                lruCache.Add(i, streamContainer);
            }

            Parallel.For(0, 1000, (i) =>
            {
                if (i % 2 == 0)
                {
                    lruCache.Remove(i);
                }
            });

            for (int i = 0; i < 1000; i++)
            {
                bool shouldExist = i % 2 != 0;
                Assert.AreEqual(shouldExist, lruCache.TryGetValue(i, out _));
            }
        }

        [TestMethod]
        public void Dispose_ClearsCache()
        {
            var lruCache = new LRUDisposableObjectCache<int, StreamContainer>(1000, new System.TimeSpan(0, 0, 0, 0, 1000), 0.8);
            using (lruCache)
            {
                for (int i = 0; i < 1000; i++)
                {
                    var streamContainer = new StreamContainer(new MemoryStream(new byte[1]));

                    lruCache.Add(i, streamContainer);
                }
            }

            Assert.AreEqual(0, lruCache.CurrentSize);
            Assert.AreEqual(0, lruCache.Count);
        }

        [TestMethod]
        public void DoubleDispose_DoesNotThrow()
        {
            var lruCache = new LRUDisposableObjectCache<int, StreamContainer>(1000, new System.TimeSpan(0, 0, 0, 0, 1000), 0.8);
            using (lruCache)
            {
                for (int i = 0; i < 1000; i++)
                {
                    var streamContainer = new StreamContainer(new MemoryStream(new byte[1]));

                    lruCache.Add(i, streamContainer);
                }
            }

            lruCache.Dispose();
        }

        [TestMethod]
        public void DisposedCacheThrowsOnAllOperations()
        {
            var lruCache = new LRUDisposableObjectCache<int, StreamContainer>(1000, new System.TimeSpan(0, 0, 0, 0, 1000), 0.8);
            lruCache.Dispose();

            Assert.ThrowsException<ObjectDisposedException>(() => lruCache.Add(1, new StreamContainer(new MemoryStream())));
            Assert.ThrowsException<ObjectDisposedException>(() => lruCache.TryGetValue(1, out _));
        }
    }
}