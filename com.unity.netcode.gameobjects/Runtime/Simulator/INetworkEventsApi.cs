﻿using System;
using System.Threading.Tasks;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Unity.Netcode
{
    public interface INetworkEventsApi
    {
        bool IsDisabledBySimulator { get; }

        void TriggerDisconnect();

        void TriggerReconnect();

        void TriggerLagSpike(TimeSpan duration);

        void ChangeNetworkType(INetworkSimulatorConfiguration newNetworkSimulatorConfiguration);
    }

    public class NoOpNetworkEventsApi : INetworkEventsApi
    {
        public bool IsDisabledBySimulator { get; private set; }

        public void TriggerDisconnect()
        {
            IsDisabledBySimulator = true;

            Debug.Log("Triggering disconnect.");
        }

        public void TriggerReconnect()
        {
            IsDisabledBySimulator = false;

            Debug.Log("Triggering reconnect.");
        }

        public void TriggerLagSpike(TimeSpan duration)
        {
            Debug.Log($"Triggering lag spike for {duration.Milliseconds} ms.");
        }

        public void ChangeNetworkType(INetworkSimulatorConfiguration newNetworkSimulatorConfiguration)
        {
            Debug.Log($"Changing network type to {newNetworkSimulatorConfiguration.Name}.");
        }
    }

    public class NetworkEventsApi : INetworkEventsApi
    {
        readonly NetworkSimulator m_NetworkSimulator;
        readonly UnityTransport m_UnityTransport;

        public NetworkEventsApi(NetworkSimulator networkSimulator, UnityTransport unityTransport)
        {
            m_NetworkSimulator = networkSimulator;
            m_UnityTransport = unityTransport;
        }

        public bool IsDisabledBySimulator => m_UnityTransport.IsDisabledBySimulator;

        public void TriggerDisconnect()
        {
            m_UnityTransport.TriggerDisconnect();
        }

        public void TriggerReconnect()
        {
            m_UnityTransport.TriggerReconnect();
        }

        public void TriggerLagSpike(TimeSpan duration)
        {
            Task.Run(async () =>
            {
                TriggerDisconnect();

                await Task.Delay(duration);

                TriggerReconnect();
            });
        }

        public void ChangeNetworkType(INetworkSimulatorConfiguration newNetworkSimulatorConfiguration)
        {
            m_NetworkSimulator.SimulatorConfiguration = newNetworkSimulatorConfiguration;
        }
    }
}
