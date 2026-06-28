using System.Reflection;
using System.Text.RegularExpressions;
using SwiftClean.Helpers;
using Xunit;

namespace SwiftClean.Tests;

public class LocTests
{
    private static Dictionary<string, string> Dict(string field)
    {
        var fi = typeof(Loc).GetField(field, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(fi);
        return (Dictionary<string, string>)fi!.GetValue(null)!;
    }

    // Distinct numeric placeholder indices in a format string, ignoring specifiers ({0:N0} -> 0).
    private static HashSet<string> Placeholders(string s)
        => Regex.Matches(s, @"\{(\d+)(?::[^}]*)?\}")
                .Select(m => m.Groups[1].Value)
                .ToHashSet();

    [Fact]
    public void En_And_Ru_HaveIdenticalKeySets()
    {
        var en = Dict("En").Keys.ToHashSet();
        var ru = Dict("Ru").Keys.ToHashSet();

        var missingInRu = en.Except(ru).OrderBy(k => k).ToList();
        var missingInEn = ru.Except(en).OrderBy(k => k).ToList();

        Assert.True(missingInRu.Count == 0, "Keys present in En but missing in Ru: " + string.Join(", ", missingInRu));
        Assert.True(missingInEn.Count == 0, "Keys present in Ru but missing in En: " + string.Join(", ", missingInEn));
    }

    [Fact]
    public void En_And_Ru_HaveMatchingPlaceholders()
    {
        var en = Dict("En");
        var ru = Dict("Ru");
        var mismatches = new List<string>();

        foreach (var (key, enValue) in en)
        {
            if (!ru.TryGetValue(key, out var ruValue))
                continue; // covered by the key-parity test
            if (!Placeholders(enValue).SetEquals(Placeholders(ruValue)))
                mismatches.Add(key);
        }

        Assert.True(mismatches.Count == 0, "Format-placeholder mismatch between En/Ru for keys: " + string.Join(", ", mismatches));
    }

    [Fact]
    public void Indexer_ReturnsKey_WhenMissing()
        => Assert.Equal("no.such.key", Loc.Instance["no.such.key"]);

    [Fact]
    public void SetLanguage_SwitchesValueAndFlags_AndFiresEvent()
    {
        var loc = Loc.Instance;
        var original = loc.Language;
        try
        {
            loc.SetLanguage("en");

            var fired = false;
            loc.LanguageChanged += Handler;
            void Handler() => fired = true;

            loc.SetLanguage("ru");

            Assert.True(fired);
            Assert.Equal("ru", loc.Language);
            Assert.True(loc.IsRussian);
            Assert.False(loc.IsEnglish);
            Assert.Equal("Реестр", loc["nav.registry"]);

            loc.LanguageChanged -= Handler;
        }
        finally
        {
            Loc.Instance.SetLanguage(original);
        }
    }

    [Fact]
    public void SetLanguage_UnknownValue_FallsBackToEnglish()
    {
        var loc = Loc.Instance;
        var original = loc.Language;
        try
        {
            loc.SetLanguage("fr");
            Assert.Equal("en", loc.Language);
            Assert.True(loc.IsEnglish);
        }
        finally
        {
            Loc.Instance.SetLanguage(original);
        }
    }
}
