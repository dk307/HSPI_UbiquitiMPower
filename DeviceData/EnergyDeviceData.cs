using HomeSeerAPI;

namespace Hspi.DeviceData
{
    internal class EnergyDeviceData : NumberDeviceData
    {
        public EnergyDeviceData(int port) : base(port, DeviceType.Energy)
        {
        }

        protected override double Denominator => 1000D;

        public override int HSDeviceType => (int)DeviceTypeInfo_m.DeviceTypeInfo.eDeviceType_Energy.KWH;
    }
}