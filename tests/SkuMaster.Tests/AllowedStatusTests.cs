using SkuMaster.Core;
using Xunit;

namespace SkuMaster.Tests;

public sealed class AllowedStatusTests
{
    [Fact]
    public void LegacyRuleStatusesRecoverWithoutLosingOtherPreferences()
    {
        var path = Path.Combine(Path.GetTempPath(), "SkuMaster-status-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var settings = new AppSettings();
            settings.Rules.ReplacementStatus = "Old custom value";
            settings.Export.Format = "xlsx";
            settings.Export.OnlyChanged = true;
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(settings));
            var loaded = new SkuMaster.Infrastructure.SettingsStore(path).Load();
            Assert.NotNull(loaded.Warning);
            Assert.Equal("Новинка", loaded.Settings.Rules.ReplacementStatus);
            Assert.Equal("xlsx", loaded.Settings.Export.Format);
            Assert.True(loaded.Settings.Export.OnlyChanged);
            Assert.Throws<ArgumentException>(() => new SkuMaster.Infrastructure.SettingsStore(path).Save(settings));
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void ManualEditingRejectsUnknownStatusWithoutChangingResult()
    {
        var result = new StatusOperation().Execute(new(["sku", "status"], [new[] { "001", "" }]), new HashSet<string> { "001" }, new());
        var review = new ResultReview(result, "sku", "status", "Временно недоступен");
        Assert.Throws<ArgumentException>(() => review.SetStatus(2, "Произвольный статус"));
        Assert.Equal("", review.Result.Output.Rows[0][1]);
        Assert.Equal(0, review.Result.Changed);
    }

    [Theory]
    [InlineData("Снят с производства")]
    [InlineData("Топ продаж")]
    [InlineData("Лучшая цена")]
    [InlineData("Скоро в продаже")]
    [InlineData("Новинка")]
    [InlineData("Уценка")]
    [InlineData("%")]
    [InlineData("Спецпредложение")]
    [InlineData("Временно недоступен")]
    [InlineData("")]
    public void EveryListedStatusAndEmptyValueCanBeAssigned(string status)
    {
        var result = new StatusOperation().Execute(new(["sku", "status"], [new[] { "001", "Новинка" }]), new HashSet<string> { "001" }, new());
        var review = new ResultReview(result, "sku", "status", "Временно недоступен");
        review.SetStatus(2, status);
        Assert.Equal(status, review.Result.Output.Rows[0][1]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RulesCannotIntroduceUnknownStatuses(int field)
    {
        var rules = new RuleSettings { ClearFoundStatus = false };
        if (field == 0) rules.UnavailableStatus = "Unknown";
        if (field == 1) rules.MatchStatus = "Unknown";
        if (field == 2) rules.ReplacementStatus = "Unknown";
        Assert.Throws<ArgumentException>(() => new StatusOperation().Execute(new(["sku", "status"], []), new HashSet<string>(), rules));
    }
}
