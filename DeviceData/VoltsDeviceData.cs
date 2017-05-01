using HomeSeerAPI;

namespace Hspi.DeviceData
{
    internal class VoltsDeviceData : NumberDeviceData
    {
        public VoltsDeviceData(int port) : base(port, DeviceType.Voltage)
        {
        }

        public override int HSDeviceType => (int)DeviceTypeInfo_m.DeviceTypeInfo.eDeviceType_Energy.Volts;
    }
}