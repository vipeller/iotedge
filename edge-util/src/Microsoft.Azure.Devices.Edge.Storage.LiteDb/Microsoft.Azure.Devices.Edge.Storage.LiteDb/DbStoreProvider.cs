using System.Collections.Generic;

namespace Microsoft.Azure.Devices.Edge.Storage.LiteDb
{
    using System.Collections.Concurrent;
    using System.IO;
    using LiteDB;

    public class DbStoreProvider : IDbStoreProvider
    {
        public const string DbFileName = "data.db";
        readonly LiteDatabase db;
        readonly ConcurrentDictionary<string, IDbStore> entityDbStoreDictionary;

        DbStoreProvider(LiteDatabase db, ConcurrentDictionary<string, IDbStore> partitions)
        {
            this.db = db;
            this.entityDbStoreDictionary = partitions;

        }
        public static DbStoreProvider Create(string path, IEnumerable<string> partitionsList)
        {
            string dbPath = Path.Combine(path, DbFileName);
            var db = new LiteDatabase(dbPath);
            var entityDbStoreDictionary = new ConcurrentDictionary<string, IDbStore>();
            foreach (string collection in partitionsList)
            {
                var dbStore = CollectionDbStore.Create(db, collection);
                entityDbStoreDictionary[collection] = dbStore;
            }

            return new DbStoreProvider(db, entityDbStoreDictionary);
        }

        public void Dispose()
        {
            this.db.Dispose();
        }

        public IDbStore GetDbStore(string partitionName) => this.entityDbStoreDictionary[partitionName];

        public IDbStore GetDbStore() => this.entityDbStoreDictionary["default"];

        public void RemoveDbStore(string partitionName)
        {
            IDbStore partDbStore;
            if (this.entityDbStoreDictionary.TryRemove(partitionName,out partDbStore))
            {
                this.db.DropCollection(partitionName);
                partDbStore.Dispose();
            }
        }
    }
}
