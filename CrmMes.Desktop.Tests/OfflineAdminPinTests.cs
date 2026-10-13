using CrmMes.Desktop;

namespace CrmMes.Desktop.Tests;

public class OfflineAdminPinTests
{
    [Theory]
    [InlineData("1234", true)]
    [InlineData("12345678", true)]
    [InlineData("123", false)] // too short
    [InlineData("123456789", false)] // too long
    [InlineData("12a4", false)] // not all digits
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidFormat_AcceptsOnly4To8Digits(string? pin, bool expected) =>
        Assert.Equal(expected, OfflineAdminPin.IsValidFormat(pin));

    [Fact]
    public void Verify_CorrectPin_Succeeds()
    {
        var (hash, salt) = OfflineAdminPin.Hash("4821");

        Assert.True(OfflineAdminPin.Verify("4821", hash, salt));
    }

    [Fact]
    public void Verify_WrongPin_Fails()
    {
        var (hash, salt) = OfflineAdminPin.Hash("4821");

        Assert.False(OfflineAdminPin.Verify("9999", hash, salt));
    }

    [Fact]
    public void Hash_SamePinTwice_ProducesDifferentSaltAndHash()
    {
        var first = OfflineAdminPin.Hash("4821");
        var second = OfflineAdminPin.Hash("4821");

        Assert.NotEqual(first.Salt, second.Salt);
        Assert.NotEqual(first.Hash, second.Hash);
        // Entrambi restano verificabili con lo stesso PIN, nonostante salt e hash diversi.
        Assert.True(OfflineAdminPin.Verify("4821", first.Hash, first.Salt));
        Assert.True(OfflineAdminPin.Verify("4821", second.Hash, second.Salt));
    }

    [Fact]
    public void Verify_CorruptedStoredValues_FailsInsteadOfThrowing()
    {
        Assert.False(OfflineAdminPin.Verify("4821", "not-base64!!", "not-base64!!"));
    }
}
