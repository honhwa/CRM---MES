using System.IO;
using CrmMes.Desktop;

namespace CrmMes.Desktop.Tests;

/// <summary>Come ClientSettingsTests: ogni test usa un proprio file temporaneo, mai la vera
/// bozza in %LOCALAPPDATA%\CrmMes\company-profile-draft.json.</summary>
public class CompanyProfileDraftTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"crmmes-draft-test-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public void Load_FileDoesNotExist_ReturnsNull() =>
        Assert.Null(CompanyProfileDraft.Load(_path));

    [Fact]
    public void SaveThenLoad_RoundTripsEverything()
    {
        var profile = new SaveCompanyProfileDto("Officina Rossi", "IT01234567890", "Via Roma 1", "0123456789", "info@rossi.it",
            "panel-building", ["metel", "quality"], ["panel-building"]);
        var departments = new List<SaveDepartmentDto> { new("assembly", "Assemblaggio", null, true) };
        var original = new CompanyProfileDraft(profile, departments, "80012345", new DateTime(2026, 10, 13, 9, 30, 0, DateTimeKind.Utc));
        original.Save(_path);

        var loaded = CompanyProfileDraft.Load(_path);

        Assert.NotNull(loaded);
        Assert.Equal("Officina Rossi", loaded!.Profile.CompanyName);
        Assert.Equal(["metel", "quality"], loaded.Profile.EnabledModules);
        Assert.Single(loaded.Departments);
        Assert.Equal("Assemblaggio", loaded.Departments[0].Name);
        Assert.Equal("80012345", loaded.Gs1CompanyPrefix);
        Assert.Equal(original.SavedAtUtc, loaded.SavedAtUtc);
    }

    [Fact]
    public void Load_CorruptedJson_ReturnsNullInsteadOfThrowing()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ not json ");

        Assert.Null(CompanyProfileDraft.Load(_path));
    }

    [Fact]
    public void Delete_RemovesTheFile()
    {
        new CompanyProfileDraft(
            new SaveCompanyProfileDto("Prova", null, null, null, null, "panel-building", [], null),
            [], null, DateTime.UtcNow).Save(_path);
        Assert.True(File.Exists(_path));

        CompanyProfileDraft.Delete(_path);

        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void Delete_FileDoesNotExist_DoesNotThrow() =>
        CompanyProfileDraft.Delete(_path);
}
