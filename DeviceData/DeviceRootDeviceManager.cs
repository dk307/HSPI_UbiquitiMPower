using HomeSeerAPI;
using Hspi.Connector;
using Hspi.Connector.Model;
using Hspi.Exceptions;
using NullGuard;
using Scheduler.Classes;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hspi.DeviceData
{
    using System.Diagnostics;
    using static System.FormattableString;

    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class DeviceRootDeviceManager
    {
        public DeviceRootDeviceManager(string deviceName, string rootDeviceId, IHSApplication HS)
        {
            this.deviceName = deviceName;
            this.HS = HS;
            this.rootDeviceId = rootDeviceId;
            GetCurrentDevices();
        }

        public void ProcessSensorData(MPowerDevice device, SensorData sensorData)
        {
            if ((device.EnabledTypes.Count == 0) || (device.EnabledPorts.Count == 0))
            {
                return;
            }

            UpdateSensorValue(device, sensorData.Label, sensorData.Port, DeviceType.Current, sensorData.Current);
            UpdateSensorValue(device, sensorData.Label, sensorData.Port, DeviceType.Energy, sensorData.Energy);
            UpdateSensorValue(device, sensorData.Label, sensorData.Port, DeviceType.Output, sensorData.Output);
            UpdateSensorValue(device, sensorData.Label, sensorData.Port, DeviceType.Power, sensorData.Power);
            UpdateSensorValue(device, sensorData.Label, sensorData.Port, DeviceType.PowerFactor, sensorData.PowerFactor);
            UpdateSensorValue(device, sensorData.Label, sensorData.Port, DeviceType.Voltage, sensorData.Voltage);
        }

        private void UpdateSensorValue(MPowerDevice device, [AllowNull]string label, int port, DeviceType deviceType, double value)
        {
            if (!device.EnabledTypes.Contains(deviceType))
            {
                return;
            }

            if (!device.Resolution.TryGetValue(deviceType, out var resolutionValue))
            {
                resolutionValue = PluginConfig.GetDefaultResolution(deviceType);
            }

            var deviceIdentifier = new DeviceIdentifier(rootDeviceId, port, deviceType);

            string address = deviceIdentifier.Address;
            if (!currentChildDevices.ContainsKey(address))
            {
                CreateDevice(label, deviceIdentifier);
            }

            var childDevice = currentChildDevices[address];

            value /= childDevice.Denominator;

            double roundedValue = Math.Round(value / resolutionValue, 0, MidpointRounding.AwayFromZero) * resolutionValue;
            childDevice.Update(HS, roundedValue);
        }

        public async Task HandleCommand(DeviceIdentifier deviceIdentifier, CancellationToken token,
                                        MPowerConnector connector, double value, ePairControlUse control)
        {
            if (deviceIdentifier.DeviceId != rootDeviceId)
            {
                throw new ArgumentException("Invalid Device Identifier");
            }

            if (!currentChildDevices.TryGetValue(deviceIdentifier.Address, out var deviceData))
            {
                throw new HspiException(Invariant($"{deviceIdentifier.Address} Not Found."));
            }

            await deviceData.HandleCommand(connector, token, value, control).ConfigureAwait(false);
        }

        private void GetCurrentDevices()
        {
            var deviceEnumerator = HS.GetDeviceEnumerator() as clsDeviceEnumeration;

            if (deviceEnumerator == null)
            {
                throw new HspiException(Invariant($"{PluginData.PlugInName} failed to get a device enumerator from HomeSeer."));
            }

            string parentAddress = DeviceIdentifier.CreateRootAddress(rootDeviceId);
            do
            {
                DeviceClass device = deviceEnumerator.GetNext();
                if ((device != null) &&
                    (device.get_Interface(HS) != null) &&
                    (device.get_Interface(HS).Trim() == PluginData.PlugInName))
                {
                    string address = device.get_Address(HS);
                    if (address == parentAddress)
                    {
                        parentRefId = device.get_Ref(HS);
                    }
                    else if (address.StartsWith(parentAddress, StringComparison.Ordinal))
                    {
                        DeviceData childDeviceData = GetDeviceData(device);
                        if (childDeviceData != null)
                        {
                            currentChildDevices.Add(address, childDeviceData);
                        }
                    }
                }
            } while (!deviceEnumerator.Finished);
        }

        private void CreateDevice([AllowNull]string label, DeviceIdentifier deviceIdentifier)
        {
            if (!parentRefId.HasValue)
            {
                string parentAddress = deviceIdentifier.RootDeviceAddress;
                var parentHSDevice = CreateDevice(null, deviceName, parentAddress, new RootDeviceData());
                parentRefId = parentHSDevice.get_Ref(HS);
            }

            string address = deviceIdentifier.Address;
            var childDevice = GetDevice(deviceIdentifier.Port, deviceIdentifier.DeviceType);
            string childDeviceName = Invariant($"{ label ?? Invariant($"{deviceName} Port {deviceIdentifier.Port}")} {EnumHelper.GetDescription(childDevice.DeviceType)}");
            var childHSDevice = CreateDevice(parentRefId.Value, childDeviceName, address, childDevice);
            childDevice.RefId = childHSDevice.get_Ref(HS);
            currentChildDevices[address] = childDevice;
        }

        private static DeviceData GetDevice(int port, DeviceType deviceType)
        {
            switch (deviceType)
            {
                case DeviceType.Current:
                    return new CurrentDeviceData(port);

                case DeviceType.Energy:
                    return new EnergyDeviceData(port);

                case DeviceType.Voltage:
                    return new VoltsDeviceData(port);

                case DeviceType.Power:
                    return new PowerDeviceData(port);

                case DeviceType.Output:
                    return new SwitchDeviceData(port);

                default:
                    return new NumberDeviceData(port, deviceType);
            }
        }

        private DeviceData GetDeviceData(DeviceClass hsDevice)
        {
            var id = DeviceIdentifier.Identify(hsDevice);
            if (id == null)
            {
                return null;
            }

            var device = GetDevice(id.Port, id.DeviceType);
            device.RefId = hsDevice.get_Ref(HS);
            return device;
        }

        /// <summary>
        /// Creates the HS device.
        /// </summary>
        /// <param name="optionalParentRefId">The optional parent reference identifier.</param>
        /// <param name="name">The name of device</param>
        /// <param name="deviceAddress">The device address.</param>
        /// <param name="deviceData">The device data.</param>
        /// <returns>
        /// New Device
        /// </returns>
        private DeviceClass CreateDevice(int? optionalParentRefId, string name, string deviceAddress, DeviceDataBase deviceData)
        {
            Trace.TraceInformation(Invariant($"Creating Device with Address:{deviceAddress}"));

            DeviceClass device = null;
            int refId = HS.NewDeviceRef(name);
            if (refId > 0)
            {
                device = (DeviceClass)HS.GetDeviceByRef(refId);
                string address = deviceAddress;
                device.set_Address(HS, address);
                device.set_Device_Type_String(HS, deviceData.HSDeviceTypeString);
                var deviceType = new DeviceTypeInfo_m.DeviceTypeInfo();
                deviceType.Device_API = deviceData.DeviceAPI;
                deviceType.Device_Type = deviceData.HSDeviceType;

                device.set_DeviceType_Set(HS, deviceType);
                device.set_Interface(HS, PluginData.PlugInName);
                device.set_InterfaceInstance(HS, string.Empty);
                device.set_Last_Change(HS, DateTime.Now);
                device.set_Location(HS, PluginData.PlugInName);

                device.MISC_Set(HS, Enums.dvMISC.SHOW_VALUES);
                if (deviceData.StatusDevice)
                {
                    device.MISC_Set(HS, Enums.dvMISC.STATUS_ONLY);
                    device.MISC_Clear(HS, Enums.dvMISC.AUTO_VOICE_COMMAND);
                    device.MISC_Clear(HS, Enums.dvMISC.SET_DOES_NOT_CHANGE_LAST_CHANGE);
                    device.set_Status_Support(HS, true);
                }
                else
                {
                    device.MISC_Set(HS, Enums.dvMISC.SET_DOES_NOT_CHANGE_LAST_CHANGE);
                    device.MISC_Set(HS, Enums.dvMISC.AUTO_VOICE_COMMAND);
                    device.set_Status_Support(HS, false);
                }

                var pairs = deviceData.StatusPairs;
                foreach (var pair in pairs)
                {
                    HS.DeviceVSP_AddPair(refId, pair);
                }

                var gPairs = deviceData.GraphicsPairs;
                foreach (var gpair in gPairs)
                {
                    HS.DeviceVGP_AddPair(refId, gpair);
                }

                DeviceClass parent = null;
                if (optionalParentRefId.HasValue)
                {
                    parent = (DeviceClass)HS.GetDeviceByRef(optionalParentRefId.Value);
                }

                if (parent != null)
                {
                    parent.set_Relationship(HS, Enums.eRelationship.Parent_Root);
                    device.set_Relationship(HS, Enums.eRelationship.Child);
                    device.AssociatedDevice_Add(HS, parent.get_Ref(HS));
                    parent.AssociatedDevice_Add(HS, device.get_Ref(HS));
                }

                deviceData.SetInitialData(HS, refId);
            }

            return device;
        }

        private readonly string rootDeviceId;
        private readonly string deviceName;
        private readonly IHSApplication HS;
        private int? parentRefId = null;
        private readonly Dictionary<string, DeviceData> currentChildDevices = new Dictionary<string, DeviceData>();
    };
}