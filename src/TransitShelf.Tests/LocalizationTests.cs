using System.Text;
using TransitShelf.Core;
using TransitShelf.Core.Localization;
using TransitShelf.Core.Persistence;
using TransitShelf.Core.Settings;

namespace TransitShelf.Tests;

public class LocalizationTests
{
    [Theory]
    [InlineData("auto", "zh-CN", "zh-CN")]
    [InlineData("auto", "zh-TW", "zh-CN")]
    [InlineData("auto", "en-US", "en")]
    [InlineData("auto", "fr-FR", "en")]
    [InlineData("zh-CN", "en-US", "zh-CN")]
    [InlineData("en", "zh-CN", "en")]
    [InlineData("invalid", "en-US", "en")]
    public void LanguageResolutionIsExplicit(string preference, string systemLanguage, string expected)
        => Assert.Equal(expected, LanguagePreference.Resolve(preference, systemLanguage));

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"items\":[]}")]
    [InlineData("{\"schemaVersion\":2,\"settings\":{},\"items\":[]}")]
    [InlineData("{\"schemaVersion\":3,\"SETTINGS\":{},\"items\":[]}")]
    public void ExistingStateWithoutLanguageKeepsChinese(string json)
        => Assert.Equal(LanguagePreference.Chinese, ShelfStateCodec.DeserializeSnapshot(json).Settings.Language);

    [Theory]
    [InlineData("auto")]
    [InlineData("zh-CN")]
    [InlineData("en")]
    public void LanguageChoiceSurvivesSerialization(string language)
    {
        var json = ShelfStateCodec.Serialize([], new ShelfSettings { Language = language });
        Assert.Equal(language, ShelfStateCodec.DeserializeSnapshot(json).Settings.Language);
    }

    [Fact]
    public void NewInstallationFollowsSystem() => Assert.Equal("auto", new ShelfSettings().Language);

    [Fact]
    public void EveryMessageHasEnglishAndCompatiblePlaceholders()
    {
        Assert.True(Loc.Catalog.Count >= 275);
        Assert.Equal(Loc.Catalog.Count, Loc.Catalog.Select(e => e.Id).Distinct().Count());
        foreach (var entry in Loc.Catalog)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.En), entry.Id);
            Assert.False(entry.En.Any(c => c >= 0x3400 && c <= 0x9fff), entry.Id);
            Assert.Equal(CompositeFormat.Parse(entry.Zh).MinimumArgumentCount, CompositeFormat.Parse(entry.En).MinimumArgumentCount);
            Assert.Equal(entry.En, Loc.TextFor("en", entry.Id));
            Assert.Equal(entry.Zh, Loc.TextFor("zh-CN", entry.Id));
        }
    }

    [Fact]
    public void SuggestedNameUsesExplicitLanguageAndKeepsParentName()
    {
        var date = new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
        var english = NameSuggester.Suggest(ItemKind.File, "123456789012.pdf", @"C:\Users\<user>\Downloads\123456789012.pdf", date, "en");
        Assert.Equal("Document 2026-11-08.pdf", english?.Name);
        var originalParent = NameSuggester.Suggest(ItemKind.File, "123456789012.pdf", @"C:\项目资料\123456789012.pdf", date, "en");
        Assert.Equal("项目资料 2026-11-08.pdf", originalParent?.Name);
        Assert.DoesNotContain("名称", originalParent!.Reason);
    }

    [Fact]
    public void EnglishSettingDoesNotRewriteUserNamesOrText()
    {
        var item = new ShelfItem { Id = "fixture", Kind = ItemKind.Text, DisplayName = "我的名称", Text = "用户原文", CreatedAt = DateTimeOffset.Now, GroupId = "group" };
        var group = new ShelfGroup { Id = "group", Name = "我的分组", CreatedAt = DateTimeOffset.Now };
        var json = ShelfStateCodec.SerializeSnapshot([item], [group], new ShelfSettings { Language = "en" });
        var saved = ShelfStateCodec.DeserializeSnapshot(json);
        Assert.Equal("我的名称", saved.Items[0].DisplayName);
        Assert.Equal("用户原文", saved.Items[0].Text);
        Assert.Equal("我的分组", saved.Groups[0].Name);
    }
}
