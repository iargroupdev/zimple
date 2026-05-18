using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Zimple.Models;
using Zimple.Services;

namespace Zimple
{
    public partial class PlcConfigurationWindow : Window
    {
        private List<PlcConfig> plcList;

        public PlcConfigurationWindow(List<PlcConfig> list)
        {
            InitializeComponent();
            plcList = list ?? new List<PlcConfig>();
            plcGrid.ItemsSource = plcList;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            PlcConfigService.Save(plcList);
            this.Close();
        }

        private void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                var row = FindParent<DataGridRow>(element);
                if (row == null)
                    return;

                // ===============================
                // NEW ROW  →  ADD
                // ===============================
                if (row.IsNewItem)
                {
                    plcGrid.CommitEdit(DataGridEditingUnit.Row, true);

                    var newItem = row.Item as PlcConfig;
                    if (newItem == null)
                        return;

                    if (string.IsNullOrWhiteSpace(newItem.Name))
                    {
                        ShowError("PLC name is required.");
                        RemoveItem(newItem);
                        return;
                    }

                    if (!IsValidIp(newItem.IpAddress))
                    {
                        ShowError("Invalid IP address format.");
                        RemoveItem(newItem);
                        return;
                    }

                    // DUPLICATE IP CHECK
                    bool ipExists = plcList
                        .Where(p => p != newItem)
                        .Any(p => p.IpAddress.Trim() == newItem.IpAddress.Trim());

                    if (ipExists)
                    {
                        ShowError("This IP address is already registered.");
                        RemoveItem(newItem);
                        return;
                    }

                    plcGrid.Items.Refresh();
                    return;
                }

                // ===============================
                // EXISTING ROW  →  DELETE
                // ===============================
                if (element.Tag is PlcConfig plc)
                {
                    var result = MessageBox.Show(
                        $"Delete PLC '{plc.Name}'?",
                        "Confirm",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result != MessageBoxResult.Yes)
                        return;

                    plcList.Remove(plc);
                    plcGrid.Items.Refresh();
                }
            }
        }

        // ===============================
        // HELPERS
        // ===============================

        private void RemoveItem(PlcConfig item)
        {
            plcList.Remove(item);
            plcGrid.Items.Refresh();
        }

        private void ShowError(string message)
        {
            MessageBox.Show(
                message,
                "Validation Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        private bool IsValidIp(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return false;

            var parts = ip.Split('.');

            if (parts.Length != 4)
                return false;

            foreach (var part in parts)
            {
                if (!int.TryParse(part, out int value))
                    return false;

                if (value < 0 || value > 255)
                    return false;
            }

            return true;
        }

        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);

            if (parentObject == null)
                return null;

            if (parentObject is T parent)
                return parent;

            return FindParent<T>(parentObject);
        }
    }
}