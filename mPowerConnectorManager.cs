using HomeSeerAPI;
using Hspi.Connector.Model;
using Hspi.DeviceData;
using NullGuard;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hspi.Connector
{
    using static System.FormattableString;

    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class mPowerConnectorManager : IDisposable
    {
        public mPowerConnectorManager(IHSApplication HS, MPowerDevice device, ILogger logger, CancellationToken shutdownToken)
        {
            this.HS = HS;
            this.logger = logger;
            this.Device = device;
            rootDeviceData = new DeviceRootDeviceManager(device.Id, this.HS, logger);

            combinedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken, instanceCancellationSource.Token);
            runTask = Task.Factory.StartNew(Run, Token);
            processTask = Task.Factory.StartNew(ProcessUpdates, Token, TaskCreationOptions.LongRunning, TaskScheduler.Current);
        }

        public void Cancel()
        {
            instanceCancellationSource.Cancel();
            runTask.Wait();
            processTask.Wait();
        }

        private async Task Run()
        {
            while (!Token.IsCancellationRequested)
            {
                // connect if needed
                await Connect().ConfigureAwait(false);

                // sleep for 120 seconds or Close
                await SleepForIntervalOrClose().ConfigureAwait(false);

                // Check for connection health
                await CheckConnection().ConfigureAwait(false);
            }
        }

        private void ProcessUpdates()
        {
            try
            {
                while (!Token.IsCancellationRequested)
                {
                    if (changedPorts.TryTake(out var sensorData, -1, Token))
                    {
                        rootDeviceData.ProcessSensorData(sensorData, Device.EnabledTypes);
                    }
                }
            }
            catch (OperationCanceledException)
            { }
        }

        private async Task CheckConnection()
        {
            if (connector != null)
            {
                bool destroyConnection = ShouldDestroyConnection();

                if (destroyConnection)
                {
                    await DestroyConnection();
                }
                else
                {
                    try
                    {
                        await connector.UpdateAllSensorData(Token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(Invariant($"Failed to Get Full Sensor Data from {Device.DeviceIP} with {ex.Message}"));
                        await DestroyConnection();
                    }
                }
            }
        }

        private bool ShouldDestroyConnection()
        {
            if (connector.ConnectionClosed.IsCancellationRequested)
            {
                return true;
            }

            if (!connector.Connected)
            {
                logger.LogWarning(Invariant($"WebSocket Disconnected to {Device.DeviceIP}. Total Errors:{connector.TotalErrors}. Reconnecting..."));
                return true;
            }

            int maxErrors = 10;
            if (connector.TotalErrors > maxErrors)
            {
                logger.LogWarning(Invariant($"Too many errors in websocket connection to {Device.DeviceIP}. Total Errors:{connector.TotalErrors}. Reconnecting..."));
                return true;
            }

            TimeSpan maxUpdateTime = TimeSpan.FromSeconds(240);
            TimeSpan lastUpdate = connector.TimeSinceLastUpdate;
            if (lastUpdate > maxUpdateTime)
            {
                logger.LogWarning(Invariant($"Did not receive update from {Device.DeviceIP} for {lastUpdate.Seconds} seconds. Reconnecting..."));
                return true;
            }

            return false;
        }

        private async Task DestroyConnection()
        {
            try
            {
                await connector.LogOut(Token).ConfigureAwait(false);
            }
            catch (Exception) { }
            DisposeConnector();
        }

        private void DisposeConnector()
        {
            if (connector != null)
            {
                connector.PortsChanged -= Connector_PortsChanged;
                connector.Dispose();
                connector = null;
            }
        }

        private async Task SleepForIntervalOrClose()
        {
            TimeSpan delay = TimeSpan.FromSeconds(120);
            if (connector != null)
            {
                using (var combinedSource = CancellationTokenSource.CreateLinkedTokenSource(Token, connector.ConnectionClosed))
                {
                    await Task.Delay(delay, combinedSource.Token).ConfigureAwait(false);
                }
            }
            else
            {
                await Task.Delay(delay, Token).ConfigureAwait(false);
            }

            Token.ThrowIfCancellationRequested();
        }

        private async Task Connect()
        {
            try
            {
                if (connector == null)
                {
                    connector = new mPowerConnector(Device.DeviceIP, logger);
                    connector.PortsChanged += Connector_PortsChanged;

                    await connector.Connect(Device.Username, Device.Password, Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(Invariant($"Failed to Connect to {Device.DeviceIP} with {ex.Message}"));
                DisposeConnector();
            }
        }

        private void Connector_PortsChanged(object sender, IList<SensorData> portsChanged)
        {
            try
            {
                foreach (var data in portsChanged)
                {
                    changedPorts.Add(data, Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private CancellationToken Token => combinedCancellationSource.Token;

        private mPowerConnector connector;
        private readonly CancellationTokenSource combinedCancellationSource;
        private readonly CancellationTokenSource instanceCancellationSource = new CancellationTokenSource();
        private readonly ILogger logger;
        private readonly IHSApplication HS;
        private readonly BlockingCollection<SensorData> changedPorts = new BlockingCollection<SensorData>();
        private readonly DeviceRootDeviceManager rootDeviceData;
        private readonly Task runTask;
        private readonly Task processTask;

        #region IDisposable Support

        private bool disposedValue = false; // To detect redundant calls
        public MPowerDevice Device { get; }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    instanceCancellationSource.Cancel();
                    instanceCancellationSource.Dispose();

                    runTask.Dispose();
                    processTask.Dispose();
                    changedPorts.Dispose();
                    DisposeConnector();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        #endregion IDisposable Support
    }
}