using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BMCollections
{
    public class StreamContainer : IDisposable, ISizeable
    {
        private readonly Stream stream;

        public StreamContainer(Stream stream)
        {
            this.stream = stream;
            this.stream.Seek(0, SeekOrigin.Begin);
        }

        public long Size => this.stream.Length;

        public Stream Stream => this.stream;

        public void Dispose()
        {
            this.stream.Dispose();
        }
    }
}
