using System;
using System.Collections.Generic;

namespace RemoteRelay;

[Serializable]
public struct AppSettings
{
    //Sources
    public List<OutputConfig> Sources { get; set; }
    public string? DefaultSource { get; set; }
    public int Outputs { get; set; }
    
    //Communication
    public string ServerName { get; set; }
    public bool IsServer { get; set; }
    public int Port { get; set; }
    
    //Options
    public bool FlashOnSelect { get; set; }
    public bool ShowIpOnScreen { get; set; }
    public bool Logging { get; set; }
    public string LogoFile { get; set; }
}