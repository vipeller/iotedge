using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Azure.Devices.Edge.Storage.LiteDb
{
    using System.Dynamic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using LiteDB;
    using Microsoft.Azure.Devices.Edge.Util;
    public class CollectionDbStore : IDbStore
    {
        LiteCollection<Document> collection;

        CollectionDbStore(LiteCollection<Document> collection)
        {
            this.collection = collection;
        }

        public static IDbStore Create(LiteDatabase db, string collectionName)
        {
            return new CollectionDbStore(db.GetCollection<Document>(collectionName));
        }

        public void Dispose()
        {
        }

        public Task Put(byte[] key, byte[] value)
        {
            var putDocument = new Document(key, value);
            this.collection.Insert(putDocument);
            return Task.CompletedTask;
        }

        public Task<Option<byte[]>> Get(byte[] key)
        {
            var results = this.collection.Find(Query.EQ("Id", key));

            return Task.FromResult(Option.Maybe(results.FirstOrDefault()).Map(r => r.Value));
        }

        public Task Remove(byte[] key)
        {
            this.collection.Delete(Query.EQ("Id", key));
            return Task.CompletedTask;
        }

        public Task<bool> Contains(byte[] key)
        {
            var results = this.collection.Count(Query.EQ("Id", key));

            return Task.FromResult(results > 0);
        }

        public Task<Option<(byte[] key, byte[] value)>> GetFirstEntry()
        {
            var results = this.collection.Find(Query.All(), 0, 1);
            return Task.FromResult(Option.Maybe(results.FirstOrDefault()).Map(r => (r.Id, r.Value)));
        }

        public Task<Option<(byte[] key, byte[] value)>> GetLastEntry()
        {
            var count = this.collection.Count(Query.All());
            var results = this.collection.Find(Query.All(), count - 1);
            return Task.FromResult(Option.Maybe(results.FirstOrDefault()).Map(r => (r.Id, r.Value)));
        }

        public Task IterateBatch(int batchSize, Func<byte[], byte[], Task> perEntityCallback) => this.IterateBatch(batchSize, perEntityCallback, CancellationToken.None);

        public Task IterateBatch(byte[] startKey, int batchSize, Func<byte[], byte[], Task> perEntityCallback) => throw new NotImplementedException();

        public Task Put(byte[] key, byte[] value, CancellationToken cancellationToken) => this.Put(key,value);

        public Task<Option<byte[]>> Get(byte[] key, CancellationToken cancellationToken) => this.Get(key);

        public Task Remove(byte[] key, CancellationToken cancellationToken) => this.Remove(key);

        public Task<bool> Contains(byte[] key, CancellationToken cancellationToken) => this.Contains(key);

        public Task<Option<(byte[] key, byte[] value)>> GetFirstEntry(CancellationToken cancellationToken) => this.GetFirstEntry();

        public Task<Option<(byte[] key, byte[] value)>> GetLastEntry(CancellationToken cancellationToken) => this.GetLastEntry();

        public Task IterateBatch(int batchSize, Func<byte[], byte[], Task> perEntityCallback, CancellationToken cancellationToken)
        {
            var batch = this.collection.Find(Query.All(), 0, batchSize);
            List<Task> waitlist = new List<Task>();
            foreach (Document document in batch)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                waitlist.Add(perEntityCallback(document.Id, document.Value));
            }

            return Task.WhenAll(waitlist);
        }

        public Task IterateBatch(byte[] startKey, int batchSize, Func<byte[], byte[], Task> perEntityCallback, CancellationToken cancellationToken) => throw new NotImplementedException();
    }
}
