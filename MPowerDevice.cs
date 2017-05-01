using NullGuard;
using System;
using System.Collections.Generic;
using System.Net;
using System.Linq;

namespace Hspi
{
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class MPowerDevice : IEquatable<MPowerDevice>
    {
        public MPowerDevice(string id, string name, IPAddress deviceIP,
                            string username, string password, IReadOnlyDictionary<DeviceType, double> enabledTypesAndResolution, ISet<int> enabledPorts)
        {
            EnabledPorts = enabledPorts;
            Name = name;
            Password = password;
            Username = username;
            DeviceIP = deviceIP;
            EnabledTypesAndResolution = enabledTypesAndResolution;
            Id = id;
        }

        public string Id { get; }
        public string Name { get; }
        public ISet<int> EnabledPorts { get; }
        public IReadOnlyDictionary<DeviceType, double> EnabledTypesAndResolution { get; }
        public IPAddress DeviceIP { get; }
        public string Username { get; }
        public string Password { get; }

        public bool Equals(MPowerDevice other)
        {
            if (this == other)
            {
                return true;
            }

            return Id == other.Id &&
                Name == other.Name &&
                Username == other.Username &&
                Password == other.Password &&
                DeviceIP == other.DeviceIP &&
                EnabledPorts.SetEquals(other.EnabledPorts) &&
                ((EnabledTypesAndResolution.Count == other.EnabledTypesAndResolution.Count) &&
                 !EnabledTypesAndResolution.Except(other.EnabledTypesAndResolution).Any());
        }
    }
}