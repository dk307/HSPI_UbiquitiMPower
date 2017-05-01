using NullGuard;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Hspi
{
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class MPowerDevice : IEquatable<MPowerDevice>
    {
        public MPowerDevice(string id, string name, IPAddress deviceIP,
                            string username, string password, ISet<DeviceType> enabledTypes,
                            IReadOnlyDictionary<DeviceType, double> resolution, ISet<int> enabledPorts)
        {
            EnabledPorts = enabledPorts;
            Name = name;
            Password = password;
            Username = username;
            DeviceIP = deviceIP;
            EnabledTypes = enabledTypes;
            Resolution = resolution;
            Id = id;
        }

        public string Id { get; }
        public string Name { get; }
        public ISet<int> EnabledPorts { get; }
        public ISet<DeviceType> EnabledTypes { get; }
        public IReadOnlyDictionary<DeviceType, double> Resolution { get; }
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
                EnabledTypes.SetEquals(other.EnabledTypes) &&
                ((Resolution.Count == other.Resolution.Count) &&
                 !Resolution.Except(other.Resolution).Any());
        }
    }
}