using HomeSeerAPI;

namespace Hspi.DeviceData
{
    internal class CurrentDeviceData : NumberDeviceData
    {
        public CurrentDeviceData(int port) : base(port, DeviceType.Current)
        {
        }

        public override int HSDeviceType => (int)DeviceTypeInfo_m.DeviceTypeInfo.eDeviceType_Energy.Amps;
    }
}