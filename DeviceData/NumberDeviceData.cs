using HomeSeerAPI;
using System.Collections.Generic;

namespace Hspi.DeviceData
{
    internal class NumberDeviceData : DeviceData
    {
        public NumberDeviceData(int port, DeviceType deviceType) : base(port, deviceType)
        {
        }

        public override void Update(IHSApplication HS, double value)
        {
            if (!lastUpdate.HasValue || lastUpdate.Value != value)
            {
                UpdateDeviceData(HS, RefId, value);
                lastUpdate = value;
            }
        }

        public override bool StatusDevice => true;
        public override DeviceTypeInfo_m.DeviceTypeInfo.eDeviceAPI DeviceAPI => DeviceTypeInfo_m.DeviceTypeInfo.eDeviceAPI.Energy;

        public override IList<VSVGPairs.VSPair> StatusPairs
        {
            get
            {
                var pairs = new List<VSVGPairs.VSPair>();
                pairs.Add(new VSVGPairs.VSPair(HomeSeerAPI.ePairStatusControl.Status)
                {
                    PairType = VSVGPairs.VSVGPairType.Range,
                    RangeStart = int.MinValue,
                    RangeEnd = int.MaxValue,
                    IncludeValues = true,
                    RangeStatusDecimals = 3,
                    RangeStatusSuffix = " " + PluginConfig.GetUnits(DeviceType),
                });
                return pairs;
            }
        }

        private double? lastUpdate = null;
    }
}