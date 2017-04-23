using HomeSeerAPI;

namespace Hspi.DeviceData
{
    internal class VoltsDeviceData : NumberDeviceData
    {
        public VoltsDeviceData(int port) : base(port, DeviceType.Voltage)
        {
        }

        protected override double MinDeltaForUpdate => 0.1D;
        protected override int RangeDecimals => 1;

        public override int HSDeviceType => (int)DeviceTypeInfo_m.DeviceTypeInfo.eDeviceType_Energy.Volts;
        protected override string Suffix => " Volts";
    }
}