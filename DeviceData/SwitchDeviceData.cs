using HomeSeerAPI;
using System.Collections.Generic;

namespace Hspi.DeviceData
{
    internal class SwitchDeviceData : DeviceData
    {
        public SwitchDeviceData(int port) : base(port, DeviceType.Output)
        {
        }

        public override void Update(IHSApplication HS, double deviceValue)
        {
            bool newValue = deviceValue != 0;

            if (!lastValue.HasValue || (newValue != lastValue.Value))
            {
                UpdateDeviceData(HS, RefId, newValue ? 255 : 0);
                lastValue = newValue;
            }
        }

        public override IList<VSVGPairs.VSPair> StatusPairs
        {
            get
            {
                var pairs = new List<VSVGPairs.VSPair>();
                pairs.Add(new VSVGPairs.VSPair(HomeSeerAPI.ePairStatusControl.Status)
                {
                    PairType = VSVGPairs.VSVGPairType.SingleValue,
                    Value = 0,
                    ControlUse = ePairControlUse._Off,
                    Status = "Off"
                });

                pairs.Add(new VSVGPairs.VSPair(HomeSeerAPI.ePairStatusControl.Status)
                {
                    PairType = VSVGPairs.VSVGPairType.SingleValue,
                    Value = 255,
                    ControlUse = ePairControlUse._On,
                    Status = "On"
                });
                return pairs;
            }
        }

        private bool? lastValue = null;
    }
}