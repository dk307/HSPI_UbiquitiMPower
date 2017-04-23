using System.Runtime.Serialization;

#pragma warning disable 0649

namespace Hspi.Connector.Model
{
    [DataContract]
    internal class InitialData
    {
        [DataMember(Name = "sensors")]
        public SensorData[] Sensors;
    }
}