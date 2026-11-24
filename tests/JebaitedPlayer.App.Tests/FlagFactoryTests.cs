using JebaitedPlayer.Views;

namespace JebaitedPlayer.App.Tests;

[TestClass]
public class FlagFactoryTests
{
    [DataTestMethod]
    [DataRow("Hrvatska", "hr")]
    [DataRow("Bosna i Hercegovina", "ba")]
    [DataRow("Srbija", "rs")]
    [DataRow("Crna Gora", "me")]
    [DataRow("Germany Sport", "de")]
    [DataRow("Turkay", "tr")]
    [DataRow("UK", "gb")]
    [DataRow("USA", "us")]
    [DataRow("Italia", "it")]
    [DataRow("Hungarian", "hu")]
    public void Recognizes_country_in_category_name(string name, string code) => Assert.AreEqual(code, FlagFactory.CodeFor(name));

    [DataTestMethod]
    [DataRow("Sport")]
    [DataRow("Film")]
    [DataRow("Muzika")]
    [DataRow("World Sport")]
    [DataRow("Pink Media")]
    public void Topic_categories_have_no_country(string name) => Assert.IsNull(FlagFactory.CodeFor(name));

    [TestMethod]
    public void Topic_emoji_for_generic_categories()
    {
        Assert.IsNotNull(FlagFactory.EmojiFor("Sport"));
        Assert.IsNotNull(FlagFactory.EmojiFor("Film"));
        Assert.IsNotNull(FlagFactory.EmojiFor("World Sport"));
        Assert.IsNull(FlagFactory.EmojiFor("Pink Media"));
    }

    [TestMethod]
    public void Every_known_flag_draws_and_is_frozen()
    {
        foreach (string code in new[] { "hr", "rs", "si", "ba", "mk", "me", "al", "at", "de", "fr", "it", "ch", "se", "dk", "no", "fi", "nl", "be", "lu", "gb", "us", "tr", "hu", "pl", "cz", "sk", "bg", "ro", "es", "pt", "gr", "ru", "ua", "ie", "ca", "jp", "cn" })
        {
            var img = FlagFactory.ForCode(code);
            Assert.IsNotNull(img, code);
            Assert.IsTrue(img!.IsFrozen, code);
        }

        Assert.IsNull(FlagFactory.ForCode("zz"));
    }
}
