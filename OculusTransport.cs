using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NaughtyAttributes;
using Networking;
using UnityEngine;
using Oculus.Platform;
using Oculus.Platform.Models;


namespace Unity.Netcode.Transports.Oculus
{
    public class OculusTransport : NetworkTransport
    {
        private static OculusClient client;
        private static OculusServer server;
        [ShowNativeProperty] public bool ClientActive => client != null;
        [ShowNativeProperty] public bool ServerActive => server != null;
        private User user;
        public ulong HostId;
        public ulong currentUserID;
        public string currentUserOculusID;
        public Room currentRoom;

        public void Login(User user)
        {
            this.user = user;
        }

        private void LateUpdate()
        {
            if (!enabled) return;
            client?.RecieveData();
            server?.RecieveData();
        }

        public void InvokeTransportEvent(NetworkEvent eventType, ulong clientId, ArraySegment<byte> payload,
            float receiveTime)
        {
            InvokeOnTransportEvent(eventType, clientId, payload, receiveTime);
        }

        public override ulong ServerClientId => 0;
        public int lastPayloadSize;
        public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
        {
            var delivery = DeliveryModeToSendPolicy(networkDelivery);
            byte[] data = new byte[payload.Count];
            Array.Copy(payload.Array, payload.Offset, data, 0, payload.Count);
            if (ServerActive)
            {
                if (server.oculusToNetcodeDictionary.TryGetValue(clientId, out int id))
                {
                    server.Send(id, data, delivery);
                }
            }
            else
            {
                client.Send(data, delivery);
            }
        }

        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload,
            out float receiveTime)
        {
            server?.RecieveData();
            client?.RecieveData();
            clientId = 0;
            receiveTime = Time.realtimeSinceStartup;
            payload = default;
            return NetworkEvent.Nothing;
        }

        public override bool StartClient()
        {
            if (!Core.IsInitialized())
            {
                Debug.Log("Oculus not initalized, sever couldn't start");
                return false;
            }

            if (ServerActive)
            {
                Debug.LogError("transport already as a server");
                return false;
            }

            if (!ClientActive || client.Error)
            {
                if (HostId == 0)
                {
                    Debug.Log("Host id must be set");
                    return false;
                }

                client = OculusClient.CreateClient(this, HostId);
                return true;
            }
            else
            {
                Debug.Log("Client already running");
                return false;
            }

            return false;
        }

        public override bool StartServer()
        {
            if (!Core.IsInitialized())
            {
                Debug.Log("Oculus not initalized, sever couldn't start");
                return false;
            }

            if (ClientActive)
            {
                Debug.LogError("transport already as a client");
                return false;
            }

            if (!ServerActive)
            {
                server = OculusServer.CreateServer(this, 20);
                return true;
            }

            Debug.LogError("Server already started");
            return false;
        }


        public override void DisconnectRemoteClient(ulong clientId)
        {
            if (ServerActive)
            {
                if (server.oculusToNetcodeDictionary.TryGetValue(clientId, out int ID))
                {
                    server.Disconnect(ID);
                }
            }
        }

        public override void DisconnectLocalClient()
        {
            Rooms.Leave(currentRoom.ID);
            client?.Disconnect();
        }

        public override ulong GetCurrentRtt(ulong clientId)
        {
            return 0;
        }

        public override void Shutdown()
        {
            ulong id = currentUserID;

            if (ServerActive)
            {
                if (server.oculusToNetcodeDictionary.TryGetValue(id, out int connectionID))
                    server?.Disconnect(connectionID);
            }

            Rooms.Leave(currentRoom.ID);
            server?.ShutDown();
            server = null;
            client?.Disconnect();
            client = null;
        }

        public override void Initialize(NetworkManager networkManager = null)
        {
            //thhis is a start method
        }

        private void Start()
        {
            try
            {
                Core.AsyncInitialize().OnComplete(EntitlementCallback);
            }
            catch (UnityException e)
            {
                Debug.LogError("platform failed to initialize");
                Debug.LogException(e);
            }
        }

        private void EntitlementCallback(Message msg)
        {
            if (msg.IsError)
            {
                Debug.LogError("You are NOT entitled to use this app.");
            }
            else
            {
                // Log the succeeded entitlement check for debugging.
                Debug.Log("You are entitled to use this app.");
                Users.GetLoggedInUser().OnComplete(LoggedInUserCallback);
            }
        }

        private void LoggedInUserCallback(Message<User> msg)
        {
            if (msg.IsError)
            {
                Debug.Log(msg.GetError().Message);
                return;
            }

            currentUserID = msg.Data.ID;
            Debug.Log($"Current user id {msg.Data.ID}");
            currentUserOculusID = msg.Data.OculusID;
            Login(user);
        }


        private SendPolicy DeliveryModeToSendPolicy(NetworkDelivery deliveryMode)
        {
            switch (deliveryMode)
            {
                case NetworkDelivery.Unreliable:
                    return SendPolicy.Unreliable;
                case NetworkDelivery.Reliable:
                    return SendPolicy.Reliable;
                default:
                    return SendPolicy.Reliable;
            }
        }
    }
}