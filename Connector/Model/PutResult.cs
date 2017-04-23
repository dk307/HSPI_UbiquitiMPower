using System.Runtime.Serialization;

#pragma warning disable 0649

namespace Hspi.Connector.Model
{
    [DataContract]
    internal class PutResult
    {
        [DataMember(Name = "status")]
        public string Status;
    }
}