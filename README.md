# LRUCacheDisposableObject

A library providing a scalable LRU cache for disposable objects. Its main use
will be to be used as a cache for large files on a GRPC microservice, so that we
don't have to send them more than once over the wire.

It supports scavenging based on the total size of the objects and time since creation.
Scavenging happens on a separate thread and is guaranteed to only run on one thread at a time.

The cache supports the IDictionary interface.

For examples, see the tests.