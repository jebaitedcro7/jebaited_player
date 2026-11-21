using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;

namespace JebaitedPlayer.Infrastructure;

/// <summary>
/// 2.0: tipka s modifikatorima (prečac). Zapis u postavkama je „Ctrl+Shift+D” (imena tipki kao u WPF-ovom Key),
/// a prikaz je čitljiv i prati raspored tipkovnice („Ctrl + Shift + D”, „←”, „Razmak”, „š”).
/// </summary>
public readonly record struct KeyChord(Key Key, ModifierKeys Modifiers)
{
    public static readonly KeyChord None = default;

    public bool IsEmpty => Key == Key.None;

    public static KeyChord Parse(string? text) => TryParse(text, out KeyChord chord) ? chord : None;

    public static bool TryParse(string? text, out KeyChord chord)
    {
        chord = None;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] parts = text.Split('+', StringSplitOptions.TrimEntries);
        ModifierKeys mods = ModifierKeys.None;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            ModifierKeys m = parts[i] switch
            {
                "Ctrl" => ModifierKeys.Control,
                "Alt" => ModifierKeys.Alt,
                "Shift" => ModifierKeys.Shift,
                _ => ModifierKeys.Windows, // nepoznato: odbaci
            };
            if (m == ModifierKeys.Windows || mods.HasFlag(m))
            {
                return false;
            }

            mods |= m;
        }

        string name = parts[^1];
        if (name.Length == 0 || !char.IsLetter(name[0]) || !Enum.TryParse(name, ignoreCase: false, out Key key)
            || key == Key.None || IsModifierKey(key))
        {
            return false;
        }

        chord = new KeyChord(key, mods);
        return true;
    }

    /// <summary>Same modifikatorske tipke (Ctrl, Alt, Shift, Windows) nisu prečac.</summary>
    public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System or Key.ImeProcessed or Key.DeadCharProcessed;

    /// <summary>Zapis za postavke (uvijek isti redoslijed: Ctrl, Alt, Shift).</summary>
    public string Serialize()
    {
        if (IsEmpty)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        if (Modifiers.HasFlag(ModifierKeys.Control))
        {
            sb.Append("Ctrl+");
        }

        if (Modifiers.HasFlag(ModifierKeys.Alt))
        {
            sb.Append("Alt+");
        }

        if (Modifiers.HasFlag(ModifierKeys.Shift))
        {
            sb.Append("Shift+");
        }

        return sb.Append(Key).ToString();
    }

    /// <summary>Čitljiv prikaz za izbornike, F1 i postavke.</summary>
    public string Display()
    {
        if (IsEmpty)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        if (Modifiers.HasFlag(ModifierKeys.Control))
        {
            sb.Append("Ctrl + ");
        }

        if (Modifiers.HasFlag(ModifierKeys.Alt))
        {
            sb.Append("Alt + ");
        }

        if (Modifiers.HasFlag(ModifierKeys.Shift))
        {
            sb.Append("Shift + ");
        }

        return sb.Append(KeyName(Key)).ToString();
    }

    public override string ToString() => Serialize();

    internal static string KeyName(Key key) => key switch
    {
        Key.Space => Loc.T("key.space"),
        Key.Left => "←",
        Key.Right => "→",
        Key.Up => "↑",
        Key.Down => "↓",
        Key.Back => "Backspace",
        Key.Delete => "Delete",
        Key.Insert => "Insert",
        Key.Home => "Home",
        Key.End => "End",
        Key.PageUp => "PgUp",
        Key.PageDown => "PgDn",
        Key.Add => "Num +",
        Key.Subtract => "Num −",
        Key.Multiply => "Num *",
        Key.Divide => "Num /",
        Key.Decimal => "Num ,",
        >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(System.Globalization.CultureInfo.InvariantCulture),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num " + ((int)key - (int)Key.NumPad0).ToString(System.Globalization.CultureInfo.InvariantCulture),
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.F1 and <= Key.F24 => key.ToString(),
        Key.OemMinus => "−",
        _ => LayoutCharacter(key) ?? key.ToString(),
    };

    /// <summary>Znak koji tipka daje na trenutnom rasporedu tipkovnice (za interpunkciju: „,” „.” „+” „š” …).</summary>
    private static string? LayoutCharacter(Key key)
    {
        int vk = KeyInterop.VirtualKeyFromKey(key);
        uint ch = MapVirtualKeyW((uint)vk, 2) & 0x7FFF; // MAPVK_VK_TO_CHAR (gornji bit = mrtva tipka)
        return ch is > 32 and < 0xFFFF ? char.ToUpperInvariant((char)ch).ToString() : null;
    }

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyW(uint code, uint mapType);
}

/// <param name="Id">Stalni ID radnje (u postavkama).</param>
/// <param name="NameKey">Ključ prijevoda naziva.</param>
/// <param name="Category">playback, subtitles, library ili windows.</param>
/// <param name="Defaults">Zadane tipke (najviše dvije).</param>
public sealed record ShortcutAction(string Id, string NameKey, string Category, KeyChord[] Defaults);

/// <summary>
/// 2.0: registar svih radnji s tipkovnice. Iz njega rade obrada tipki (MainWindow.HandleShortcut), F1, tekst tipki u
/// izbornicima i kartica Postavke → Prečaci. U postavkama se spremaju samo promjene (radnja → tipke), pa nove zadane
/// tipke u budućim verzijama vrijede i onima koji su nešto mijenjali.
/// </summary>
public static class Shortcuts
{
    public const int Slots = 2;

    public static readonly IReadOnlyList<string> Categories = ["playback", "subtitles", "library", "windows"];

    private const ModifierKeys Ctrl = ModifierKeys.Control;
    private const ModifierKeys Shift = ModifierKeys.Shift;

    private static KeyChord K(Key key, ModifierKeys mods = ModifierKeys.None) => new(key, mods);

    /// <summary>Sve radnje; zadane tipke su iste kao prije 2.0 (varijante s Ctrl/Shift su sada zasebne radnje).</summary>
    public static readonly IReadOnlyList<ShortcutAction> Actions =
    [
        new("playPause", "shortcut.playPause", "playback", [K(Key.Space), K(Key.K)]),
        new("seekBack", "shortcutAction.seekBack", "playback", [K(Key.Left)]),
        new("seekForward", "shortcutAction.seekForward", "playback", [K(Key.Right)]),
        new("seekBackLong", "shortcutAction.seekBackLong", "playback", [K(Key.Left, Ctrl)]),
        new("seekForwardLong", "shortcutAction.seekForwardLong", "playback", [K(Key.Right, Ctrl)]),
        new("seekStart", "shortcutAction.seekStart", "playback", [K(Key.Home)]),
        new("volumeUp", "shortcutAction.volumeUp", "playback", [K(Key.Up)]),
        new("volumeDown", "shortcutAction.volumeDown", "playback", [K(Key.Down)]),
        new("mute", "shortcut.mute", "playback", [K(Key.M)]),
        new("fullscreen", "menu.fullscreen", "playback", [K(Key.F), K(Key.F11)]),
        new("frameBack", "shortcutAction.frameBack", "playback", [K(Key.OemComma)]),
        new("frameForward", "shortcutAction.frameForward", "playback", [K(Key.OemPeriod)]),
        new("speedUp", "menu.faster", "playback", [K(Key.OemPlus), K(Key.Add)]),
        new("speedDown", "menu.slower", "playback", [K(Key.OemMinus), K(Key.Subtract)]),
        new("speedNormal", "shortcutAction.speedNormal", "playback", [K(Key.Back)]),
        new("nextAudio", "menu.nextAudio", "playback", [K(Key.B)]),
        new("aspect", "menu.nextAspect", "playback", [K(Key.O)]),
        new("screenshot", "shortcutAction.screenshot", "playback", [K(Key.S)]),
        new("screenshotPictureOnly", "shortcutAction.screenshotPictureOnly", "playback", [K(Key.S, Shift)]),

        new("loadSubtitles", "shortcutAction.loadSubtitles", "subtitles", [K(Key.T, Ctrl)]),
        new("searchSubtitles", "shortcutAction.searchSubtitles", "subtitles", [K(Key.F, Ctrl)]),
        new("toggleSubtitles", "shortcutAction.toggleSubtitles", "subtitles", [K(Key.V)]),
        new("nextSubtitles", "shortcutAction.nextSubtitles", "subtitles", [K(Key.J)]),
        new("subtitleEarlier", "shortcutAction.subtitleEarlier", "subtitles", [K(Key.G)]),
        new("subtitleLater", "shortcutAction.subtitleLater", "subtitles", [K(Key.H)]),
        new("subtitleEarlierFine", "shortcutAction.subtitleEarlierFine", "subtitles", [K(Key.G, Ctrl)]),
        new("subtitleLaterFine", "shortcutAction.subtitleLaterFine", "subtitles", [K(Key.H, Ctrl)]),
        new("subtitleEarlierCoarse", "shortcutAction.subtitleEarlierCoarse", "subtitles", [K(Key.G, Shift)]),
        new("subtitleLaterCoarse", "shortcutAction.subtitleLaterCoarse", "subtitles", [K(Key.H, Shift)]),
        new("markAudio", "shortcutAction.markAudio", "subtitles", [K(Key.Z)]),
        new("markSubtitle", "shortcutAction.markSubtitle", "subtitles", [K(Key.X)]),
        new("autoSync", "shortcut.autoSync", "subtitles", [K(Key.A)]),
        new("resetSync", "menu.resetSync", "subtitles", [K(Key.Back, Ctrl)]),

        new("nextEpisode", "menu.nextEpisode", "library", [K(Key.N)]),
        new("previousEpisode", "shortcutAction.previousEpisode", "library", [K(Key.P)]),
        new("playlist", "menu.playlist", "library", [K(Key.L)]),
        new("favorite", "shortcut.favorite", "library", [K(Key.D, Ctrl)]),
        new("openFile", "shortcutAction.openFile", "library", [K(Key.O, Ctrl)]),
        new("openFolder", "shortcutAction.openFolder", "library", [K(Key.O, Ctrl | Shift)]),

        new("miniPlayer", "menu.miniPlayer", "windows", [K(Key.M, Ctrl)]),
        new("radio", "radio.title", "windows", [K(Key.R, Ctrl)]),
        new("spotify", "spotify.title", "windows", [K(Key.S, Ctrl | Shift)]),
        new("youTube", "youtube.title", "windows", [K(Key.Y, Ctrl | Shift)]),
        new("discord", "discord.title", "windows", [K(Key.D, Ctrl | Shift)]),
        new("settings", "settings.title", "windows", []),
        new("help", "shortcut.help", "windows", [K(Key.F1)]),
    ];

    public static ShortcutAction? Action(string id) => Actions.FirstOrDefault(a => a.Id == id);

    public static string Name(ShortcutAction action) => Loc.T(action.NameKey);

    public static string CategoryName(string category) => category switch
    {
        "playback" => Loc.T("shortcutCategory.playback"),
        "subtitles" => Loc.T("shortcutCategory.subtitles"),
        "library" => Loc.T("shortcutCategory.library"),
        _ => Loc.T("shortcutCategory.windows"),
    };

    /// <summary>
    /// Tipke koje se ne mogu dodijeliti: imaju stalnu ulogu (Esc, Enter na karticama, Alt+Enter, Alt+F4) ili ih koristi
    /// Windows / tipkovnica (Tab, Windows, medijske tipke i glasnoća).
    /// </summary>
    public static bool IsReserved(KeyChord chord) =>
        chord.Key is Key.Escape or Key.Enter or Key.Tab or Key.LWin or Key.RWin or Key.Apps or Key.Snapshot or Key.Sleep
            or Key.MediaPlayPause or Key.MediaNextTrack or Key.MediaPreviousTrack or Key.MediaStop
            or Key.VolumeUp or Key.VolumeDown or Key.VolumeMute
        || (chord.Key == Key.F4 && chord.Modifiers.HasFlag(ModifierKeys.Alt))
        || (chord.Key == Key.Space && chord.Modifiers.HasFlag(ModifierKeys.Alt));

    private static KeyChord[] Defaults(string id)
    {
        var keys = new KeyChord[Slots];
        if (Action(id) is { } action)
        {
            for (int i = 0; i < Math.Min(Slots, action.Defaults.Length); i++)
            {
                keys[i] = action.Defaults[i];
            }
        }

        return keys;
    }

    /// <summary>Tipke radnje (promjena iz postavki ili zadane); uvijek dva mjesta, prazno = KeyChord.None.</summary>
    public static KeyChord[] Get(AppSettings settings, string id)
    {
        if (settings.Shortcuts.TryGetValue(id, out string[]? custom) && custom is not null)
        {
            var keys = new KeyChord[Slots];
            for (int i = 0; i < Math.Min(Slots, custom.Length); i++)
            {
                keys[i] = KeyChord.Parse(custom[i]);
            }

            return keys;
        }

        return Defaults(id);
    }

    public static bool IsDefault(AppSettings settings, string id) => !settings.Shortcuts.ContainsKey(id);

    /// <summary>Radnja kojoj pripada točno ta kombinacija; null ako nijednoj.</summary>
    public static string? Find(AppSettings settings, KeyChord chord)
    {
        if (chord.IsEmpty)
        {
            return null;
        }

        foreach (ShortcutAction action in Actions)
        {
            if (Array.IndexOf(Get(settings, action.Id), chord) >= 0)
            {
                return action.Id;
            }
        }

        return null;
    }

    /// <summary>
    /// Tipka za pritisnutu kombinaciju: točna, a ako nema točne i pritisnut je samo Shift, ista tipka bez Shifta
    /// (kao prije 2.0: npr. Shift+M i dalje utišava, „+” na engleskom rasporedu je Shift+=).
    /// </summary>
    public static string? Resolve(AppSettings settings, Key key, ModifierKeys mods) =>
        Find(settings, new KeyChord(key, mods))
        ?? (mods == ModifierKeys.Shift ? Find(settings, new KeyChord(key, ModifierKeys.None)) : null);

    /// <summary>
    /// Dodjeljuje tipku radnji na mjesto slot (0 ili 1). Ako tu tipku ima druga radnja, ondje se briše; vraća ID te radnje.
    /// Prazna tipka briše mjesto.
    /// </summary>
    public static string? Assign(AppSettings settings, string id, int slot, KeyChord chord)
    {
        string? taken = chord.IsEmpty ? null : TakeFromOthers(settings, id, chord);
        KeyChord[] mine = Get(settings, id);
        for (int i = 0; i < Slots; i++)
        {
            if (i != slot && !chord.IsEmpty && mine[i] == chord)
            {
                mine[i] = KeyChord.None;
            }
        }

        mine[Math.Clamp(slot, 0, Slots - 1)] = chord;
        Set(settings, id, mine);
        return taken;
    }

    /// <summary>Vraća zadane tipke radnje; ako ih je u međuvremenu dobila druga radnja, ondje se brišu.</summary>
    public static IReadOnlyList<string> Reset(AppSettings settings, string id)
    {
        settings.Shortcuts.Remove(id);
        var taken = new List<string>();
        foreach (KeyChord chord in Defaults(id).Where(c => !c.IsEmpty))
        {
            if (TakeFromOthers(settings, id, chord) is { } other)
            {
                taken.Add(other);
            }
        }

        return taken;
    }

    private static string? TakeFromOthers(AppSettings settings, string id, KeyChord chord)
    {
        string? taken = null;
        foreach (ShortcutAction other in Actions)
        {
            if (other.Id == id)
            {
                continue;
            }

            KeyChord[] keys = Get(settings, other.Id);
            int index = Array.IndexOf(keys, chord);
            if (index >= 0)
            {
                keys[index] = KeyChord.None;
                Set(settings, other.Id, keys);
                taken = other.Id;
            }
        }

        return taken;
    }

    /// <summary>Sprema tipke radnje; ako su jednake zadanima, promjena se briše.</summary>
    private static void Set(AppSettings settings, string id, KeyChord[] keys)
    {
        if (keys.SequenceEqual(Defaults(id)))
        {
            settings.Shortcuts.Remove(id);
        }
        else
        {
            settings.Shortcuts[id] = keys.Select(k => k.Serialize()).ToArray();
        }
    }

    /// <summary>
    /// Ručno uređena ili stara datoteka: nepoznate radnje i neispravne tipke se uklanjaju, višak mjesta se odbacuje,
    /// rezervirane tipke i dvostruke dodjele se brišu (ostaje prva radnja po redoslijedu).
    /// </summary>
    public static void Normalize(AppSettings settings)
    {
        foreach (string id in settings.Shortcuts.Keys.ToList())
        {
            if (Action(id) is null)
            {
                settings.Shortcuts.Remove(id);
                continue;
            }

            KeyChord[] keys = Get(settings, id).Select(k => IsReserved(k) ? KeyChord.None : k).ToArray();
            Set(settings, id, keys);
        }

        var seen = new HashSet<KeyChord>();
        foreach (ShortcutAction action in Actions)
        {
            KeyChord[] keys = Get(settings, action.Id);
            bool changed = false;
            for (int i = 0; i < Slots; i++)
            {
                if (!keys[i].IsEmpty && !seen.Add(keys[i]))
                {
                    keys[i] = KeyChord.None;
                    changed = true;
                }
            }

            if (changed)
            {
                Set(settings, action.Id, keys);
            }
        }
    }

    /// <summary>Tipke radnje za prikaz („Razmak / K”); prazno ako nema nijedne.</summary>
    public static string Display(AppSettings settings, string id, string separator = " / ") =>
        string.Join(separator, Get(settings, id).Where(k => !k.IsEmpty).Select(k => k.Display()));

    /// <summary>Prva tipka radnje za prikaz u izborniku (npr. „Ctrl + O”); prazno ako nema nijedne.</summary>
    public static string Gesture(AppSettings settings, string id) =>
        Get(settings, id).FirstOrDefault(k => !k.IsEmpty).Display();
}
