using System.Runtime.CompilerServices;

// Erlaubt Unit-Tests direkten Zugriff auf einzelne, sicherheitsrelevante interne
// Hilfsfunktionen (z. B. UpdateOrchestrator-Versionsvergleich/Kreis-Quote-Reihenfolge),
// ohne sie dafür in die öffentliche API aufnehmen zu müssen.
[assembly: InternalsVisibleTo("HaelpMi.Core.Tests")]
