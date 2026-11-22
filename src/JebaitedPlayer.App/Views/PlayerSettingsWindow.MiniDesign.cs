using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using JebaitedPlayer.Infrastructure;

namespace JebaitedPlayer.Views;

/// <summary>
/// 2.3.15: Postavke → Izgled → Mini player → Izgled. Svih pet izgleda s pregledom (shematski crtež u pravim omjerima),
/// odabir se odmah vidi na mini playeru (bez ponovnog pokretanja) i sprema se kao i ostale postavke.
/// Isti odabir postoji i desnim klikom na mini player; oba putovanja idu kroz <see cref="MiniPlayerSettings.SelectDesign"/>.
/// </summary>
public partial class PlayerSettingsWindow
{
    /// <summary>Izvorne veličine izgleda (px) iz docs/MINI_PLAYER_DIZAJNI.md; pregled ih crta u istom omjeru.</summary>
    private static readonly IReadOnlyDictionary<string, (double W, double H)> MiniDesignSizes = new Dictionary<string, (double, double)>
    {
        ["classic"] = (460, 116),
        ["card"] = (360, 150),
        ["strip"] = (560, 44),
        ["side"] = (250, 480),
        ["ufo"] = (340, 170),
    };

    private void LoadMiniDesign()
    {
        MiniDesignPanel.Children.Clear();
        string current = _settings.MiniPlayer.ActiveDesign;
        foreach (string id in MiniPlayerSettings.Designs)
        {
            MiniDesignPanel.Children.Add(MiniDesignCard(id, id == current));
        }
    }

    private FrameworkElement MiniDesignCard(string id, bool selected)
    {
        string name = Loc.T("miniDesign." + id);
        var label = new TextBlock
        {
            Text = name,
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, selected ? "AccentBrush" : "TextPrimaryBrush");

        var stack = new StackPanel { Width = 132 };
        stack.Children.Add(new Border { Height = 100, Child = new Viewbox { Stretch = Stretch.Uniform, Child = MiniDesignSketch(id) } });
        stack.Children.Add(label);

        var button = new Button
        {
            Style = (Style)FindResource("SecondaryButton"),
            Content = stack,
            Padding = new Thickness(8),
            Name = "MiniDesign_" + id,
            ToolTip = name,
        };
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => SetMiniDesign(id);

        var frame = new Border
        {
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 4, 8, 4),
            Child = button,
        };
        if (selected)
        {
            frame.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        }
        else
        {
            frame.BorderBrush = Brushes.Transparent;
        }

        return frame;
    }

    private void SetMiniDesign(string id)
    {
        if (_loading || !_settings.MiniPlayer.SelectDesign(id))
        {
            return;
        }

        LoadMiniDesign();
        _host.ApplyMiniPlayerSettings(); // otvoreni mini player odmah mijenja izgled
        UpdateSaveState();
    }

    /// <summary>Dijagnostika: odabir izgleda kao klik na karticu.</summary>
    internal void SelectMiniDesignForTests(string id) => SetMiniDesign(id);

    /// <summary>Dijagnostika: pomakni popis tako da su kartice izgleda vidljive (za snimku).</summary>
    internal void ShowMiniDesignCardsForTests() => MiniDesignPanel.BringIntoView();

    /// <summary>Dijagnostika: kartice izgleda (id → odabran).</summary>
    internal IReadOnlyList<(string Id, bool Selected)> MiniDesignCards => MiniDesignPanel.Children.OfType<Border>()
        .Select(b => (((Button)b.Child).Name["MiniDesign_".Length..], b.BorderBrush != Brushes.Transparent)).ToList();

    // --- shematski crteži -------------------------------------------------------------------------------------------

    private Canvas MiniDesignSketch(string id)
    {
        (double w, double h) = MiniDesignSizes[id];
        var canvas = new Canvas { Width = w, Height = h };
        Panel(canvas, 0, 0, w, h, "PanelBackgroundBrush", radius: 10, border: true);
        switch (id)
        {
            case "classic":
                Panel(canvas, 8, 28, 124, 52, "AccentBrush", 4, opacity: 0.35);   // LCD s vremenom i VU
                Panel(canvas, 140, 28, 312, 26, "AccentBrush", 4, opacity: 0.35); // naslov
                Dots(canvas, 142, 78, 6, 22, 10);
                Line(canvas, 306, 78, 146, 8);
                Line(canvas, 8, 100, 444, 6);
                break;
            case "card":
                Panel(canvas, 12, 12, 78, 78, "AccentBrush", 6, opacity: 0.35);   // omot
                Line(canvas, 104, 18, 220, 12);
                Line(canvas, 104, 42, 150, 8);
                Panel(canvas, 290, 36, 56, 28, "AccentBrush", 14);                 // play
                Line(canvas, 12, 104, 336, 6);
                Dots(canvas, 16, 124, 5, 28, 12);
                break;
            case "strip":
                Dots(canvas, 10, 22, 4, 26, 11);
                Line(canvas, 138, 17, 230, 10);
                Line(canvas, 380, 17, 80, 10);
                Line(canvas, 10, 38, 540, 4);
                break;
            case "side":
                Panel(canvas, 25, 18, 200, 200, "AccentBrush", 8, opacity: 0.35);  // veliki omot
                Line(canvas, 25, 232, 160, 12);
                Line(canvas, 25, 256, 100, 8);
                Line(canvas, 25, 278, 200, 6);
                Dots(canvas, 30, 300, 5, 30, 14);
                for (int i = 0; i < 5; i++)
                {
                    Line(canvas, 25, 350 + (i * 24), 200 - (i % 2 * 50), 8, opacity: 0.4);
                }

                break;
            default: // ufo
                Panel(canvas, 14, 14, 312, 82, "AccentBrush", 16, opacity: 0.35);  // zaobljeni LCD
                Panel(canvas, 24, 106, 52, 52, "AccentBrush", 26);                 // okrugla tipka
                Line(canvas, 92, 128, 230, 6);                                       // „snop” = napredak
                Dots(canvas, 92, 140, 4, 22, 12);
                break;
        }

        return canvas;
    }

    private static void Panel(Canvas c, double x, double y, double w, double h, string brush, double radius, double opacity = 1, bool border = false)
    {
        var r = new Rectangle { Width = w, Height = h, RadiusX = radius, RadiusY = radius, Opacity = opacity };
        r.SetResourceReference(Shape.FillProperty, brush);
        if (border)
        {
            r.SetResourceReference(Shape.StrokeProperty, "MenuBorderBrush");
            r.StrokeThickness = 3;
        }

        Canvas.SetLeft(r, x);
        Canvas.SetTop(r, y);
        c.Children.Add(r);
    }

    private static void Line(Canvas c, double x, double y, double w, double h, double opacity = 0.7)
    {
        var r = new Rectangle { Width = w, Height = h, RadiusX = h / 2, RadiusY = h / 2, Opacity = opacity };
        r.SetResourceReference(Shape.FillProperty, "TextSecondaryBrush");
        Canvas.SetLeft(r, x);
        Canvas.SetTop(r, y);
        c.Children.Add(r);
    }

    private static void Dots(Canvas c, double x, double y, int count, double step, double size)
    {
        for (int i = 0; i < count; i++)
        {
            var e = new Ellipse { Width = size, Height = size, Opacity = 0.8 };
            e.SetResourceReference(Shape.FillProperty, "TextSecondaryBrush");
            Canvas.SetLeft(e, x + (i * step));
            Canvas.SetTop(e, y);
            c.Children.Add(e);
        }
    }
}
