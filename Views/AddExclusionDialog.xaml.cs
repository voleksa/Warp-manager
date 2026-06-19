using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using WarpManager.Models;

namespace WarpManager.Views;

public partial class AddExclusionDialog : Window
{
    private readonly ExclusionType _type;
    public string EnteredValue { get; private set; } = string.Empty;

    private static readonly Regex HostRegex =
        new(@"^(\*\.)?([a-zA-Z0-9-]+\.)+[a-zA-Z]{2,}$|^localhost$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public AddExclusionDialog(ExclusionType type)
    {
        InitializeComponent();
        _type         = type;
        TitleText.Text = type == ExclusionType.Ip ? "Add IP / CIDR Exclusion" : "Add Host / Domain Exclusion";
        InputBox.Focus();
    }

    private void InputBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var input = InputBox.Text.Trim();
        if (string.IsNullOrEmpty(input))
        {
            AddButton.IsEnabled        = false;
            ValidationText.Visibility  = Visibility.Collapsed;
            return;
        }

        var (ok, errorMsg) = Validate(_type, input);
        AddButton.IsEnabled = ok;
        if (ok)
        {
            ValidationText.Visibility = Visibility.Collapsed;
        }
        else
        {
            ValidationText.Text       = $"⚠ {errorMsg}";
            ValidationText.Visibility = Visibility.Visible;
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        EnteredValue  = InputBox.Text.Trim();
        DialogResult  = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private static (bool ok, string error) Validate(ExclusionType type, string input) =>
        type == ExclusionType.Ip
            ? (IsValidIpOrCidr(input), "Must be a valid IP or CIDR (e.g. 192.168.1.0/24)")
            : (IsValidHost(input),     "Must be a valid domain (e.g. internal.corp)");

    private static bool IsValidIpOrCidr(string input)
    {
        try { IPNetwork.Parse(input); return true; }
        catch { return false; }
    }

    private static bool IsValidHost(string input) => HostRegex.IsMatch(input);
}
