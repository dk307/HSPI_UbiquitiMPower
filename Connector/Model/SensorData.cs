using System.Runtime.Serialization;

#pragma warning disable 0649

namespace Hspi.Connector.Model
{
    [DataContract]
    internal class SensorData
    {
        [DataMember(Name = "label")]
        public string Label;

        [DataMember(Name = "port")]
        public int Port;

        [DataMember(Name = "output")]
        public int Output;

        [DataMember(Name = "power")]
        public double Power;

        [DataMember(Name = "current")]
        public double Current;

        [DataMember(Name = "voltage")]
        public double Voltage;

        [DataMember(Name = "powerfactor")]
        public double PowerFactor;

        [DataMember(Name = "energy")]
        public double Energy;

        public SensorData Clone()
        {
            return (SensorData)this.MemberwiseClone();
        }

        public void ApplyDelta(SensorData data)
        {
            Output = data.Output;
            Power = data.Power;
            Current = data.Current;
            Voltage = data.Voltage;
            PowerFactor = data.PowerFactor;
            Energy = data.Energy;
        }
    }
}