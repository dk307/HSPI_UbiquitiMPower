using System.ComponentModel;

namespace Hspi
{
    internal enum DeviceType
    {
        [Description("Switch")]
        Output = 1,

        Power,
        Current,
        Voltage,

        [Description("Power Factor")]
        PowerFactor,

        Energy,
    }
}