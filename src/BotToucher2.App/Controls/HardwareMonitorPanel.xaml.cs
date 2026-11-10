using System.Windows.Controls;
using BotToucher2.Core.Hardware;
namespace BotToucher2.App.Controls;
/// <summary>Snapshot presentation only; no timers and no ownership of sampling resources.</summary>
public partial class HardwareMonitorPanel : UserControl
{
    public HardwareMonitorPanel()
    {
        InitializeComponent();
        MetricList.ItemsSource = new[] { "CPU", "GPU", "RAM", "VRAM" }.Select(label =>
            new HardwareMetricSnapshot { Key = label.ToLowerInvariant(), Label = label, Value = "sampling…", Available = false });
    }
    public void SetObservation(HardwareObservation observation)
    {
        if (observation.Snapshot is { } snapshot)
        {
            MetricList.ItemsSource = snapshot.Metrics;
            SampleText.Text = "Sampled " + snapshot.CapturedAtUtc.ToLocalTime().ToString("HH:mm:ss");
        }
        else
        {
            MetricList.ItemsSource = new[] { "CPU", "GPU", "RAM", "VRAM" }.Select(label =>
                new HardwareMetricSnapshot { Key = label.ToLowerInvariant(), Label = label, Value = "unavailable", Available = false });
            SampleText.Text = observation.Notice ?? "Sampling unavailable.";
        }
    }
}
