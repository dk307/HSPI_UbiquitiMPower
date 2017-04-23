using System.Runtime.Serialization;

#pragma warning disable 0649

namespace Hspi.Connector.Model
{
    [DataContract]
    internal class WebSocketData
    {
        [DataMember(Name = "sensors")]
        public SensorData[] Sensors;
    }
}