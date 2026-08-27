using System.Runtime.CompilerServices;

// Erlaubt Unit-Tests direkten Zugriff auf einzelne, sicherheitsrelevante interne
// Hilfsfunktionen (z. B. UpdateOrchestrator-Versionsvergleich/Kreis-Quote-Reihenfolge),
// ohne sie dafür in die öffentliche API aufnehmen zu müssen.
[assembly: InternalsVisibleTo("HaelpMi.Core.Tests")]

// Issue #18/#19: HaelpMi.InstallCreator.Tests prüft, dass eine dort erstellte Lizenz gegen
// den echten LicenseReader.Load(string, Guid, byte[])-Overload (mit Wegwerf-Schlüsselpaar
// statt dem eingebetteten Produktionsschlüssel) tatsächlich verifiziert - ohne diese
// Freigabe war der Formatunterschied zwischen unabhängig gebauten Ersteller/Prüfer nur
// durch manuelles Gegenlesen zu finden.
[assembly: InternalsVisibleTo("HaelpMi.InstallCreator.Tests")]
