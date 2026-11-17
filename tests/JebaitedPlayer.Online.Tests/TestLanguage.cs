namespace JebaitedPlayer.Tests;

[TestClass]
public static class TestLanguage
{
    /// <summary>Testovi provjeravaju hrvatske tekstove (izvorni jezik poruka), pa se jezik sučelja postavlja na hrvatski.</summary>
    [AssemblyInitialize]
    public static void Init(TestContext context) => JebaitedPlayer.Localization.Loc.SetLanguage("hr");
}
