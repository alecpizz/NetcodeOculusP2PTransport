using System;
using System.Collections.Generic;
using Oculus.Platform;
using Oculus.Platform.Models;
using UnityEngine;

namespace Unity.Netcode.Transports.Oculus
{
    public class OculusClient : OculusCommon
    {
        public bool Connected { get; private set; }
        public bool Error { get; private set; }
        private TimeSpan ConnectionTimeOut;
        private event Action<byte[], SendPolicy> OnReceivedData;
        private event Action OnConnected;
        private event Action OnDisconnected;

        private ulong hostID;
        private List<Action> bufferedData;

        private OculusClient() => bufferedData = new List<Action>();

        public static OculusClient CreateClient(OculusTransport transport, ulong host)
        {
            //iconnectionmanager is client....
            myTransport = transport;
            var c = new OculusClient();

            c.OnConnected += delegate
            {
                transport.InvokeTransportEvent(NetworkEvent.Connect, transport.ServerClientId, default,
                    Time.realtimeSinceStartup);
            };

            c.OnDisconnected += delegate
            {
                transport.InvokeTransportEvent(NetworkEvent.Disconnect, transport.ServerClientId, default,
                    Time.realtimeSinceStartup);
            };

            c.OnReceivedData += delegate(byte[] bytes, SendPolicy arg3)
            {
                transport.InvokeTransportEvent(NetworkEvent.Data, transport.ServerClientId,
                    new ArraySegment<byte>(bytes), Time.realtimeSinceStartup);
            };
            //c.OnConnected += transport.OnClientConnected;
            //c.OnConnected += () => transport.
            if (host != 0)
            {
                if (Core.IsInitialized())
                {
                    c.Connect(host);
                }
                else
                {
                    Debug.Log("Oculus platform not initalized");
                    c.OnConnectionFailed();
                }
            }
            else
            {
                Debug.Log("hostID invalid or unset");
            }

            return c;
        }

        private void Connect(ulong host)
        {
            if (host != 0)
            {
                hostID = host;
                Net.SetConnectionStateChangedCallback(OnConnectionStatusChanged);
                Net.Connect(hostID);
            }
            else
            {
                Debug.Log($"Couldn't parse {host} to ulong, invoke onconnection failed");
                Error = true;
            }
        }

        private void OnConnectionStatusChanged(Message<NetworkingPeer> message)
        {
            Debug.Log($"Connection state changed : {message.Data.State}");
            switch (message.Data.State)
            {
                case PeerConnectionState.Unknown:
                    break;
                case PeerConnectionState.Connected:
                    Connected = true;
                    OnConnected?.Invoke();

                    if (bufferedData.Count > 0)
                    {
                        foreach (var a in bufferedData)
                        {
                            a();
                        }
                    }

                    break;
                case PeerConnectionState.Timeout:
                    break;
                case PeerConnectionState.Closed:
                    InternalDisconnect();
                    break;
            }
        }

        public void Disconnect()
        {
            if (Net.IsConnected(hostID))
            {
                Net.Close(hostID);
            }
        }

        protected void Dispose()
        {
            Net.SetConnectionStateChangedCallback(_ => { });
            DisposeAllPackets();
            myTransport = null;
        }

        private void InternalDisconnect()
        {
            Dispose();
            Connected = false;
            OnDisconnected?.Invoke();
            Debug.Log("Internally disconnecting");
        }

        public void RecieveData()
        {
            Packet packet;
            while ((packet = Net.ReadPacket()) != null)
            {
                (byte[] data, SendPolicy policy) = ProcessPacket(packet);
                if (Connected)
                {
                    OnReceivedData(data, policy);
                }
                else
                {
                    bufferedData.Add(() => OnReceivedData(data, policy));
                }
            }
        }

        public void Send(byte[] data, SendPolicy policy)
        {
            var sent = SendPacket(hostID, data, policy);
            if (!sent)
            {
                Debug.Log("Couldn't send data");
                InternalDisconnect();
            }
        }

        private void OnConnectionFailed() => OnDisconnected?.Invoke();

        public void FlushData()
        {
        }
    }
}