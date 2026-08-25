using HaelpMi.Core.Licensing;
using Xunit;

namespace HaelpMi.Core.Tests;

public class LicenseRenewalMailBuilderTests
{
    private static readonly Guid CustomerGroupId = new("d3f1a000-a11d-4000-9000-0000000000aa");
    private static readonly DateTime ExpiresAt = new(2026, 10, 24, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Build_TargetsProviderAddress()
    {
        var mail = LicenseRenewalMailBuilder.Build(CustomerGroupId, ExpiresAt);

        Assert.Equal("info@it-scholle.de", mail.To);
    }

    [Fact]
    public void Build_SubjectNamesCustomerGroupAsRenewalRequest()
    {
        var mail = LicenseRenewalMailBuilder.Build(CustomerGroupId, ExpiresAt);

        Assert.Contains("Verlängerungsanfrage", mail.Subject);
        Assert.Contains(CustomerGroupId.ToString(), mail.Subject);
    }

    [Fact]
    public void Build_BodyStatesRenewalOfExistingAgreementForNextBillingPeriod()
    {
        var mail = LicenseRenewalMailBuilder.Build(CustomerGroupId, ExpiresAt);

        Assert.Contains("Verlängerung der bestehenden Nutzungsvereinbarung", mail.Body);
        Assert.Contains("Abrechnungszeitraum", mail.Body);
        Assert.Contains(CustomerGroupId.ToString(), mail.Body);
        Assert.Contains("24.10.2026", mail.Body);
        Assert.DoesNotContain("Gerät", mail.Body); // Nutzerfeedback: Gerätename ist nicht notwendig
    }

    [Fact]
    public void ToMailtoUri_EncodesSubjectAndBodyForMailClient()
    {
        var mail = LicenseRenewalMailBuilder.Build(CustomerGroupId, ExpiresAt);

        var uri = mail.ToMailtoUri().AbsoluteUri;

        Assert.StartsWith("mailto:info@it-scholle.de?subject=", uri);
        Assert.DoesNotContain(" ", uri); // Leerzeichen müssen kodiert sein
        Assert.Contains("body=", uri);
    }
}
