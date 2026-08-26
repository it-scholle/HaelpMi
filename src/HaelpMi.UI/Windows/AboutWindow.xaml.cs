using System.Windows;
using HaelpMi.Core.Models;
using HaelpMi.Core.Runtime;

namespace HaelpMi.UI.Windows;

/// <summary>
/// "Über mich"-Popup (Issue #6). Kundennummer kommt wie die Version aus lokalen,
/// bei der Installation fest hinterlegten Daten (<see cref="DeploymentInfo.CustomerNumber"/>,
/// vom Install-Creator vergeben, siehe Issue #39) - kein Live-Abruf von irgendwo.
/// </summary>
public partial class AboutWindow : Window
{
    public AboutWindow(DeploymentInfo deployment)
    {
        InitializeComponent();
        VersionText.Text = $"Version {LiveIdentityFactory.CurrentProgramVersion}";
        CustomerNumberText.Text = deployment.CustomerNumber.ToString();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
