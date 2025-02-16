using System;
using System.Collections.Generic;
using System.Linq;

namespace RemoteRelay.Common;

[Serializable]
public struct AppSettings
{
    //Sources
    public List<RelayConfig> Routes { get; set; }
    public string? DefaultSource { get; set; }
    public Dictionary<string, int> PhysicalSourceButtons { get; set; }
    public IReadOnlyCollection<string> Sources => Routes.Select(x => x.SourceName).Distinct().ToArray();
    public IReadOnlyCollection<string> Outputs => Routes.Select(x => x.OutputName).Distinct().ToArray();


    //Communication
    public string ServerName { get; set; }
   public int ServerPort { get; set; }
   public string? TcpMirrorAddress { get; set; }
    public int? TcpMirrorPort { get; set; }

    //Options
    public bool FlashOnSelect { get; set; }
    public bool ShowIpOnScreen { get; set; }
    public bool Logging { get; set; }
    public string LogoFile { get; set; }
}