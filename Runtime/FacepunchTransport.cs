using System;
using System.Collections;
using System.Collections.Generic;
using Steamworks;
using Steamworks.Data;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Netcode;
using UnityEngine;

namespace Netcode.Transports.Facepunch
{
    using SocketConnection = Connection;

    public class FacepunchTransport : NetworkTransport, IConnectionManager, ISocketManager
    {
        private ConnectionManager connectionManager;
        private SocketManager socketManager;
        private Dictionary<ulong, Client> connectedClients;

        private const double SendFailureLogIntervalSeconds = 5.0;

        private string lastDisconnectSummary = string.Empty;
        private int sendFailureCount;
        private int sendLimitExceededCount;
        private Result lastSendResult = Result.OK;
        private double lastSendFailureLogRealtime;

        [Space]
        [Tooltip(
            "The Steam App ID of your game. Technically you're not allowed to use 480, but Valve doesn't do anything about it so it's fine for testing purposes."
        )]
        [SerializeField]
        private uint steamAppId = 480;

        [Tooltip("The Steam ID of the user targeted when joining as a client.")]
        [SerializeField]
        public ulong targetSteamId;

        [Header("Info")]
        [ReadOnly]
        [Tooltip("When in play mode, this will display your Steam ID.")]
        [SerializeField]
        private ulong userSteamId;

        private LogLevel LogLevel => NetworkManager.Singleton.LogLevel;

        private class Client
        {
            public SteamId steamId;
            public SocketConnection connection;
        }

        #region MonoBehaviour Messages

        // private void Awake()
        // {
        //     try
        //     {
        //         SteamClient.Init(steamAppId, false);
        //     }
        //     catch (Exception e)
        //     {
        //         if (LogLevel <= LogLevel.Error)
        //             Debug.LogError($"[{nameof(FacepunchTransport)}] - Caught an exeption during initialization of Steam client: {e}");
        //     }
        //     finally
        //     {
        //         StartCoroutine(InitSteamworks());
        //     }
        // }

        // private void Update()
        // {
        //     SteamClient.RunCallbacks();
        // }

        // private void OnDestroy()
        // {
        //     SteamClient.Shutdown();
        // }

        #endregion

        #region NetworkTransport Overrides

        public override ulong ServerClientId => 0;

        /// <summary>Reads the Steam-authenticated identity for a transport connection, including pending NGO approval.</summary>
        public bool TryGetSteamId(ulong transportClientId, out ulong steamId)
        {
            if (
                connectedClients != null
                && connectedClients.TryGetValue(transportClientId, out Client client)
            )
            {
                steamId = client.steamId.Value;
                return true;
            }

            steamId = 0;
            return false;
        }

        /// <summary>Last Steam disconnect summary for diagnostics. Empty when none recorded.</summary>
        public string LastDisconnectSummary => lastDisconnectSummary;

        public override void DisconnectLocalClient()
        {
            connectionManager?.Connection.Close();

            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(FacepunchTransport)}] - Disconnecting local client.");
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            if (connectedClients.TryGetValue(clientId, out Client user))
            {
                // NGO queues the disconnect reason before closing. Keep reliable messages alive
                // until Steam delivers them; Flush alone does not wait for delivery.
                user.connection.Flush();
                user.connection.Close(linger: true);

                if (LogLevel <= LogLevel.Developer)
                    Debug.Log(
                        $"[{nameof(FacepunchTransport)}] - Disconnecting remote client with ID {clientId}."
                    );
            }
            else if (LogLevel <= LogLevel.Normal)
                Debug.LogWarning(
                    $"[{nameof(FacepunchTransport)}] - Failed to disconnect remote client with ID {clientId}, client not connected."
                );
        }

        public override unsafe ulong GetCurrentRtt(ulong clientId)
        {
            try
            {
                if (clientId == ServerClientId)
                {
                    if (connectionManager == null)
                        return 0;

                    int ping = connectionManager.Connection.QuickStatus().Ping;
                    return ping > 0 ? (ulong)ping : 0;
                }

                if (
                    connectedClients != null
                    && connectedClients.TryGetValue(clientId, out Client user)
                )
                {
                    int ping = user.connection.QuickStatus().Ping;
                    return ping > 0 ? (ulong)ping : 0;
                }
            }
            catch (Exception e)
            {
                if (LogLevel <= LogLevel.Developer)
                    Debug.Log(
                        $"[{nameof(FacepunchTransport)}] - Failed to read RTT for client {clientId}: {e.Message}"
                    );
            }

            return 0;
        }

        public override void Initialize(NetworkManager networkManager = null)
        {
            connectedClients = new Dictionary<ulong, Client>();
            lastDisconnectSummary = string.Empty;
            sendFailureCount = 0;
            sendLimitExceededCount = 0;
            lastSendResult = Result.OK;
            lastSendFailureLogRealtime = 0;
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
                    Debug.LogError(
                        $"[{nameof(FacepunchTransport)}] - Caught an exception while shutting down: {e}"
                    );
            }
        }

        public override void Send(ulong clientId, ArraySegment<byte> data, NetworkDelivery delivery)
        {
            var sendType = NetworkDeliveryToSendType(delivery);

            if (clientId == ServerClientId)
            {
                if (connectionManager == null)
                {
                    if (LogLevel <= LogLevel.Normal)
                        Debug.LogWarning(
                            $"[{nameof(FacepunchTransport)}] - Failed to send packet to server, connection not established."
                        );

                    return;
                }

                Result result = connectionManager.Connection.SendMessage(
                    data.Array,
                    data.Offset,
                    data.Count,
                    sendType
                );
                HandleSendResult(result, clientId, data.Count, delivery);
            }
            else if (connectedClients.TryGetValue(clientId, out Client user))
            {
                Result result = user.connection.SendMessage(
                    data.Array,
                    data.Offset,
                    data.Count,
                    sendType
                );
                HandleSendResult(result, clientId, data.Count, delivery);
            }
            else if (LogLevel <= LogLevel.Normal)
                Debug.LogWarning(
                    $"[{nameof(FacepunchTransport)}] - Failed to send packet to remote client with ID {clientId}, client not connected."
                );
        }

        private void HandleSendResult(
            Result result,
            ulong clientId,
            int size,
            NetworkDelivery delivery
        )
        {
            if (result == Result.OK)
                return;

            sendFailureCount++;
            if (result == Result.LimitExceeded)
                sendLimitExceededCount++;
            lastSendResult = result;

            if (
                sendFailureCount > 1
                && Time.realtimeSinceStartup - lastSendFailureLogRealtime
                    < SendFailureLogIntervalSeconds
            )
                return;
            lastSendFailureLogRealtime = Time.realtimeSinceStartup;

            if (LogLevel <= LogLevel.Normal)
                Debug.LogWarning(
                    $"[{nameof(FacepunchTransport)}] - Send to client {clientId} failed {sendFailureCount} time(s) ({sendLimitExceededCount} queue-full). Last: {lastSendResult} size {size} via {delivery}."
                );
        }

        public override NetworkEvent PollEvent(
            out ulong clientId,
            out ArraySegment<byte> payload,
            out float receiveTime
        )
        {
            connectionManager?.Receive();
            socketManager?.Receive();

            clientId = 0;
            receiveTime = Time.realtimeSinceStartup;
            payload = default;
            return NetworkEvent.Nothing;
        }

        public override bool StartClient()
        {
            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(FacepunchTransport)}] - Starting as client.");

            if (!EnsureSteamInitialized())
                return false;

            if (targetSteamId == SteamClient.SteamId.Value)
            {
                if (LogLevel <= LogLevel.Normal)
                    Debug.LogWarning(
                        $"[{nameof(FacepunchTransport)}] - Connecting to same Steam ID as yourself (host and client on same machine). Use separate Steam accounts or a different machine for proper multiplayer testing."
                    );
            }

            SteamNetworkingUtils.InitRelayNetworkAccess();
            connectionManager = SteamNetworkingSockets.ConnectRelay<ConnectionManager>(
                targetSteamId
            );
            if (connectionManager == null)
            {
                if (LogLevel <= LogLevel.Error)
                    Debug.LogError(
                        $"[{nameof(FacepunchTransport)}] - Failed to connect to relay. Steam Relay Network may not be ready yet."
                    );
                return false;
            }

            connectionManager.Interface = this;
            return true;
        }

        public override bool StartServer()
        {
            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(FacepunchTransport)}] - Starting as server.");

            if (!EnsureSteamInitialized())
                return false;

            SteamNetworkingUtils.InitRelayNetworkAccess();
            socketManager = SteamNetworkingSockets.CreateRelaySocket<SocketManager>();
            if (socketManager == null)
            {
                if (LogLevel <= LogLevel.Error)
                    Debug.LogError(
                        $"[{nameof(FacepunchTransport)}] - Failed to create relay socket. Steam Relay Network may not be ready yet."
                    );
                return false;
            }

            socketManager.Interface = this;
            return true;
        }

        #endregion

        #region ConnectionManager Implementation

        private byte[] payloadCache = new byte[4096];

        private void EnsurePayloadCapacity(int size)
        {
            if (payloadCache.Length >= size)
                return;

            payloadCache = new byte[Math.Max(payloadCache.Length * 2, size)];
        }

        void IConnectionManager.OnConnecting(ConnectionInfo info)
        {
            if (LogLevel <= LogLevel.Developer)
                Debug.Log(
                    $"[{nameof(FacepunchTransport)}] - Connecting with Steam user {info.Identity.SteamId}."
                );
        }

        void IConnectionManager.OnConnected(ConnectionInfo info)
        {
            InvokeOnTransportEvent(
                NetworkEvent.Connect,
                ServerClientId,
                default,
                Time.realtimeSinceStartup
            );

            if (LogLevel <= LogLevel.Developer)
                Debug.Log(
                    $"[{nameof(FacepunchTransport)}] - Connected with Steam user {info.Identity.SteamId}."
                );
        }

        void IConnectionManager.OnDisconnected(ConnectionInfo info)
        {
            lastDisconnectSummary =
                $"client link to {info.Identity.SteamId} state {info.State} end {info.EndReason}";

            InvokeOnTransportEvent(
                NetworkEvent.Disconnect,
                ServerClientId,
                default,
                Time.realtimeSinceStartup
            );

            LogDisconnect(info, $"Disconnected Steam user {info.Identity.SteamId}");
        }

        private void LogDisconnect(ConnectionInfo info, string description)
        {
            bool routine =
                info.EndReason == NetConnectionEnd.Invalid
                || (int)info.EndReason >= (int)NetConnectionEnd.App_Min
                    && (int)info.EndReason <= (int)NetConnectionEnd.App_Max;
            string message =
                $"[{nameof(FacepunchTransport)}] - {description} state {info.State} end {info.EndReason}.";
            if (routine)
            {
                if (LogLevel <= LogLevel.Developer)
                    Debug.Log(message);
            }
            else if (LogLevel <= LogLevel.Normal)
                Debug.LogWarning(message);
        }

        unsafe void IConnectionManager.OnMessage(
            IntPtr data,
            int size,
            long messageNum,
            long recvTime,
            int channel
        )
        {
            EnsurePayloadCapacity(size);

            fixed (byte* payload = payloadCache)
            {
                UnsafeUtility.MemCpy(payload, (byte*)data, size);
            }

            InvokeOnTransportEvent(
                NetworkEvent.Data,
                ServerClientId,
                new ArraySegment<byte>(payloadCache, 0, size),
                Time.realtimeSinceStartup
            );
        }

        #endregion

        #region SocketManager Implementation

        void ISocketManager.OnConnecting(SocketConnection connection, ConnectionInfo info)
        {
            if (LogLevel <= LogLevel.Developer)
                Debug.Log(
                    $"[{nameof(FacepunchTransport)}] - Accepting connection from Steam user {info.Identity.SteamId}."
                );

            connection.Accept();
        }

        void ISocketManager.OnConnected(SocketConnection connection, ConnectionInfo info)
        {
            if (!connectedClients.ContainsKey(connection.Id))
            {
                connectedClients.Add(
                    connection.Id,
                    new Client() { connection = connection, steamId = info.Identity.SteamId }
                );

                InvokeOnTransportEvent(
                    NetworkEvent.Connect,
                    connection.Id,
                    default,
                    Time.realtimeSinceStartup
                );

                if (LogLevel <= LogLevel.Developer)
                    Debug.Log(
                        $"[{nameof(FacepunchTransport)}] - Connected with Steam user {info.Identity.SteamId}."
                    );
            }
            else if (LogLevel <= LogLevel.Normal)
                Debug.LogWarning(
                    $"[{nameof(FacepunchTransport)}] - Failed to connect client with ID {connection.Id}, client already connected."
                );
        }

        void ISocketManager.OnDisconnected(SocketConnection connection, ConnectionInfo info)
        {
            // This callback owns removal and NGO notification. Removing the connection when Close()
            // is requested would make this callback look stale and suppress NetworkEvent.Disconnect.
            if (connectedClients.Remove(connection.Id))
            {
                lastDisconnectSummary =
                    $"client {connection.Id} steam {info.Identity.SteamId} state {info.State} end {info.EndReason}";

                InvokeOnTransportEvent(
                    NetworkEvent.Disconnect,
                    connection.Id,
                    default,
                    Time.realtimeSinceStartup
                );

                LogDisconnect(
                    info,
                    $"Disconnected client {connection.Id} Steam user {info.Identity.SteamId}"
                );
            }
            else if (LogLevel <= LogLevel.Developer)
                Debug.Log(
                    $"[{nameof(FacepunchTransport)}] - Disconnect callback for client {connection.Id}, client already removed."
                );
        }

        unsafe void ISocketManager.OnMessage(
            SocketConnection connection,
            NetIdentity identity,
            IntPtr data,
            int size,
            long messageNum,
            long recvTime,
            int channel
        )
        {
            EnsurePayloadCapacity(size);

            fixed (byte* payload = payloadCache)
            {
                UnsafeUtility.MemCpy(payload, (byte*)data, size);
            }

            InvokeOnTransportEvent(
                NetworkEvent.Data,
                connection.Id,
                new ArraySegment<byte>(payloadCache, 0, size),
                Time.realtimeSinceStartup
            );
        }

        #endregion

        #region Utility Methods

        public bool EnsureSteamInitialized()
        {
            if (SteamClient.IsValid)
                return true;

            try
            {
                SteamClient.Init(steamAppId, true);
                SteamNetworkingUtils.InitRelayNetworkAccess();
                return SteamClient.IsValid;
            }
            catch (Exception e)
            {
                if (LogLevel <= LogLevel.Error)
                    Debug.LogError(
                        $"[{nameof(FacepunchTransport)}] - Failed to initialize Steam: {e.Message}. Ensure Steam is running."
                    );
                return false;
            }
        }

        private IEnumerator InitSteamworks()
        {
            yield return new WaitUntil(() => SteamClient.IsValid);

            SteamNetworkingUtils.InitRelayNetworkAccess();

            if (LogLevel <= LogLevel.Developer)
                Debug.Log(
                    $"[{nameof(FacepunchTransport)}] - Initialized access to Steam Relay Network."
                );

            userSteamId = SteamClient.SteamId;

            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(FacepunchTransport)}] - Fetched user Steam ID.");
        }

        #endregion
    }
}
