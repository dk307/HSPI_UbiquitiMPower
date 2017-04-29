using HomeSeerAPI;
using Hspi.Connector;
using NullGuard;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hspi.DeviceData
{
    using static System.FormattableString;

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

        public override void SetInitialData(IHSApplication HS, int refId)
        {
            HS.SetDeviceValueByRef(refId, 0D, false);
            HS.set_DeviceInvalidValue(refId, true);
        }

        public abstract void Update(IHSApplication HS, double deviceValue);

        public virtual Task HandleCommand(MPowerConnector connector, CancellationToken token,
                                          double value, ePairControlUse control) => throw new NotImplementedException();

        public int Port { get; }
        public int RefId { get; set; }
        public DeviceType DeviceType { get; }
        public override int HSDeviceType => 0;
        public override string HSDeviceTypeString => Invariant($"{PluginData.PlugInName} Information Device");

        public override IList<VSVGPairs.VGPair> GraphicsPairs => GetSingleGraphicsPairs("electricity.gif");
    };
}