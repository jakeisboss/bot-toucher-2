using System.Text.Json.Serialization;
namespace BotToucher2.Core.Shell;
public enum Destination { Home, PacketCenter, Data, Lab, Settings, Help }
public enum PacketSection { Project, Packets, Activity, UpdateCenter }
public enum RailTab { Context, Widgets }
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ShellState
{
    [JsonRequired]
    public int SchemaVersion { get; init; } = 1;
    [JsonRequired]
    public Destination Destination { get; init; }
    [JsonRequired]
    public PacketSection PacketSection { get; init; }
    [JsonRequired]
    public RailTab RightTab { get; init; }
    [JsonRequired]
    public bool LeftVisible { get; init; } = true;
    [JsonRequired]
    public bool RightVisible { get; init; } = true;
    [JsonRequired]
    public double LeftWidth { get; init; } = 180;
    [JsonRequired]
    public double RightWidth { get; init; } = 280;
    public void Validate()
    {
        if (SchemaVersion != 1 || !Enum.IsDefined(Destination) || !Enum.IsDefined(PacketSection) || !Enum.IsDefined(RightTab))
            throw new ArgumentException("Unsupported shell layout or navigation.");
        if (!double.IsFinite(LeftWidth) || LeftWidth < 140 || LeftWidth > 300 ||
            !double.IsFinite(RightWidth) || RightWidth < 220 || RightWidth > 360)
            throw new ArgumentException("Rail widths are outside supported bounds.");
    }
}
