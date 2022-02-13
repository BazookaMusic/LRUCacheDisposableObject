using System;
using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using BMCollections;

namespace BMCollectionsBenchmarks
{
    public class LRUCacheBenchmark
    {
        public LRUDisposableObjectCache<int, StreamContainer> lruCache;

        [Params(1, 100, 1000, 10_000, 100_000, 1_000_000)]
        public int N { get; set; }

        [Benchmark]
        public int Add()
        {
            var container = new StreamContainer(new MemoryStream(0));
            for (int i = N; i < 2*N; i++)
            {
                this.lruCache.Add(i, container);
            }

            return this.lruCache.Count;
        }

        [Benchmark]
        public int AddDoubleThanCapacity()
        {
            var container = new StreamContainer(new MemoryStream(0));
            for (int i = N; i < 3*N; i++)
            {
                this.lruCache.Add(i, container);
            }

            return this.lruCache.Count;
        }

        [Benchmark]
        public int Remove()
        {
            for (int i = 0; i < N; i++)
            {
                this.lruCache.Remove(i);
            }

            return this.lruCache.Count;
        }

        [Benchmark]
        public long TryGet()
        {
            long size = 0;
            for (int i = 0; i < N; i++)
            {
                this.lruCache.TryGetValue(i, out var streamContainer);
                size += streamContainer.Size;
            }

            return size;
        }

        [IterationSetup]
        public void InitializeCache()
        {
            this.lruCache = new LRUDisposableObjectCache<int, StreamContainer>(2*N + 1, TimeSpan.FromHours(1), 1.0, initialScavengeDelay: TimeSpan.FromHours(10), expectedElementCount: 2*N);
            for (int i = 0; i < N; i++)
            {
                var container = new StreamContainer(new MemoryStream(0));
                this.lruCache.Add(i, container);
            }
        }

        [IterationCleanup]
        public void CleanCache()
        {
            this.lruCache = null;
        }
    }

    public class Program
    {
        static void Main(string[] args)
        {
            var summary = BenchmarkRunner.Run<LRUCacheBenchmark>();
            Console.ReadKey();
        }
    }
}