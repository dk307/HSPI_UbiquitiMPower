using System.ComponentModel;

namespace Hspi
{
    internal enum DeviceType
    {
        [Description("Switch")]
        Output = 1,

        [Description("Power(Watts)")]
        Power,

        Current,
        Voltage,

        [Description("Power Factor")]
        PowerFactor,

        [Description("Kw Hours")]
        Energy,
    }
}