using System;

namespace HaelpMi.InstallCreator;

/// <summary>
/// Issue #56: EIN Ed25519-Schlüsselpaar pro <see cref="CustomerGroupId"/> statt eines
/// globalen - <see cref="PrivateKeyBase64"/> verlässt den Anbieter nie (nicht ins Repo,
/// nicht ins Log), <see cref="PublicKeyHex"/> wird beim Bauen ins jeweilige
/// deployment.json eingebettet (siehe MainWindow.BuildAdminInstallerAsync).
/// </summary>
internal sealed record LicenseKeyPairEntry(
    Guid CustomerGroupId,
    string PrivateKeyBase64,
    string PublicKeyHex,
    DateTime ErstelltAm);
