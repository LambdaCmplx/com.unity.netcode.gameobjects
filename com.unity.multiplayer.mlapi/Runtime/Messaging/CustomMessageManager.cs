using System;
using System.Collections.Generic;
using System.IO;
using MLAPI.Configuration;
using MLAPI.Logging;
using MLAPI.Security;
using MLAPI.Serialization;
using MLAPI.Serialization.Pooled;
using MLAPI.Hashing;
using MLAPI.Transports;

namespace MLAPI.Messaging
{
    /// <summary>
    /// The manager class to manage custom messages, note that this is different from the NetworkingManager custom messages.
    /// These are named and are much easier to use.
    /// </summary>
    public static class CustomMessagingManager
    {
        #region Unnamed
        /// <summary>
        /// Delegate used for incoming unnamed messages
        /// </summary>
        /// <param name="clientId">The clientId that sent the message</param>
        /// <param name="stream">The stream containing the message data</param>
        public delegate void UnnamedMessageDelegate(ulong clientId, Stream stream);

        /// <summary>
        /// Event invoked when unnamed messages arrive
        /// </summary>
        public static event UnnamedMessageDelegate OnUnnamedMessage;

        internal static void InvokeUnnamedMessage(NetworkingManager networkingManager, ulong clientId, Stream stream)
        {
            if (OnUnnamedMessage != null)
            {
                OnUnnamedMessage(clientId, stream);
            }

            networkingManager.InvokeOnIncomingCustomMessage(clientId, stream);
        }


        /// <summary>
        /// Sends unnamed message to a list of clients
        /// </summary>
        /// <param name="networkingManager">The NetworkingManager instance to use for sending.</param>
        /// <param name="clientIds">The clients to send to, sends to everyone if null</param>
        /// <param name="stream">The message stream containing the data</param>
        /// <param name="channel">The channel to send the data on</param>
        /// <param name="security">The security settings to apply to the message</param>
        public static void SendUnnamedMessage(NetworkingManager networkingManager, List<ulong> clientIds, BitStream stream, Channel channel = Channel.Internal, SecuritySendFlags security = SecuritySendFlags.None)
        {
            if (!networkingManager.IsServer)
            {
                if (NetworkingManager.LogLevel <= LogLevel.Error) NetworkLog.LogWarning("Can not send unnamed messages to multiple users as a client");
                return;
            }

            networkingManager.MessageSender.Send(MLAPIConstants.MLAPI_UNNAMED_MESSAGE, channel, clientIds, stream, security);
        }

        /// <summary>
        /// Sends a unnamed message to a specific client
        /// </summary>
        /// <param name="networkingManager">The NetworkingManager instance to use for sending.</param>
        /// <param name="clientId">The client to send the message to</param>
        /// <param name="stream">The message stream containing the data</param>
        /// <param name="channel">The channel tos end the data on</param>
        /// <param name="security">The security settings to apply to the message</param>
        public static void SendUnnamedMessage(NetworkingManager networkingManager, ulong clientId, BitStream stream, Channel channel = Channel.Internal, SecuritySendFlags security = SecuritySendFlags.None)
        {
            networkingManager.MessageSender.Send(clientId, MLAPIConstants.MLAPI_UNNAMED_MESSAGE, channel, stream, security);
        }
        #endregion
        #region Named
        /// <summary>
        /// Delegate used to handle named messages
        /// </summary>
        public delegate void HandleNamedMessageDelegate(ulong sender, Stream payload);

        private static readonly Dictionary<ulong, HandleNamedMessageDelegate> namedMessageHandlers16 = new Dictionary<ulong, HandleNamedMessageDelegate>();
        private static readonly Dictionary<ulong, HandleNamedMessageDelegate> namedMessageHandlers32 = new Dictionary<ulong, HandleNamedMessageDelegate>();
        private static readonly Dictionary<ulong, HandleNamedMessageDelegate> namedMessageHandlers64 = new Dictionary<ulong, HandleNamedMessageDelegate>();


        internal static void InvokeNamedMessage(NetworkingManager networkingManager, ulong hash, ulong sender, Stream stream)
        {
            if (networkingManager == null)
            {
                // We dont know what size to use. Try every (more collision prone)
                if (namedMessageHandlers16.ContainsKey(hash))
                {
                    namedMessageHandlers16[hash](sender, stream);
                }

                if (namedMessageHandlers32.ContainsKey(hash))
                {
                    namedMessageHandlers32[hash](sender, stream);
                }

                if (namedMessageHandlers64.ContainsKey(hash))
                {
                    namedMessageHandlers64[hash](sender, stream);
                }
            }
            else
            {
                // Only check the right size.
                if (networkingManager.NetworkConfig.RpcHashSize == HashSize.VarIntTwoBytes)
                {
                    if (namedMessageHandlers16.ContainsKey(hash))
                    {
                        namedMessageHandlers16[hash](sender, stream);
                    }
                }
                else if (networkingManager.NetworkConfig.RpcHashSize == HashSize.VarIntFourBytes)
                {
                    if (namedMessageHandlers32.ContainsKey(hash))
                    {
                        namedMessageHandlers32[hash](sender, stream);
                    }
                }
                else if (networkingManager.NetworkConfig.RpcHashSize == HashSize.VarIntEightBytes)
                {
                    if (namedMessageHandlers64.ContainsKey(hash))
                    {
                        namedMessageHandlers64[hash](sender, stream);
                    }
                }
            }
        }

        /// <summary>
        /// Registers a named message handler delegate.
        /// </summary>
        /// <param name="name">Name of the message.</param>
        /// <param name="callback">The callback to run when a named message is received.</param>
        public static void RegisterNamedMessageHandler(string name, HandleNamedMessageDelegate callback)
        {
            namedMessageHandlers16[name.GetStableHash16()] = callback;
            namedMessageHandlers32[name.GetStableHash32()] = callback;
            namedMessageHandlers64[name.GetStableHash64()] = callback;
        }

        /// <summary>
        /// Unregisters a named message handler.
        /// </summary>
        /// <param name="name">The name of the message.</param>
        public static void UnregisterNamedMessageHandler(string name)
        {
            namedMessageHandlers16.Remove(name.GetStableHash16());
            namedMessageHandlers32.Remove(name.GetStableHash32());
            namedMessageHandlers64.Remove(name.GetStableHash64());
        }

        /// <summary>
        /// Sends a named message
        /// </summary>
        /// <param name="networkingManager">The NetworkingManager to send this message over. </param>
        /// <param name="name">The message name to send</param>
        /// <param name="clientId">The client to send the message to</param>
        /// <param name="stream">The message stream containing the data</param>
        /// <param name="channel">The channel to send the data on</param>
        /// <param name="security">The security settings to apply to the message</param>
        public static void SendNamedMessage(NetworkingManager networkingManager, string name, ulong clientId, Stream stream, Channel channel = Channel.Internal, SecuritySendFlags security = SecuritySendFlags.None)
        {
            ulong hash = 0;
            switch (networkingManager.NetworkConfig.RpcHashSize)
            {
                case HashSize.VarIntTwoBytes:
                    hash = name.GetStableHash16();
                    break;
                case HashSize.VarIntFourBytes:
                    hash = name.GetStableHash32();
                    break;
                case HashSize.VarIntEightBytes:
                    hash = name.GetStableHash64();
                    break;
            }

            using (PooledBitStream messageStream = PooledBitStream.Get())
            {
                using (PooledBitWriter writer = PooledBitWriter.Get(messageStream))
                {
                    writer.WriteUInt64Packed(hash);
                }

                messageStream.CopyFrom(stream);

                InternalMessageSender.Send(clientId, MLAPIConstants.MLAPI_NAMED_MESSAGE, channelId, messageStream, security);
            }
        }

        /// <summary>
        /// Sends the named message
        /// </summary>
        /// <param name="networkingManager">The NetworkingManager to send this message over.</param>
        /// <param name="name">The message name to send</param>
        /// <param name="clientIds">The clients to send to, sends to everyone if null</param>
        /// <param name="stream">The message stream containing the data</param>
        /// <param name="channel">The channel to send the data on</param>
        /// <param name="security">The security settings to apply to the message</param>
        public static void SendNamedMessage(NetworkingManager networkingManager, string name, List<ulong> clientIds, Stream stream, Channel channel = Channel.Internal, SecuritySendFlags security = SecuritySendFlags.None)
        {
            ulong hash = 0;
            switch (networkingManager.NetworkConfig.RpcHashSize)
            {
                case HashSize.VarIntTwoBytes:
                    hash = name.GetStableHash16();
                    break;
                case HashSize.VarIntFourBytes:
                    hash = name.GetStableHash32();
                    break;
                case HashSize.VarIntEightBytes:
                    hash = name.GetStableHash64();
                    break;
            }

            using (PooledBitStream messageStream = PooledBitStream.Get())
            {
                using (PooledBitWriter writer = PooledBitWriter.Get(messageStream))
                {
                    writer.WriteUInt64Packed(hash);
                }

                messageStream.CopyFrom(stream);

                if (!networkingManager.IsServer)
                {
                    if (NetworkingManager.LogLevel <= LogLevel.Error) NetworkLog.LogWarning("Can not send named messages to multiple users as a client");
                    return;
                }

                networkingManager.MessageSender.Send(MLAPIConstants.MLAPI_NAMED_MESSAGE, channel, clientIds, messageStream, security);
            }
        }
        #endregion
    }
}
