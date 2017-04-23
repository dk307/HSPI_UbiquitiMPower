using HomeSeerAPI;
using NullGuard;
using Scheduler.Classes;
using System;
using System.Collections.Generic;
using Hspi.Exceptions;
using Hspi.Connector;
using Hspi.Connector.Model;

namespace Hspi.DeviceData
{
    using static System.FormattableString;

    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class DeviceRootDeviceManager
    {
        private const char AddressSeparator = '.';
        private readonly string deviceId;
        private readonly IHSApplication HS;
        private int? parentRefId = null;
        private readonly IDictionary<string, DeviceData> currentChildDevices = new Dictionary<string, DeviceData>();
        private readonly ILogger logger;

        public DeviceRootDeviceManager(string deviceId, IHSApplication HS, ILogger logger)
        {
            this.logger = logger;
            this.HS = HS;
            this.deviceId = deviceId;
            GetCurrentDevices();
        }

        public void ProcessSensorData(SensorData sensorData, ISet<DeviceType> updateTypes)
        {
            if (updateTypes.Count == 0)
            {
                return;
            }

            UpdateSensorValue(updateTypes, sensorData.Label, sensorData.Port, DeviceType.Current, sensorData.Current);
            UpdateSensorValue(updateTypes, sensorData.Label, sensorData.Port, DeviceType.Energy, sensorData.Energy);
            UpdateSensorValue(updateTypes, sensorData.Label, sensorData.Port, DeviceType.Output, sensorData.Output);
            UpdateSensorValue(updateTypes, sensorData.Label, sensorData.Port, DeviceType.Power, sensorData.Power);
            UpdateSensorValue(updateTypes, sensorData.Label, sensorData.Port, DeviceType.PowerFactor, sensorData.PowerFactor);
            UpdateSensorValue(updateTypes, sensorData.Label, sensorData.Port, DeviceType.Voltage, sensorData.Voltage);
        }

        private void UpdateSensorValue(ISet<DeviceType> updateTypes, [AllowNull]string label, int port, DeviceType deviceType, double value)
        {
            if (!updateTypes.Contains(deviceType))
            {
                return;
            }

            string address = DeviceRootDeviceManager.CreateAddress(deviceId, port, deviceType);
            if (!currentChildDevices.ContainsKey(address))
            {
                CreateDevice(label, port, deviceType);
            }

            currentChildDevices[address].Update(HS, value);
        }

        private static string CreateRootDeviceAddress(string deviceId)
        {
            return Invariant($"{PluginData.PlugInName}{AddressSeparator}{deviceId}");
        }

        private static string CreateAddress(string deviceId, int port, DeviceType type)
        {
            return Invariant($"{CreateRootDeviceAddress(deviceId)}{AddressSeparator}{port}{AddressSeparator}{type}");
        }

        private void GetCurrentDevices()
        {
            var deviceEnumerator = HS.GetDeviceEnumerator() as clsDeviceEnumeration;

            if (deviceEnumerator == null)
            {
                throw new HspiException(Invariant($"{PluginData.PlugInName} failed to get a device enumerator from HomeSeer."));
            }

            string parentAddress = DeviceRootDeviceManager.CreateRootDeviceAddress(deviceId);
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
                        currentChildDevices.Add(address, GetDeviceData(device));
                    }
                }
            } while (!deviceEnumerator.Finished);
        }

        private void CreateDevice([AllowNull]string label, int port, DeviceType deviceType)
        {
            if (!parentRefId.HasValue)
            {
                string parentAddress = CreateRootDeviceAddress(deviceId);
                var parentHSDevice = CreateDevice(null, parentAddress, new RootDeviceData());
                parentRefId = parentHSDevice.get_Ref(HS);
            }

            string address = DeviceRootDeviceManager.CreateAddress(deviceId, port, deviceType);
            var childDevice = GetDevice(port, deviceType);
            childDevice.Label = label;
            var childHSDevice = CreateDevice(parentRefId.Value, address, childDevice);
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
            var childAddress = hsDevice.get_Address(HS);

            var parts = childAddress.Split(AddressSeparator);

            if (parts.Length != 4)
            {
                return null;
            }

            if (!int.TryParse(parts[2], out int port))
            {
                return null;
            }

            if (!Enum.TryParse(parts[3], out DeviceType deviceType))
            {
                return null;
            }

            var device = GetDevice(port, deviceType);
            device.RefId = hsDevice.get_Ref(HS);
            return device;
        }

        /// <summary>
        /// Creates the HS device.
        /// </summary>
        /// <param name="parent">The data for parent of device.</param>
        /// <param name="rootDeviceData">The root device data.</param>
        /// <param name="deviceData">The device data.</param>
        /// <returns>New Device</returns>
        private DeviceClass CreateDevice(int? optionalParentRefId, string deviceAddress, DeviceDataBase deviceData)
        {
            logger.DebugLog(Invariant($"Creating Device with Address:{deviceAddress}"));

            DeviceClass device = null;
            int refId = HS.NewDeviceRef(deviceData.Name);
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
                //device.set_Location2(HS, parent != null ? parent.get_Name(HS) : deviceData.Name);
                device.set_Location(HS, PluginData.PlugInName);
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

                device.MISC_Set(HS, Enums.dvMISC.STATUS_ONLY);
                device.MISC_Set(HS, Enums.dvMISC.SHOW_VALUES);
                device.MISC_Clear(HS, Enums.dvMISC.AUTO_VOICE_COMMAND);
                device.MISC_Clear(HS, Enums.dvMISC.SET_DOES_NOT_CHANGE_LAST_CHANGE);
                device.set_Status_Support(HS, false);

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

                HS.SetDeviceValueByRef(refId, deviceData.InitialValue, false);
                HS.SetDeviceString(refId, deviceData.InitialString, false);
            }

            return device;
        }
    };
}