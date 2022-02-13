using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BMCollections
{
    internal class LargeObjectElement<TKey, TContent> : IDisposable
        where TKey: IEquatable<TKey>
        where TContent: ISizeable, IDisposable
    {
        private readonly TContent content;

        public TKey Key { get; private set; }

        public long Size => this.content.Size;

        public DateTime TimeOfCreation { get; private set; }

        public TContent Content => this.content;

        public LargeObjectElement(TKey key, TContent content)
        {
            this.content = content;
            this.Key = key;
            this.TimeOfCreation = DateTime.Now;
        }

        public void Dispose()
        {
            this.content.Dispose();
        }
    }
}
