using System.Xml.Linq;
using Xunit;

namespace EasyDownloaderYT.Tests;

public sealed class LocalizationResourceTests
{
    [Fact]
    public void LanguageDictionaries_ContainTheSameKeys()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Resources", "Languages");
        HashSet<string>? expected = null;

        foreach (string file in Directory.EnumerateFiles(directory, "Strings.*.xaml"))
        {
            XDocument document = XDocument.Load(file);
            XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
            var keys = document.Descendants()
                .Attributes(xaml + "Key")
                .Select(attribute => attribute.Value)
                .ToHashSet(StringComparer.Ordinal);

            if (expected is null)
                expected = keys;
            else
                Assert.True(
                    expected.SetEquals(keys),
                    $"{Path.GetFileName(file)} does not have the same localization keys as the other dictionaries.");
        }

        Assert.NotNull(expected);
    }
}
