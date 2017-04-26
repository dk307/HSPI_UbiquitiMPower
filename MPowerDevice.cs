using NullGuard;
using System;
using System.Collections.Generic;
using System.Net;

namespace Hspi
{
    [NullGuard(ValidationFlags.Arguments | ValidationFlags.NonPublic)]
    internal class MPowerDevice : IEquatable<MPowerDevice>
    {
        public MPowerDevice(string id, string name, IPAddress deviceIP,
                            string username, string password, ISet<DeviceType> enabledTypes, ISet<int> enabledPorts)
        {
            EnabledPorts = enabledPorts;
            Name = name;
            Password = password;
            Username = username;
            DeviceIP = deviceIP;
            EnabledTypes = enabledTypes;
            Id = id;
        }

        public string Id { get; }
        public string Name { get; }
        public ISet<int> EnabledPorts { get; }
        public ISet<DeviceType> EnabledTypes { get; }
        public IPAddress DeviceIP { get; }
        public string Username { get; }
        public string Password { get; }

        public bool Equals(MPowerDevice other)
        {
            return Id == other.Id &&
                Name == other.Name &&
                Username == other.Username &&
                Password == other.Password &&
                DeviceIP == other.DeviceIP &&
                EnabledTypes.SetEquals(other.EnabledTypes) &&
                EnabledTypes.SetEquals(other.EnabledTypes);
        }
    }
}