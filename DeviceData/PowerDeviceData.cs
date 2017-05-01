using HomeSeerAPI;

namespace Hspi.DeviceData
{
    internal class PowerDeviceData : NumberDeviceData
    {
        public PowerDeviceData(int port) : base(port, DeviceType.Power)
        {
        }

        public override int HSDeviceType => (int)DeviceTypeInfo_m.DeviceTypeInfo.eDeviceType_Energy.Watts;
    }
}