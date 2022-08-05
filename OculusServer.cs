using System;
using Oculus.Platform;
using Oculus.Platform.Models;
using UnityEngine;

namespace Unity.Netcode.Transports.Oculus
{
    public class OculusServer : OculusCommon
    {
        private event Action<ulong> OnConnected;
        private event Action<ulong, byte[], SendPolicy> OnReceivedData;
        private event Action<ulong> OnDisconnected;
        private event Action<int, Exception> OnReceivedError;
        private readonly int maxConnections;
        public readonly BidirectionalDictionary<ulong, int> oculusToNetcodeDictionary;
        private int nextConnectionID;

        private OculusServer(int maxConnections)
        {
            this.maxConnections = maxConnections;
            oculusToNetcodeDictionary = new();
            nextConnectionID = 1;
            Net.SetPeerConnectRequestCallback(OnPeerConnectionRequest);
            Net.SetConnectionStateChangedCallback(OnConnectionStatusChanged);
        }

        public static OculusServer CreateServer(OculusTransport transport, int maxConnections)
        {
            OculusServer s = new OculusServer(maxConnections);
            myTransport = transport;
            s.OnReceivedData += delegate(ulong id, byte[] bytes, SendPolicy policy)
            {
                myTransport.InvokeTransportEvent(NetworkEvent.Data, id, new ArraySegment<byte>(bytes),
                    Time.realtimeSinceStartup);
            };
            s.OnConnected += delegate(ulong id)
            {
                myTransport.InvokeTransportEvent(NetworkEvent.Connect, id, default, Time.realtimeSinceStartup);
                Debug.Log("user connected with id " + id);
            };

            s.OnDisconnected += delegate(ulong id)
            {
                myTransport.InvokeTransportEvent(NetworkEvent.Disconnect, id, default, Time.realtimeSinceStartup);
            };

            s.OnReceivedError += (id, exception) => myTransport.InvokeTransportEvent(NetworkEvent.TransportFailure,
                (ulong) id, default, Time.realtimeSinceStartup);
            if (!Core.IsInitialized())
            {
                Debug.Log("Oculus platform not intitialized");
            }
            Debug.Log("server created");
            return s;
        }

        private void OnPeerConnectionRequest(Message<NetworkingPeer> message)
        {
            Debug.Log("onpeer request");
            var id = message.Data.ID;
            if (oculusToNetcodeDictionary.TryGetValue(id, out int _))
            {
                Debug.LogError($"Incoming connection {id} already exists");
            }
            else
            {
                if (oculusToNetcodeDictionary.Count >= maxConnections)
                {
                    Debug.Log("Max player count exceeed. rejecting");
                }
                else
                {
                    Debug.Log($"Accepted connection {id}");
                    Net.Accept(id);
                }
            }
        }

        private void OnConnectionStatusChanged(Message<NetworkingPeer> message)
        {
            var oculusId = message.Data.ID;
            switch (message.Data.State)
            {
                case PeerConnectionState.Unknown:
                    break;
                case PeerConnectionState.Connected:
                case PeerConnectionState.Timeout:
                    Debug.Log($"Client with OculusID {oculusId} connected. Assigning connection id {message.Data.ID}");
                    int connectionID = nextConnectionID++;
                    oculusToNetcodeDictionary.Add(oculusId, connectionID);
                    OnConnected?.Invoke(oculusId);
                    break;
                case PeerConnectionState.Closed:
                    if (oculusToNetcodeDictionary.TryGetValue(oculusId, out int connID))
                    {
                        InternalDisconnect(connID, oculusId);
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void InternalDisconnect(int connID, ulong userID)
        {
            if (oculusToNetcodeDictionary.TryGetValue(userID, out int _))
            {
                oculusToNetcodeDictionary.Remove(connID);
                OnDisconnected?.Invoke(userID);
            }
            else
            {
                Debug.Log("Nothing to disconnect");
            }
        }

        public bool Disconnect(int connectionID)
        {
            if (oculusToNetcodeDictionary.TryGetValue(connectionID, out ulong userID))
            {
                Debug.Log($"closing connection for user {connectionID}");
                Net.Close(userID);
                return true;
            }
            else
            {
                Debug.Log($"trying to disconnect unknown id {connectionID}");
                return false;
            }
        }

        public void FlushData()
        {
        }

        public void RecieveData()
        {
            Packet packet;
            while ((packet = Net.ReadPacket()) != null)
            {
                if (packet.SenderID != 0)
                {
                    (byte[] data, SendPolicy policy) = ProcessPacket(packet);
                    OnReceivedData?.Invoke(packet.SenderID, data, policy);
                }
                else
                {
                    Debug.LogWarning("Ignoring packet from sender with 0 id");
                }
            }
        }

        public void Send(int connectionID, byte[] data, SendPolicy policy)
        {
            if (oculusToNetcodeDictionary.TryGetValue(connectionID, out ulong userID))
            {
                var sent = SendPacket(userID, data, policy);

                if (!sent)
                {
                    Debug.Log($"Could not send");
                }
            }
            else
            {
                Debug.Log("Trying to send on unknown connection: " + connectionID);
                OnReceivedError?.Invoke(connectionID, new Exception("ERROR Unknown Connection"));
            }
        }

        public string ServerGetClientAddress(int connectionId)
        {
            if (oculusToNetcodeDictionary.TryGetValue(connectionId, out ulong userID))
            {
                return userID.ToString();
            }
            else
            {
                Debug.Log("Trying to get info on unknown connection: " + connectionId);
                OnReceivedError?.Invoke(connectionId, new Exception("ERROR Unknown Connection"));
                return string.Empty;
            }
        }

        public void ShutDown()
        {
            Net.SetPeerConnectRequestCallback(_ => { });
            Net.SetConnectionStateChangedCallback(_ => { });
            DisposeAllPackets();
        }
    }
}