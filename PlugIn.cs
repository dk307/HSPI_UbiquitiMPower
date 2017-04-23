using Hspi.Connector;
using NullGuard;
using System;
using System.Collections.Generic;

namespace Hspi
{
    using static System.FormattableString;

    /// <summary>
    /// Plugin class for Weather Underground
    /// </summary>
    /// <seealso cref="Hspi.HspiBase" />
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class PlugIn : HspiBase
    {
        public PlugIn()
            : base(PluginData.PlugInName)
        {
        }

        public override string InitIO(string port)
        {
            string result = string.Empty;
            try
            {
                pluginConfig = new PluginConfig(HS);
                configPage = new ConfigPage(HS, pluginConfig);
                LogInfo("Starting Plugin");
#if DEBUG
                pluginConfig.DebugLogging = true;
#endif
                pluginConfig.ConfigChanged += PluginConfig_ConfigChanged;

                RegisterConfigPage();

                RestartMPowerConnections();

                DebugLog("Plugin Started");
            }
            catch (Exception ex)
            {
                result = Invariant($"Failed to initialize PlugIn With {ex.Message}");
                LogError(result);
            }

            return result;
        }

        private void RestartMPowerConnections()
        {
            lock (connectorManagerLock)
            {
                var currentDevices = pluginConfig.Devices;

                // Update changed or new
                foreach (var device in pluginConfig.Devices)
                {
                    if (connectorManager.TryGetValue(device.Key, out var oldConnector))
                    {
                        if (!device.Value.Equals(oldConnector.Device))
                        {
                            oldConnector.Cancel();
                            oldConnector.Dispose();
                            connectorManager[device.Key] = new mPowerConnectorManager(HS, device.Value, this as ILogger, ShutdownCancellationToken);
                        }
                    }
                    else
                    {
                        connectorManager.Add(device.Key, new mPowerConnectorManager(HS, device.Value, this as ILogger, ShutdownCancellationToken));
                    }
                }

                // Remove deleted
                List<string> removalList = new List<string>();
                foreach (var deviceKeyPair in connectorManager)
                {
                    if (!currentDevices.ContainsKey(deviceKeyPair.Key))
                    {
                        deviceKeyPair.Value.Cancel();
                        deviceKeyPair.Value.Dispose();
                        removalList.Add(deviceKeyPair.Key);
                    }
                }

                foreach (var key in removalList)
                {
                    connectorManager.Remove(key);
                }
            }
        }

        private void PluginConfig_ConfigChanged(object sender, EventArgs e)
        {
            RestartMPowerConnections();
        }

        public override void DebugLog(string message)
        {
            if (pluginConfig.DebugLogging)
            {
                base.DebugLog(message);
            }
        }

        public override string GetPagePlugin(string page, [AllowNull]string user, int userRights, [AllowNull]string queryString)
        {
            if (page == ConfigPage.Name)
            {
                return configPage.GetWebPage(queryString);
            }

            return string.Empty;
        }

        public override string PostBackProc(string page, string data, [AllowNull]string user, int userRights)
        {
            if (page == ConfigPage.Name)
            {
                return configPage.PostBackProc(data, user, userRights);
            }

            return string.Empty;
        }

        private void RegisterConfigPage()
        {
            string link = ConfigPage.Name;
            HS.RegisterPage(link, Name, string.Empty);

            HomeSeerAPI.WebPageDesc wpd = new HomeSeerAPI.WebPageDesc()
            {
                plugInName = Name,
                link = link,
                linktext = link,
                page_title = Invariant($"{Name} Config"),
            };
            Callback.RegisterConfigLink(wpd);
            Callback.RegisterLink(wpd);
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (pluginConfig != null)
                {
                    pluginConfig.ConfigChanged -= PluginConfig_ConfigChanged;
                }
                if (configPage != null)
                {
                    configPage.Dispose();
                }

                if (pluginConfig != null)
                {
                    pluginConfig.Dispose();
                }

                foreach (var deviceKeyPair in connectorManager)
                {
                    deviceKeyPair.Value.Cancel();
                    deviceKeyPair.Value.Dispose();
                }

                connectorManager.Clear();

                disposedValue = true;
            }

            base.Dispose(disposing);
        }

        private readonly object connectorManagerLock = new object();
        private readonly IDictionary<string, mPowerConnectorManager> connectorManager = new Dictionary<string, mPowerConnectorManager>();
        private ConfigPage configPage;
        private PluginConfig pluginConfig;
        private const int ActionRefreshTANumber = 1;
        private bool disposedValue = false;
    }
}