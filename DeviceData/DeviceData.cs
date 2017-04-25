using HomeSeerAPI;
using NullGuard;
using System.Collections.Generic;

namespace Hspi.DeviceData
{
    using System;
    using Hspi.Connector;
    using static System.FormattableString;
    using System.Threading.Tasks;
    using System.Threading;

    /// <summary>
    ///  Base class for Child Devices
    /// </summary>
    /// <seealso cref="Hspi.DeviceDataBase" />
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal abstract class DeviceData : DeviceDataBase
    {
        public DeviceData(int port, DeviceType deviceType)
        {
            DeviceType = deviceType;
            Port = port;
        }

        public string Label { get; set; }

        public override string Name
        {
            get
            {
                string label = Label ?? Invariant($"Port {Port}");
                string typeDescription = EnumHelper.GetDescription(DeviceType);
                return Invariant($"{label} {typeDescription}");
            }
        }

        public abstract void Update(IHSApplication HS, double deviceValue);

        public virtual Task HandleCommand(mPowerConnector connector, CancellationToken token,
                                          double value, ePairControlUse control) => throw new NotImplementedException();

        public int Port { get; }
        public int RefId { get; set; }
        public DeviceType DeviceType { get; }
        public override int HSDeviceType => 0;
        public override string HSDeviceTypeString => Invariant($"{PluginData.PlugInName} Information Device");
        public override string InitialString => "--";
        public override double InitialValue => 0D;

        public override IList<VSVGPairs.VGPair> GraphicsPairs => new List<VSVGPairs.VGPair>();
    };
}