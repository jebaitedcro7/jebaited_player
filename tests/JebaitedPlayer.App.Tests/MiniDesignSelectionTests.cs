using System.Text.Json;
using JebaitedPlayer.Infrastructure;

namespace JebaitedPlayer.App.Tests;

/// <summary>2.3.15: odabir izgleda mini playera (Postavke i desni klik koriste istu metodu); spremanje prolazi kroz JSON.</summary>
[TestClass]
public class MiniDesignSelectionTests
{
    [TestMethod]
    public void All_five_designs_can_be_selected_and_survive_serialization()
    {
        Assert.AreEqual(5, MiniPlayerSettings.Designs.Count);
        foreach (string id in MiniPlayerSettings.Designs)
        {
            var m = new MiniPlayerSettings();
            Assert.IsTrue(m.SelectDesign(id), id);
            Assert.AreEqual(id, m.ActiveDesign, id);

            var copy = JsonSerializer.Deserialize<MiniPlayerSettings>(JsonSerializer.Serialize(m))!;
            Assert.AreEqual(id, copy.ActiveDesign, "izgled mora ostati spremljen: " + id);
        }
    }

    [TestMethod]
    public void Strip_is_compact_and_switching_back_clears_it()
    {
        var m = new MiniPlayerSettings();
        Assert.IsTrue(m.SelectDesign("strip"));
        Assert.IsTrue(m.Compact);
        Assert.AreEqual("strip", m.ActiveDesign);

        Assert.IsTrue(m.SelectDesign("card"));
        Assert.IsFalse(m.Compact);
        Assert.AreEqual("card", m.ActiveDesign);
    }

    [TestMethod]
    public void Side_panel_shows_playlist_and_unknown_design_changes_nothing()
    {
        var m = new MiniPlayerSettings();
        Assert.IsTrue(m.SelectDesign("side"));
        Assert.IsTrue(m.ShowPlaylist);

        Assert.IsFalse(m.SelectDesign("neposotojeci"));
        Assert.AreEqual("side", m.ActiveDesign);
    }
}
