using HomeSeerAPI;
using NullGuard;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Hspi;

namespace Hspi.DeviceData
{
    using static System.FormattableString;

    /// <summary>
    ///  Base class for Root Devices
    /// </summary>
    /// <seealso cref="Hspi.DeviceDataBase" />
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class RootDeviceData : DeviceDataBase
    {
        public RootDeviceData()
        {
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
                });
                return pairs;
            }
        }

        public override IList<VSVGPairs.VGPair> GraphicsPairs => GetSingleGraphicsPairs("root.png");

        public override string HSDeviceTypeString => Invariant($"{PluginData.PlugInName} Root Device");
        public override string InitialString => "Root";
        public override double InitialValue => 0D;
        public override int HSDeviceType => (int)DeviceTypeInfo_m.DeviceTypeInfo.eDeviceType_Plugin.Root;

        public override string Name => "mPower Root Device";
    }
}