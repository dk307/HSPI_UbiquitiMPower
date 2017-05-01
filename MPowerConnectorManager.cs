using HomeSeerAPI;
using Hspi.Connector.Model;
using Hspi.DeviceData;
using Hspi.Exceptions;
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
    internal class MPowerConnectorManager : IDisposable
    {
        public MPowerConnectorManager(IHSApplication HS, MPowerDevice device, ILogger logger, CancellationToken shutdownToken)
        {
            this.HS = HS;
            this.logger = logger;
            this.Device = device;
            rootDeviceData = new DeviceRootDeviceManager(device.Name, device.Id, this.HS, logger);

            combinedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken, instanceCancellationSource.Token);
            runTask = Task.Factory.StartNew(ManageConnection, Token);
            processTask = Task.Factory.StartNew(ProcessDeviceUpdates, Token, TaskCreationOptions.LongRunning, TaskScheduler.Current);
        }

        public void Cancel()
        {
            instanceCancellationSource.Cancel();
            runTask.Wait();
            processTask.Wait();
        }

        public async Task HandleCommand(DeviceIdentifier deviceIdentifier, double value, ePairControlUse control)
        {
            if (deviceIdentifier.DeviceId != Device.Id)
            {
                throw new ArgumentException("Invalid Device Identifier");
            }

            // This function runs in separate thread than main run

            MPowerConnector connectorCopy;
            await rootDeviceDataLock.WaitAsync(Token);
            try
            {
                connectorCopy = connector;
                if (connectorCopy == null)
                {
                    throw new HspiException(Invariant($"No connection to Device for {Device.DeviceIP}"));
                }
                await rootDeviceData.HandleCommand(deviceIdentifier, Token, connectorCopy, value, control);
            }
            finally
            {
                rootDeviceDataLock.Release();
            }

            await connectorCopy.UpdateAllSensorData(Token);
        }

        private async Task ManageConnection()
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

        private async Task ProcessDeviceUpdates()
        {
            try
            {
                while (!Token.IsCancellationRequested)
                {
                    if (changedPorts.TryTake(out var sensorData, -1, Token))
                    {
                        await rootDeviceDataLock.WaitAsync(Token);
                        try
                        {
                            if (Device.EnabledPorts.Contains(sensorData.Port))
                            {
                                rootDeviceData.ProcessSensorData(sensorData, Device.EnabledTypesAndResolution);
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(Invariant($"Failed to update Sensor Data for Port {sensorData.Port} on {Device.DeviceIP} with {ex.Message}"));
                        }
                        finally
                        {
                            rootDeviceDataLock.Release();
                        }
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
            if (connector == null)
            {
                MPowerConnector newConnector = null;
                try
                {
                    newConnector = new MPowerConnector(Device.DeviceIP, logger);
                    newConnector.PortsChanged += Connector_PortsChanged;

                    await newConnector.Connect(Device.Username, Device.Password, Token).ConfigureAwait(false);
                    connector = newConnector;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(Invariant($"Failed to Connect to {Device.DeviceIP} with {ex.Message}"));
                    newConnector?.Dispose();
                }
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
                    combinedCancellationSource.Dispose();

                    runTask.Dispose();
                    processTask.Dispose();
                    changedPorts.Dispose();
                    DisposeConnector();
                    rootDeviceDataLock.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        #endregion IDisposable Support

        private CancellationToken Token => combinedCancellationSource.Token;
        private volatile MPowerConnector connector;
        private readonly CancellationTokenSource combinedCancellationSource;
        private readonly CancellationTokenSource instanceCancellationSource = new CancellationTokenSource();
        private readonly ILogger logger;
        private readonly IHSApplication HS;
        private readonly BlockingCollection<SensorData> changedPorts = new BlockingCollection<SensorData>();
        private readonly DeviceRootDeviceManager rootDeviceData;
        private readonly Task runTask;
        private readonly Task processTask;
        private readonly SemaphoreSlim rootDeviceDataLock = new SemaphoreSlim(1);
    }
}