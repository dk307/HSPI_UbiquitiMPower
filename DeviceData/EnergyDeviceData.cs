using HomeSeerAPI;

namespace Hspi.DeviceData
{
    internal class EnergyDeviceData : NumberDeviceData
    {
        public EnergyDeviceData(int port) : base(port, DeviceType.Energy)
        {
        }

        protected override double MinDeltaForUpdate => 0.1D;
        protected override double Denominator => 1000D;
        protected override int RangeDecimals => 1;

        public override int HSDeviceType => (int)DeviceTypeInfo_m.DeviceTypeInfo.eDeviceType_Energy.KWH;
        protected override string Suffix => " KWH";
    }
}