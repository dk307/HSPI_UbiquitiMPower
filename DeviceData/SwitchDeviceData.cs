using HomeSeerAPI;
using System.Collections.Generic;

namespace Hspi.DeviceData
{
    using Hspi.Connector;
    using System.Threading;
    using System.Threading.Tasks;
    using static System.FormattableString;

    internal class SwitchDeviceData : DeviceData
    {
        public SwitchDeviceData(int port) : base(port, DeviceType.Output)
        {
        }

        public override bool StatusDevice => false;
        public override string HSDeviceTypeString => Invariant($"{PluginData.PlugInName} Binary Switch");

        private const int OnValue = 100;
        private const int OffValue = 0;

        public override void Update(IHSApplication HS, double deviceValue)
        {
            bool newValue = deviceValue != OffValue;

            if (!lastValue.HasValue || (newValue != lastValue.Value))
            {
                UpdateDeviceData(HS, RefId, newValue ? OnValue : OffValue);
                lastValue = newValue;
            }
        }

        public override IList<VSVGPairs.VSPair> StatusPairs
        {
            get
            {
                var pairs = new List<VSVGPairs.VSPair>();
                pairs.Add(new VSVGPairs.VSPair(HomeSeerAPI.ePairStatusControl.Both)
                {
                    PairType = VSVGPairs.VSVGPairType.SingleValue,
                    Value = OffValue,
                    ControlUse = ePairControlUse._Off,
                    Status = "Off",
                    Render = Enums.CAPIControlType.Button
                });

                pairs.Add(new VSVGPairs.VSPair(HomeSeerAPI.ePairStatusControl.Both)
                {
                    PairType = VSVGPairs.VSVGPairType.SingleValue,
                    Value = OnValue,
                    ControlUse = ePairControlUse._On,
                    Status = "On",
                    Render = Enums.CAPIControlType.Button
                });
                return pairs;
            }
        }

        public override Task HandleCommand(MPowerConnector connector, CancellationToken token, double value, ePairControlUse control)
        {
            bool output = false;
            if (control == ePairControlUse._Off)
            {
                output = false;
            }
            else if (control == ePairControlUse._On)
            {
                output = true;
            }
            else if (value == OffValue)
            {
                output = false;
            }
            else if (value == OnValue)
            {
                output = true;
            }

            return connector.UpdateOutput(Port, output, token);
        }

        private bool? lastValue = null;
    }
}