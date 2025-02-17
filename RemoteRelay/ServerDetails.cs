using System;

namespace RemoteRelay;

[Serializable]
public struct ServerDetails
{
   public string Host { get; set; }
   public int Port { get; set; }
}