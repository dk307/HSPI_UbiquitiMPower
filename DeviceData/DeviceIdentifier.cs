using Scheduler.Classes;
using System;

namespace Hspi.DeviceData
{
    using static System.FormattableString;

    internal class DeviceIdentifier
    {
        public DeviceIdentifier(string deviceId, int port, DeviceType deviceType)
        {
            DeviceId = deviceId;
            Port = port;
            DeviceType = deviceType;
        }

        public string DeviceId { get; }
        public int Port { get; }
        public DeviceType DeviceType { get; }

        public static DeviceIdentifier Identify(DeviceClass hsDevice)
        {
            var childAddress = hsDevice.get_Address(null);

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

            return new DeviceIdentifier(parts[1], port, deviceType);
        }

        public static string CreateRootAddress(string deviceId) => Invariant($"{PluginData.PlugInName}{AddressSeparator}{deviceId}");

        public string RootDeviceAddress => CreateRootAddress(DeviceId);
        public string Address => Invariant($"{RootDeviceAddress}{AddressSeparator}{Port}{AddressSeparator}{DeviceType}");

        private const char AddressSeparator = '.';
    }
}