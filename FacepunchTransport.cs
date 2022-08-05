using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

#if STEAM

using Steamworks;
using Steamworks.Data;
#endif

using Unity.Netcode;

using UnityEngine;

#if STEAM
namespace Netcode.Transports.FacepunchTransport
{
	using SocketConnection = Connection;
	
	public class FacepunchTransport : NetworkTransport, IConnectionManager, ISocketManager
	{
		private NetIdentity hostIdentity;
		private bool hostIdentitySet;

		private ConnectionManager connectionManager;
		private SocketManager socketManager;
		private Dictionary<ulong, Client> connectedClients;
		
		private LogLevel LogLevel => NetworkManager.Singleton.LogLevel;

		public NetIdentity HostIdentity {
			get => hostIdentity;
			set {
				hostIdentitySet = true;
				hostIdentity = value;
			}
		}

		#region NetworkTransport Overrides

        public override ulong ServerClientId => 0;

        public override void Send(ulong clientId, ArraySegment<byte> data, NetworkDelivery delivery)
        {
	        var sendType = NetworkDeliveryToSendType(delivery);

	        if (clientId == ServerClientId)
		        connectionManager.Connection.SendMessage(data.Array, data.Offset,data.Count, sendType);
	        else if (connectedClients.TryGetValue(clientId, out Client user))
		        user.connection.SendMessage(data.Array, data.Offset,data.Count, sendType);
	        else if (LogLevel <= LogLevel.Normal)
		        Debug.LogWarning($"[{nameof(FacepunchTransport)}] - Failed to send packet to remote client with ID {clientId}, client not connected.");
        }

        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
	        connectionManager?.Receive();
	        socketManager?.Receive();

	        clientId = 0;
	        receiveTime = Time.realtimeSinceStartup;
	        payload = default;
	        return NetworkEvent.Nothing;
        }
        
        public override void Initialize(NetworkManager networkManager)
        {
	        connectedClients = new Dictionary<ulong, Client>();
            
	        if (!SteamClient.IsValid) {
		        Debug.LogWarning("Make sure you initialize Steam with your own AppID! Defaulting to 480...");
		        SteamClient.Init(480);
	        }

	        if (hostIdentitySet == false) {
		        throw new MissingHostIdentityException();
	        }
        }
        
        public override bool StartClient()
        {
	        if (LogLevel <= LogLevel.Developer)
		        Debug.Log($"[{nameof(FacepunchTransport)}] - Starting as client.");

	        connectionManager = hostIdentity.IsSteamId ? SteamNetworkingSockets.ConnectRelay<ConnectionManager>(hostIdentity.SteamId) : SteamNetworkingSockets.ConnectNormal<ConnectionManager>(hostIdentity.Address);
	        connectionManager.Interface = this;
	        return true;
        }

        public override bool StartServer()
        {
	        if (LogLevel <= LogLevel.Developer)
		        Debug.Log($"[{nameof(FacepunchTransport)}] - Starting as server.");

	        socketManager = hostIdentity.IsSteamId ? SteamNetworkingSockets.CreateRelaySocket<SocketManager>() : SteamNetworkingSockets.CreateNormalSocket<SocketManager>(hostIdentity.Address);
	        socketManager.Interface = this;
	        return true;
        }
        
        public override void DisconnectLocalClient()
        {
            connectionManager.Connection.Close();

            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(FacepunchTransport)}] - Disconnecting local client.");
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            if (connectedClients.TryGetValue(clientId, out Client user))
            {
                user.connection.Close();
                connectedClients.Remove(clientId);

                if (LogLevel <= LogLevel.Developer)
                    Debug.Log($"[{nameof(FacepunchTransport)}] - Disconnecting remote client with ID {clientId}.");
            }
            else if (LogLevel <= LogLevel.Normal)
                Debug.LogWarning($"[{nameof(FacepunchTransport)}] - Failed to disconnect remote client with ID {clientId}, client not connected.");
        }

        public override ulong GetCurrentRtt(ulong clientId)
        {
            return 0;
        }

        public override void Shutdown()
        {
            try
            {
                if (LogLevel <= LogLevel.Developer)
                    Debug.Log($"[{nameof(FacepunchTransport)}] - Shutting down.");

                connectionManager?.Close();
                socketManager?.Close();
            }
            catch (Exception e)
            {
                if (LogLevel <= LogLevel.Error)
                    Debug.LogError($"[{nameof(FacepunchTransport)}] - Caught an exception while shutting down: {e}");
            }
        }

        #endregion

        #region ConnectionManager Implementation

        void IConnectionManager.OnConnecting(ConnectionInfo info)
        {
            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(FacepunchTransport)}] - Connecting with Steam user {info.Identity.SteamId}.");
        }

        void IConnectionManager.OnConnected(ConnectionInfo info)
        {
            InvokeOnTransportEvent(NetworkEvent.Connect, ServerClientId, default, Time.realtimeSinceStartup);

            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(FacepunchTransport)}] - Connected with Steam user {info.Identity.SteamId}.");
        }

        void IConnectionManager.OnDisconnected(ConnectionInfo info)
        {
            InvokeOnTransportEvent(NetworkEvent.Disconnect, ServerClientId, default, Time.realtimeSinceStartup);

            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(FacepunchTransport)}] - Disconnected Steam user {info.Identity.SteamId}.");
        }

        void IConnectionManager.OnMessage(IntPtr data, int size, long messageNum, long recvTime, int channel)
        {
            byte[] payload = new byte[size];
            Marshal.Copy(data, payload, 0, size);
            InvokeOnTransportEvent(NetworkEvent.Data, ServerClientId, new ArraySegment<byte>(payload, 0, size), Time.realtimeSinceStartup);
        }

        #endregion

        #region SocketManager Implementation

        void ISocketManager.OnConnecting(SocketConnection connection, ConnectionInfo info)
        {
            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(FacepunchTransport)}] - Accepting connection from Steam user {info.Identity.SteamId}.");

            connection.Accept();
        }

        void ISocketManager.OnConnected(SocketConnection connection, ConnectionInfo info)
        {
            if (!connectedClients.ContainsKey(connection.Id))
            {
                connectedClients.Add(connection.Id, new Client
                {
                    connection = connection,
                    steamId = info.Identity.SteamId
                });

                InvokeOnTransportEvent(NetworkEvent.Connect, connection.Id, default, Time.realtimeSinceStartup);

                if (LogLevel <= LogLevel.Developer)
                    Debug.Log($"[{nameof(FacepunchTransport)}] - Connected with Steam user {info.Identity.SteamId}.");
            }
            else if (LogLevel <= LogLevel.Normal)
                Debug.LogWarning($"[{nameof(FacepunchTransport)}] - Failed to connect client with ID {connection.Id}, client already connected.");
        }

        void ISocketManager.OnDisconnected(SocketConnection connection, ConnectionInfo info)
        {
            connectedClients.Remove(connection.Id);

            InvokeOnTransportEvent(NetworkEvent.Disconnect, connection.Id, default, Time.realtimeSinceStartup);

            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(FacepunchTransport)}] - Disconnected Steam user {info.Identity.SteamId}");
        }

        void ISocketManager.OnMessage(SocketConnection connection, NetIdentity identity, IntPtr data, int size, long messageNum, long recvTime, int channel)
        {
            byte[] payload = new byte[size];
            Marshal.Copy(data, payload, 0, size);
            InvokeOnTransportEvent(NetworkEvent.Data, connection.Id, new ArraySegment<byte>(payload, 0, size), Time.realtimeSinceStartup);
        }

        #endregion

        #region Utilities

        private class Client
        {
	        public SteamId steamId;
	        public SocketConnection connection;
        }
        
        private class MissingHostIdentityException : Exception
        {
	        public override string Message { get => "Host identity not set! Make sure you set \"HostIdentity\" before you start!"; }
        }

        private SendType NetworkDeliveryToSendType(NetworkDelivery delivery)
        {
	        return delivery switch
	        {
		        NetworkDelivery.Reliable => SendType.Reliable,
		        NetworkDelivery.ReliableFragmentedSequenced => SendType.Reliable,
		        NetworkDelivery.ReliableSequenced => SendType.Reliable,
		        NetworkDelivery.Unreliable => SendType.Unreliable,
		        NetworkDelivery.UnreliableSequenced => SendType.Unreliable,
		        _ => SendType.Reliable
	        };
        }
        
        #endregion

	}

	
}
#endif