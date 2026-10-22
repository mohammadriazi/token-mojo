using System.Windows;
using System.Windows.Controls;
using TokenMojo.Core;

namespace TokenMojo;

public partial class DashboardView : UserControl
{
    public DashboardView() => InitializeComponent();

    public void Show(DashboardModel model)
    {
        DataContext = model;
        Panel.Visibility = Visibility.Visible;
        MessageText.Visibility = Visibility.Collapsed;
    }

    public void ShowMessage(string message)
    {
        MessageText.Text = message;
        MessageText.Visibility = Visibility.Visible;
        Panel.Visibility = Visibility.Collapsed;
    }
}
