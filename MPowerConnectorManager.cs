using HomeSeerAPI;
using Hspi.Connector.Model;
using Hspi.DeviceData;
using Hspi.Exceptions;
using Nito.AsyncEx;
using NullGuard;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Hspi.Connector
{
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal sealed class MPowerConnectorManager : IDisposable
    {
        public MPowerConnectorManager(IHSApplication HS, MPowerDevice device, CancellationToken shutdownToken)
        {
            this.HS = HS;
            Device = device;
            rootDeviceData = new DeviceRootDeviceManager(device.Name, device.Id, this.HS);

            combinedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
            Task.Factory.StartNew(ManageConnection,
                                                Token,
                                                TaskCreationOptions.LongRunning,
                                                TaskScheduler.Current);
            Task.Factory.StartNew(ProcessDeviceUpdates,
                                                Token,
                                                TaskCreationOptions.LongRunning,
                                                TaskScheduler.Current);
        }

        public async Task HandleCommand(DeviceIdentifier deviceIdentifier, double value, ePairControlUse control)
        {
            if (deviceIdentifier.DeviceId != Device.Id)
            {
                throw new ArgumentException("Invalid Device Identifier");
            }

            // This function runs in separate thread than main run

            MPowerConnector connectorCopy;
            using (var sync = await rootDeviceDataLock.LockAsync(Token).ConfigureAwait(false))
            {
                connectorCopy = connector;
                if (connectorCopy == null)
                {
                    throw new HspiException(Invariant($"No connection to Device for {Device.DeviceIP}"));
                }
                await rootDeviceData.HandleCommand(deviceIdentifier, Token, connectorCopy, value, control).ConfigureAwait(false);
            }

            await connectorCopy.UpdateAllSensorData(Token).ConfigureAwait(false);
        }

        private async Task ManageConnection()
        {
            try
            {
                while (!Token.IsCancellationRequested)
                {
                    // connect if needed
                    await Connect().ConfigureAwait(false);

                    // sleep for 30 seconds or Close
                    await SleepForIntervalOrClose().ConfigureAwait(false);

                    // Check for connection health
                    await CheckConnection().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                if (ex.IsCancelException())
                {
                    throw;
                }

                Trace.TraceError(Invariant($"ManageConnection for {Device.DeviceIP} failed with {ex.GetFullMessage()}. Restarting ..."));
            }
        }

        private async Task ProcessDeviceUpdates()
        {
            try
            {
                while (!Token.IsCancellationRequested)
                {
                    var sensorData = await changedPorts.DequeueAsync(Token).ConfigureAwait(false);
                    using (var sync = await rootDeviceDataLock.LockAsync(Token).ConfigureAwait(false))
                    {
                        try
                        {
                            if (Device.EnabledPorts.Contains(sensorData.Port))
                            {
                                rootDeviceData.ProcessSensorData(Device, sensorData);
                            }
                        }
                        catch (Exception ex)
                        {
                            if (ex.IsCancelException())
                            {
                                throw;
                            }

                            Trace.TraceWarning(Invariant($"Failed to update Sensor Data for Port {sensorData.Port} on {Device.DeviceIP} with {ex.GetFullMessage()}"));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex.IsCancelException())
                {
                    throw;
                }

                Trace.TraceError(Invariant($"ProcessDeviceUpdates for {Device.DeviceIP} failed with {ex.GetFullMessage()}. Restarting ..."));
            }
        }

        private async Task CheckConnection()
        {
            if (connector != null)
            {
                bool destroyConnection = ShouldDestroyConnection();

                if (destroyConnection)
                {
                    await DestroyConnection().ConfigureAwait(false);
                }
                else
                {
                    try
                    {
                        await connector.UpdateAllSensorData(Token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        if (ex.IsCancelException())
                        {
                            throw;
                        }

                        Trace.TraceWarning(Invariant($"Failed to Get Full Sensor Data from {Device.DeviceIP} with {ex.GetFullMessage()}"));
                        await DestroyConnection().ConfigureAwait(false);
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
                Trace.TraceWarning(Invariant($"WebSocket Disconnected to {Device.DeviceIP}. Total Errors:{connector.TotalErrors}. Reconnecting..."));
                return true;
            }

            int maxErrors = 10;
            if (connector.TotalErrors > maxErrors)
            {
                Trace.TraceWarning(Invariant($"Too many errors in websocket connection to {Device.DeviceIP}. Total Errors:{connector.TotalErrors}. Reconnecting..."));
                return true;
            }

            TimeSpan maxUpdateTime = TimeSpan.FromSeconds(240);
            TimeSpan lastUpdate = connector.TimeSinceLastUpdate;
            if (lastUpdate > maxUpdateTime)
            {
                Trace.TraceWarning(Invariant($"Did not receive update from {Device.DeviceIP} for {lastUpdate.Seconds} seconds. Reconnecting..."));
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
            TimeSpan delay = TimeSpan.FromSeconds(30);
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
                    newConnector = new MPowerConnector(Device.DeviceIP);
                    newConnector.PortsChanged += Connector_PortsChanged;

                    await newConnector.Connect(Device.Username, Device.Password, Token).ConfigureAwait(false);
                    connector = newConnector;
                }
                catch (Exception ex)
                {
                    if (ex.IsCancelException())
                    {
                        throw;
                    }
                    Trace.TraceWarning(Invariant($"Failed to Connect to {Device.DeviceIP} with {ex.Message}"));
                    newConnector.PortsChanged -= Connector_PortsChanged;

                    newConnector?.Dispose();
                }
            }
        }

        private void Connector_PortsChanged(object sender, IList<SensorData> portsChanged)
        {
            if (sender == connector)
            {
                try
                {
                    foreach (var data in portsChanged)
                    {
                        changedPorts.Enqueue(data, Token);
                    }
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        #region IDisposable Support

        private bool disposedValue = false; // To detect redundant calls

        public MPowerDevice Device { get; }

        private void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    combinedCancellationSource.Cancel();
                    DisposeConnector();
                    combinedCancellationSource.Dispose();
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
        private readonly IHSApplication HS;
        private readonly AsyncProducerConsumerQueue<SensorData> changedPorts = new AsyncProducerConsumerQueue<SensorData>();
        private readonly DeviceRootDeviceManager rootDeviceData;
        private readonly AsyncLock rootDeviceDataLock = new AsyncLock();
    }
}