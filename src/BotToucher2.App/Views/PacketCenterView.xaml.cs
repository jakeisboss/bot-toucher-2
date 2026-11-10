using System.Windows;
using System.Windows.Controls;
using BotToucher2.Core.Shell;
namespace BotToucher2.App.Views;
public partial class PacketCenterView : UserControl
{
    private bool _updating;
    public event Action<PacketSection>? SectionSelected;
    public PacketCenterView() => InitializeComponent();
    public void ShowSection(PacketSection section)
    {
        _updating = true;
        PacketTabs.SelectedIndex = section switch { PacketSection.Project => 0, PacketSection.Activity => 2, _ => 1 };
        _updating = false;
    }
    private void SectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || !IsInitialized || e.Source != PacketTabs) return;
        SectionSelected?.Invoke(PacketTabs.SelectedIndex switch { 0 => PacketSection.Project, 2 => PacketSection.Activity, _ => PacketSection.Packets });
    }
    private void OpenUpdates(object sender, RoutedEventArgs e) => SectionSelected?.Invoke(PacketSection.UpdateCenter);
}
