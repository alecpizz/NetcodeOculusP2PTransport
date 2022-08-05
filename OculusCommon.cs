using Oculus.Platform;
using Unity.Netcode.Transports.Oculus;

namespace Unity.Netcode.Transports.Oculus
{
    public class OculusCommon
    {
        public static OculusTransport myTransport;

        public static bool CanParseID(string address)
        {
            if (ulong.TryParse(address, out ulong _))
            {
                return true;
            }

            return false;
        }

        public int ReliableMaxMessageSize = 65535;
        public int UnreliableMaxMessageSize = 1200;

        protected bool SendPacket(ulong userID, byte[] data, SendPolicy sendMode)
        {
            return Net.SendPacket(userID, data, sendMode);
        }

        protected (byte[], SendPolicy) ProcessPacket(Packet packet)
        {
            byte[] managedArray = new byte[packet.Size];
            packet.ReadBytes(managedArray);
            packet.Dispose();
            return (managedArray, packet.Policy);
        }

        public static void DisposeAllPackets()
        {
            Packet packet;
            while ((packet = Net.ReadPacket()) != null)
            {
                packet.Dispose();
            }
        }
    }
}