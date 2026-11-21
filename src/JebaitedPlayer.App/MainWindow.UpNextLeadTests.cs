using JebaitedPlayer.ViewModels;

namespace JebaitedPlayer;

/// <summary>Dijagnostika (--diag-ui-test upnextlead): sljedeÄ‡a epizoda se nudi zadani broj sekundi prije kraja.</summary>
public partial class MainWindow
{
    private async Task<Dictionary<string, object?>> UpNextLeadUiTestAsync()
    {
        var r = new Dictionary<string, object?>();
        await WaitForAsync(() => _vm.HasMedia && !_vm.IsLoading && _vm.Duration > 0 && _vm.Library.Current != null, 10000);
        LibraryController lib = _vm.Library;
        _player?.SetPropertyAsync("loop-file", "no");
        _vm.SetPaused(false);
        lib.UpNextLeadSeconds = 5;
        lib.AutoPlayNext = true;
        double d = _vm.Duration;
        r["duration"] = Math.Round(d, 1);
        r["playlist"] = lib.Playlist.Count;

        _vm.SeekAbsolute(d - 9, exact: true);
        await Task.Delay(1500);
        r["farFromEnd"] = new Dictionary<string, object?> { ["card"] = lib.UpNext?.Label, ["remaining"] = Math.Round(_vm.Duration - _vm.Position, 1) };

        _vm.SeekAbsolute(d - 4.5, exact: true);
        await Task.Delay(1200);
        r["nearEnd"] = new Dictionary<string, object?> { ["card"] = lib.UpNext?.Label, ["seconds"] = lib.UpNextSeconds, ["remaining"] = Math.Round(_vm.Duration - _vm.Position, 1) };

        _vm.SeekAbsolute(1, exact: true);
        await Task.Delay(1200);
        r["seekedBack"] = new Dictionary<string, object?> { ["card"] = lib.UpNext?.Label };

        _vm.SeekAbsolute(d - 4.5, exact: true);
        await Task.Delay(1000);
        bool shownAgain = lib.UpNext != null;
        string first = lib.Current?.Label ?? string.Empty;
        await WaitForAsync(() => lib.Current is { } c && c.Label != first && _vm.Duration > 0 && !_vm.IsLoading, 12000);
        r["autoNext"] = new Dictionary<string, object?> { ["shownAgain"] = shownAgain, ["first"] = first, ["now"] = lib.Current?.Label };

        lib.UpNextLeadSeconds = 0;
        _vm.SeekAbsolute(_vm.Duration - 4.5, exact: true);
        await Task.Delay(1200);
        r["leadZero"] = new Dictionary<string, object?> { ["card"] = lib.UpNext?.Label, ["remaining"] = Math.Round(_vm.Duration - _vm.Position, 1) };
        return r;
    }
}

