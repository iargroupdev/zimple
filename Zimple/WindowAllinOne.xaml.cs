using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Zimple.Models;
using Zimple.Services;

namespace Zimple
{
    public partial class WindowAllinOne : Window
    {
        private List<PlcConfig> plcList;

        public WindowAllinOne()
        {
            InitializeComponent();
            LoadPlcs();
        }

        private void LoadPlcs()
        {
            plcList = PlcConfigService.Load();
            BuildTabs();
        }

        private void BuildTabs()
        {
            tabControlOps.Items.Clear();

            if (plcList == null || plcList.Count == 0)
            {
                tabControlOps.Visibility = Visibility.Collapsed;
                noPlcMessage.Visibility = Visibility.Visible;
                return;
            }

            noPlcMessage.Visibility = Visibility.Collapsed;
            tabControlOps.Visibility = Visibility.Visible;

            foreach (var plc in plcList)
            {
                if (plc == null || string.IsNullOrWhiteSpace(plc.IpAddress))
                    continue;

                var parts = plc.IpAddress.Split('.');
                if (parts.Length != 4)
                    continue;

                ConnectionOP uc = new ConnectionOP();

                uc.ipPart1.Text = parts[0];
                uc.ipPart2.Text = parts[1];
                uc.ipPart3.Text = parts[2];
                uc.ipPart4.Text = parts[3];

                uc.ipPart1.IsReadOnly = true;
                uc.ipPart2.IsReadOnly = true;
                uc.ipPart3.IsReadOnly = true;
                uc.ipPart4.IsReadOnly = true;

                // Create status dot
                Ellipse statusDot = new Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = Brushes.Gray,
                    Margin = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                TextBlock nameText = new TextBlock
                {
                    Text = plc.Name,
                    VerticalAlignment = VerticalAlignment.Center
                };

                StackPanel headerPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal
                };

                headerPanel.Children.Add(statusDot);
                headerPanel.Children.Add(nameText);

                TabItem tab = new TabItem
                {
                    Header = headerPanel,
                    Content = uc
                };

                tabControlOps.Items.Add(tab);

                // Subscribe to connection status event
                uc.ConnectionStatusChanged += (connected) =>
                {
                    statusDot.Fill = connected ? Brushes.Green : Brushes.Red;
                };
            }
        }

        private void OpenConfiguration_Click(object sender, RoutedEventArgs e)
        {
            PlcConfigurationWindow config = new PlcConfigurationWindow(plcList);
            config.ShowDialog();

            plcList = PlcConfigService.Load();
            BuildTabs();
        }

        private void ConnectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (TabItem tab in tabControlOps.Items)
            {
                if (tab.Content is ConnectionOP uc)
                {
                    uc.ConnectFromParent();
                }
            }
        }
    }
}