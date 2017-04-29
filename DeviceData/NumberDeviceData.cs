using HomeSeerAPI;
using System;
using System.Collections.Generic;

namespace Hspi.DeviceData
{
    internal class NumberDeviceData : DeviceData
    {
        public NumberDeviceData(int port, DeviceType deviceType) : base(port, deviceType)
        {
        }

        public override void Update(IHSApplication HS, double deviceValue)
        {
            double MinDelta = MinDeltaForUpdate;
            double value = deviceValue / Denominator;
            if (!lastUpdate.HasValue || Math.Abs(lastUpdate.Value - value) > MinDelta)
            {
                UpdateDeviceData(HS, RefId, value);
                lastUpdate = value;
            }
        }

        public override bool StatusDevice => true;

        protected virtual double MinDeltaForUpdate => 0.01D;
        protected virtual double Denominator => 1D;
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
                    RangeStatusDecimals = RangeDecimals,
                    RangeStatusSuffix = Suffix,
                });
                return pairs;
            }
        }

        protected virtual int RangeDecimals => 2;
        protected virtual string Suffix => string.Empty;
        private double? lastUpdate = null;
    }
}