namespace HaelpMi.InstallCreator.Tests;

/// <summary>
/// WPF-Controls verlangen ein STA-Apartment; xUnit-Testthreads laufen standardmäßig MTA.
/// Eigener kleiner Runner statt einer zusätzlichen Test-Bibliothek nur für dieses eine Problem.
/// </summary>
internal static class StaFact
{
    public static void Run(Action action)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (caught is not null)
        {
            throw caught;
        }
    }
}
