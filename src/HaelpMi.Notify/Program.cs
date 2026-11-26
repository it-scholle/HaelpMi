using System.Drawing;
using System.Windows.Forms;

namespace HaelpMi.Notify;

/// <summary>
/// Zeigt genau eine Windows-Benachrichtigung (Titel und Text als die beiden Argumente) und
/// beendet sich, sobald sie geschlossen ist, spätestens nach <see cref="MaxLaufzeit"/>.
/// Kein Netzwerk, kein Zustand; Startfehler protokolliert der aufrufende Agent.
/// </summary>
internal static class Program
{
    private static readonly TimeSpan MaxLaufzeit = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Anzeigedauer = TimeSpan.FromSeconds(10);

    // Grenzen von NOTIFYICONDATA (szInfoTitle 64, szInfo 256 Zeichen inkl. Nullterminator).
    private const int MaxTitelLaenge = 63;
    private const int MaxTextLaenge = 255;

    private const int ExitOk = 0;
    private const int ExitUngueltigeArgumente = 2;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 2
            || string.IsNullOrWhiteSpace(args[0]) || args[0].Length > MaxTitelLaenge
            || string.IsNullOrWhiteSpace(args[1]) || args[1].Length > MaxTextLaenge)
        {
            return ExitUngueltigeArgumente;
        }

        ApplicationConfiguration.Initialize();

        using var symbol = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = Application.ProductName,
            Visible = true,
        };
        symbol.BalloonTipClosed += (_, _) => Application.ExitThread();
        symbol.BalloonTipClicked += (_, _) => Application.ExitThread();

        using var notbremse = new System.Windows.Forms.Timer { Interval = (int)MaxLaufzeit.TotalMilliseconds };
        notbremse.Tick += (_, _) => Application.ExitThread();
        notbremse.Start();

        symbol.ShowBalloonTip((int)Anzeigedauer.TotalMilliseconds, args[0], args[1], ToolTipIcon.None);
        Application.Run();

        symbol.Visible = false;
        return ExitOk;
    }
}
