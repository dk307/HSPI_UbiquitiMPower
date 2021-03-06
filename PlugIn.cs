using HomeSeerAPI;
using Hspi.Connector;
using Hspi.DeviceData;
using Hspi.Exceptions;
using NullGuard;
using Scheduler.Classes;
using System;
using System.Collections.Generic;
using System.Text;
using static System.FormattableString;

namespace Hspi
{
    /// <summary>
    /// Plugin class for Ubiquiti mPower
    /// </summary>
    /// <seealso cref="Hspi.HspiBase" />
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class Plugin : HspiBase
    {
        public Plugin()
            : base(PluginData.PlugInName, supportConfigDevice: true)
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

                LogInfo("Plugin Started");
            }
            catch (Exception ex)
            {
                result = Invariant($"Failed to initialize PlugIn With {ExceptionHelper.GetFullMessage(ex)}");
                LogError(result);
            }

            return result;
        }

        private void RestartMPowerConnections()
        {
            lock (connectorManagerLock)
            {
                // This returns a new copy every time
                var currentDevices = pluginConfig.Devices;

                // Update changed or new
                foreach (var device in pluginConfig.Devices)
                {
                    if (connectorManager.TryGetValue(device.Key, out var oldConnector))
                    {
                        if (!device.Value.Equals(oldConnector.Device))
                        {
                            oldConnector.Dispose();
                            connectorManager[device.Key] = new MPowerConnectorManager(HS, device.Value, ShutdownCancellationToken);
                        }
                    }
                    else
                    {
                        connectorManager.Add(device.Key, new MPowerConnectorManager(HS, device.Value, ShutdownCancellationToken));
                    }
                }

                // Remove deleted
                List<string> removalList = new List<string>();
                foreach (var deviceKeyPair in connectorManager)
                {
                    if (!currentDevices.ContainsKey(deviceKeyPair.Key))
                    {
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

        public override void LogDebug(string message)
        {
            if ((pluginConfig != null) && pluginConfig.DebugLogging)
            {
                base.LogDebug(message);
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

        public override void SetIOMulti(List<CAPI.CAPIControl> colSend)
        {
            foreach (var control in colSend)
            {
                try
                {
                    int refId = control.Ref;
                    DeviceClass deviceClass = (DeviceClass)HS.GetDeviceByRef(refId);

                    var deviceIdentifier = DeviceIdentifier.Identify(deviceClass);

                    lock (connectorManagerLock)
                    {
                        if (connectorManager.TryGetValue(deviceIdentifier.DeviceId, out var connector))
                        {
                            connector.HandleCommand(deviceIdentifier, control.ControlValue, control.ControlUse).Wait();
                        }
                        else
                        {
                            throw new HspiException(Invariant($"{refId} Device Not Found for processing."));
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogError(Invariant($"Failed With {ExceptionHelper.GetFullMessage(ex)}"));
                }
            }
        }

        public override string ConfigDevice(int deviceId, [AllowNull] string user, int userRights, bool newDevice)
        {
            if (newDevice)
            {
                return string.Empty;
            }

            try
            {
                DeviceClass deviceClass = (DeviceClass)HS.GetDeviceByRef(deviceId);
                var deviceIdentifier = DeviceIdentifier.Identify(deviceClass);

                if (deviceIdentifier != null)
                {
                    foreach (var device in pluginConfig.Devices)
                    {
                        if (device.Key == deviceIdentifier.DeviceId)
                        {
                            StringBuilder stb = new StringBuilder();

                            stb.Append(@"<table style='width:100%;border-spacing:0px;'");
                            stb.Append("<tr height='5'><td style='width:25%'></td><td style='width:20%'></td><td style='width:55%'></td></tr>");
                            stb.Append(Invariant($"<tr><td class='tablecell'>Name:</td><td class='tablecell' colspan=2>{ConfigPage.HtmlEncode(device.Value.Name)}</td></tr>"));
                            stb.Append(Invariant($"<tr><td class='tablecell'>Device IP:</td><td class='tablecell' colspan=2>{ConfigPage.HtmlEncode(device.Value.DeviceIP)}</td></tr>"));
                            stb.Append(Invariant($"<tr><td class='tablecell'>Port:</td><td class='tablecell' colspan=2>{ConfigPage.HtmlEncode(deviceIdentifier.Port)}</td></tr>"));
                            stb.Append(Invariant($"<tr><td class='tablecell'>Type:</td><td class='tablecell' colspan=2>{EnumHelper.GetDescription(deviceIdentifier.DeviceType)}</td></tr>"));
                            stb.Append(Invariant($"</td><td></td></tr>"));
                            stb.Append("<tr height='5'><td colspan=3></td></tr>");
                            stb.Append(@" </table>");

                            return stb.ToString();
                        }
                    }
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                LogError(Invariant($"ConfigDevice for {deviceId} With {ex.Message}"));
                return string.Empty;
            }
        }

        private void RegisterConfigPage()
        {
            string link = ConfigPage.Name;
            HS.RegisterPage(link, Name, string.Empty);

            HomeSeerAPI.WebPageDesc wpd = new HomeSeerAPI.WebPageDesc()
            {
                plugInName = Name,
                link = link,
                linktext = "Configuration",
                page_title = Invariant($"{PluginData.PlugInName} Configuration"),
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
                    deviceKeyPair.Value.Dispose();
                }

                connectorManager.Clear();

                disposedValue = true;
            }

            base.Dispose(disposing);
        }

        private readonly object connectorManagerLock = new object();
        private readonly Dictionary<string, MPowerConnectorManager> connectorManager = new Dictionary<string, MPowerConnectorManager>();
        private ConfigPage configPage;
        private PluginConfig pluginConfig;
        private bool disposedValue = false;
    }
}