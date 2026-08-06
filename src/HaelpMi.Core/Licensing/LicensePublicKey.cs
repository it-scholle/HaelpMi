namespace HaelpMi.Core.Licensing;

/// <summary>
/// Public half of the Ed25519 keypair used by the separate customer-licensing
/// mechanism (FR-32, Pflichtenheft 1./3.9/5.10) - that mechanism itself (license file
/// format, signed fields, verification flow) is out of scope for this Pflichtenheft and
/// "wird separat spezifiziert". Nothing else in this codebase reads this constant yet.
///
/// PLACEHOLDER: this is not a real production key. It must be replaced with the actual
/// public key once the customer-licensing mechanism is specified and a real Ed25519
/// keypair has been generated *outside* of this repository. The matching private key
/// must never be committed here (see .gitignore) or anywhere else in this repo.
/// </summary>
public static class LicensePublicKey
{
    /// <summary>32-byte Ed25519 public key, hex-encoded. Placeholder value - see class remarks.</summary>
    public const string PlaceholderHex =
        "0000000000000000000000000000000000000000000000000000000000000000";
}
