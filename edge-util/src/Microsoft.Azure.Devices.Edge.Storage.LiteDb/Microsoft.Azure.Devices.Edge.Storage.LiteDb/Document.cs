namespace Microsoft.Azure.Devices.Edge.Storage.LiteDb
{
    class Document
    {
        public Document()
        {
            this.Id=new byte[0];
            this.Value=new byte[0];
        }

        public Document(byte[] key, byte[] value)
        {
            this.Id = key;
            this.Value = value;
        }
        public byte[] Id;
        public byte[] Value;
    }
}
