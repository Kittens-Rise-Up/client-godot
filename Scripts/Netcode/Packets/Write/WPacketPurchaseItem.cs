using Common.Networking.Message;
using Common.Networking.IO;
using Common.Networking.Packet;

namespace KRU.Networking 
{
    public class WPacketPurchaseItem : IWritable
    {
        public uint StructureID { get; set; }

        public void Write(PacketWriter writer)
        {
            writer.Write(StructureID);
        }
    }
}
