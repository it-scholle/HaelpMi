using System.Windows;

namespace HaelpMi.UI.Windows;

/// <summary>
/// Kleines Hinweis-Popup vor dem ersten Bearbeiten von Raum/Raumnummer im User-Fenster
/// (Nutzerwunsch 05.08.2026) - reine Information, keine echte Zugriffsprüfung. Ersetzt
/// später möglichst durch einen echten Windows-Admin-Prompt (UAC), siehe ConfigWindow.
/// </summary>
public partial class AdminOnlyNoticeWindow : Window
{
    public AdminOnlyNoticeWindow()
    {
        InitializeComponent();
    }

    private void UnderstoodButton_Click(object sender, RoutedEventArgs e) => Close();
}
