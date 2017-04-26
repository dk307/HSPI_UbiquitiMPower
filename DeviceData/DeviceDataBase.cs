using HomeSeerAPI;
using NullGuard;
using System.Collections.Generic;
using System.IO;

namespace Hspi.DeviceData
{
    /// <summary>
    /// This is base class for creating and updating devices in HomeSeer.
    /// </summary>
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal abstract class DeviceDataBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceDataBase" /> class.
        /// </summary>
        /// <param name="name">Name of the Device</param>
        protected DeviceDataBase()
        {
        }

        /// <summary>
        /// Gets the status pairs for creating device.
        /// </summary>
        /// <param name="config">The plugin configuration.</param>
        /// <returns></returns>
        public abstract IList<VSVGPairs.VSPair> StatusPairs { get; }

        /// <summary>
        /// Gets the graphics pairs for creating device
        /// </summary>
        /// <param name="config">The plugin configuration.</param>
        /// <returns></returns>
        public abstract IList<VSVGPairs.VGPair> GraphicsPairs { get; }

        public abstract string Name { get; }
        public abstract int HSDeviceType { get; }
        public virtual DeviceTypeInfo_m.DeviceTypeInfo.eDeviceAPI DeviceAPI => DeviceTypeInfo_m.DeviceTypeInfo.eDeviceAPI.Plug_In;
        public abstract string HSDeviceTypeString { get; }
        public abstract string InitialString { get; }
        public abstract double InitialValue { get; }
        public virtual bool StatusDevice => true;

        protected static IList<VSVGPairs.VGPair> GetSingleGraphicsPairs(string fileName)
        {
            var pairs = new List<VSVGPairs.VGPair>();
            pairs.Add(new VSVGPairs.VGPair()
            {
                PairType = VSVGPairs.VSVGPairType.Range,
                Graphic = Path.Combine(PluginData.HSImagesPathRoot, fileName),
                RangeStart = int.MinValue,
                RangeEnd = int.MaxValue,
            });
            return pairs;
        }

        /// <summary>
        /// Updates the device data from number data
        /// </summary>
        /// <param name="HS">Homeseer application.</param>
        /// <param name="refId">The reference identifier.</param>
        /// <param name="data">Number data.</param>
        protected void UpdateDeviceData(IHSApplication HS, int refId, double? data)
        {
            if (data.HasValue)
            {
                HS.SetDeviceString(refId, null, false);
                HS.SetDeviceValueByRef(refId, data.Value, true);
            }
            else
            {
                // do not update double value on no value.
                HS.SetDeviceString(refId, InitialString, false);
            }
        }
    };
}