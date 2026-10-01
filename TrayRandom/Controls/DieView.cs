using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TrayRandom.Dice;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.ViewManagement;

namespace TrayRandom.Controls;

/// <summary>
/// The brushes the dice tray paints with, made once per tray. Dice in the user's accent color;
/// subtracted dice (the d4 in 2d6-d4) in a warm orange so they read as taking away.
/// </summary>
internal sealed class DiceArt
{
    public DiceArt()
    {
        UISettings ui = new();
        Accent = new DiePaint(ui.GetColorValue(UIColorType.AccentLight1), ui.GetColorValue(UIColorType.AccentDark1), ui.GetColorValue(UIColorType.AccentDark2), ui.GetColorValue(UIColorType.AccentDark3));
        Subtracted = new DiePaint(Color.FromArgb(255, 0xF7, 0xA0, 0x72), Color.FromArgb(255, 0xD9, 0x48, 0x0F), Color.FromArgb(255, 0xA0, 0x36, 0x0B), Color.FromArgb(255, 0x6F, 0x25, 0x07));
        Color accent = ui.GetColorValue(UIColorType.Accent);
        Spotlight = Radial(new Point(0.5, 0.42), 0.75, Color.FromArgb(0x30, accent.R, accent.G, accent.B), Color.FromArgb(0, accent.R, accent.G, accent.B));
    }

    public DiePaint Accent { get; }

    public DiePaint Subtracted { get; }

    /// <summary>The soft accent pool of light the dice land in.</summary>
    public Brush Spotlight { get; }

    public Brush Facet { get; } = new SolidColorBrush(Color.FromArgb(0x55, 255, 255, 255));

    public Brush FaceFill { get; } = new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255));

    public Brush Sheen { get; } = Radial(new Point(0.5, 0.5), 0.5, Color.FromArgb(0x80, 255, 255, 255), Color.FromArgb(0, 255, 255, 255));

    public Brush Pip { get; } = Radial(new Point(0.4, 0.35), 0.6, Color.FromArgb(255, 255, 255, 255), Color.FromArgb(255, 0xD8, 0xDC, 0xE4));

    public Brush Shadow { get; } = Radial(new Point(0.5, 0.5), 0.5, Color.FromArgb(0x70, 0, 0, 0), Color.FromArgb(0, 0, 0, 0));

    public Brush Glow { get; } = Radial(new Point(0.5, 0.5), 0.5, Color.FromArgb(0, 0xFF, 0xC8, 0x3D), Color.FromArgb(0x90, 0xFF, 0xC8, 0x3D));

    public Brush GlowEdge { get; } = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xC8, 0x3D));

    private static RadialGradientBrush Radial(Point center, double radius, Color inner, Color outer)
    {
        RadialGradientBrush brush = new() { Center = center, GradientOrigin = center, RadiusX = radius, RadiusY = radius };
        brush.GradientStops.Add(new GradientStop { Color = inner, Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = outer, Offset = 1 });
        return brush;
    }
}

/// <summary>A die's body color: lit from the top left, darkening to the bottom right, with a dark edge.</summary>
internal sealed class DiePaint(Color light, Color body, Color dark, Color edge)
{
    public Brush Fill { get; } = new LinearGradientBrush
    {
        StartPoint = new Point(0.2, 0),
        EndPoint = new Point(0.8, 1),
        GradientStops =
        {
            new GradientStop { Color = light, Offset = 0 },
            new GradientStop { Color = body, Offset = 0.5 },
            new GradientStop { Color = dark, Offset = 1 },
        },
    };

    public Brush Edge { get; } = new SolidColorBrush(edge);
}

/// <summary>
/// One die drawn in XAML, shaped by its number of sides the way the real polyhedra look face on:
/// a triangle for a d4, a pipped cube for a d6, a d20's triangle face inside its hexagon outline.
/// The outer element carries the throw; <see cref="Body"/> carries effects after landing.
/// </summary>
internal sealed partial class DieView : Grid
{
    // Pip positions on a d6, in reading order; each face lists which of them show.
    private static readonly Point[] PipSpots = [new(0.28, 0.28), new(0.72, 0.28), new(0.28, 0.5), new(0.5, 0.5), new(0.72, 0.5), new(0.28, 0.72), new(0.72, 0.72)];
    private static readonly string[] PipFaces = ["0001000", "0100010", "0101010", "1100011", "1101011", "1110111"];

    private readonly float _size;
    private readonly TextBlock? _number;
    private readonly Ellipse[]? _pips;
    private readonly double _textScale = 1;

    public DieView(TrayDie die, float size, DiceArt art)
    {
        Die = die;
        _size = size;
        Width = size;
        Height = size;
        IsHitTestVisible = false;

        Body = new Grid { Width = size, Height = size };
        Children.Add(Body);

        DiePaint paint = die.Subtracted ? art.Subtracted : art.Accent;
        double stroke = Math.Max(1, size / 36);
        Point sheenAt = new(0.42, 0.26);

        if (die.Sides == 6)
        {
            Body.Children.Add(new Rectangle { Fill = paint.Fill, Stroke = paint.Edge, StrokeThickness = stroke, RadiusX = size * 0.2, RadiusY = size * 0.2, Margin = new Thickness(size * 0.04) });
            Body.Children.Add(new Rectangle { Stroke = art.Facet, StrokeThickness = stroke * 0.8, RadiusX = size * 0.14, RadiusY = size * 0.14, Margin = new Thickness(size * 0.12) });
            AddSheen(art, sheenAt);
            _pips = new Ellipse[PipSpots.Length];
            double pip = size * 0.16;
            for (int i = 0; i < PipSpots.Length; i++)
            {
                _pips[i] = new Ellipse
                {
                    Width = pip,
                    Height = pip,
                    Fill = art.Pip,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness((PipSpots[i].X * size) - (pip / 2), (PipSpots[i].Y * size) - (pip / 2), 0, 0),
                };
                Body.Children.Add(_pips[i]);
            }
        }
        else
        {
            (Point[]? outer, Point[] face, (int Face, int Outer)[] links, double textY, double textScale) = Shape(die.Sides);
            _textScale = textScale;
            if (outer is null)
            {
                Body.Children.Add(new Ellipse { Fill = paint.Fill, Stroke = paint.Edge, StrokeThickness = stroke, Margin = new Thickness(size * 0.03) });
                Body.Children.Add(new Ellipse { Fill = art.FaceFill, Stroke = art.Facet, StrokeThickness = stroke * 0.8, Margin = new Thickness(size * 0.16) });
            }
            else
            {
                Body.Children.Add(new Polygon { Points = Scaled(outer), Fill = paint.Fill, Stroke = paint.Edge, StrokeThickness = stroke, StrokeLineJoin = PenLineJoin.Round });
                Body.Children.Add(new Polygon { Points = Scaled(face), Fill = art.FaceFill, Stroke = art.Facet, StrokeThickness = stroke * 0.8, StrokeLineJoin = PenLineJoin.Round });
                GeometryGroup lines = new();
                foreach ((int f, int o) in links)
                {
                    lines.Children.Add(new LineGeometry { StartPoint = ToPixels(face[f]), EndPoint = ToPixels(outer[o]) });
                }

                Body.Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Data = lines, Stroke = art.Facet, StrokeThickness = stroke * 0.8 });
                sheenAt = die.Sides == 4 ? new Point(0.5, 0.42) : new Point(0.45, 0.24);
            }

            AddSheen(art, sheenAt);
            _number = new TextBlock
            {
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,

                // Centered, a top margin moves the text down by half of it.
                Margin = new Thickness(0, (textY - 0.5) * 2 * size, 0, 0),
            };
            Body.Children.Add(_number);
        }

        ShowFace(die.Value);
    }

    public TrayDie Die { get; }

    /// <summary>The drawn die, inside the element that moves; dims or pulses once the die lands.</summary>
    public Grid Body { get; }

    public void ShowFace(int value)
    {
        if (_pips is not null)
        {
            string face = PipFaces[Math.Clamp(value, 1, 6) - 1];
            for (int i = 0; i < _pips.Length; i++)
            {
                _pips[i].Visibility = face[i] == '1' ? Visibility.Visible : Visibility.Collapsed;
            }

            return;
        }

        string text = value.ToString(CultureInfo.CurrentCulture);
        _number!.Text = text;
        _number.FontSize = _size * _textScale * text.Length switch { 1 => 0.42, 2 => 0.34, 3 => 0.26, _ => 0.2 };

        // A tumbled 6 and 9 look alike, so dice underline them.
        _number.TextDecorations = value is 6 or 9 ? TextDecorations.Underline : TextDecorations.None;
    }

    // Outline, the face pointing at the viewer, facet lines from face corners to outline corners,
    // where the number sits, and how big it may be. A null outline means a round die.
    private static (Point[]? Outer, Point[] Face, (int, int)[] Links, double TextY, double TextScale) Shape(int sides) => sides switch
    {
        4 => (Regular(3, 0.5, -90, 0.5, 0.6), Regular(3, 0.2, -90, 0.5, 0.6), [(0, 0), (1, 1), (2, 2)], 0.64, 0.7),
        8 => (Regular(4, 0.48, -90), Regular(3, 0.3, -90), [(0, 0), (1, 1), (2, 3), (1, 2), (2, 2)], 0.54, 0.8),
        10 or 100 => (
            [new(0.5, 0.02), new(0.97, 0.55), new(0.5, 0.98), new(0.03, 0.55)],
            [new(0.5, 0.2), new(0.77, 0.56), new(0.5, 0.79), new(0.23, 0.56)],
            [(0, 0), (1, 1), (2, 2), (3, 3)],
            0.53,
            0.82),
        12 => (Regular(10, 0.48, -90), Regular(5, 0.3, -90), [(0, 0), (1, 2), (2, 4), (3, 6), (4, 8)], 0.52, 0.85),
        20 => (Regular(6, 0.49, -90), Regular(3, 0.33, -90), [(0, 0), (1, 2), (2, 4), (0, 1), (1, 1), (1, 3), (2, 3), (2, 5), (0, 5)], 0.54, 0.78),
        _ => (null, [], [], 0.5, 0.9),
    };

    private static Point[] Regular(int corners, double radius, double startDegrees, double cx = 0.5, double cy = 0.5)
    {
        Point[] points = new Point[corners];
        for (int i = 0; i < corners; i++)
        {
            double angle = (startDegrees + (360.0 * i / corners)) * Math.PI / 180;
            points[i] = new Point(cx + (radius * Math.Cos(angle)), cy + (radius * Math.Sin(angle)));
        }

        return points;
    }

    private void AddSheen(DiceArt art, Point at) => Body.Children.Add(new Ellipse
    {
        Width = _size * 0.36,
        Height = _size * 0.18,
        Fill = art.Sheen,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness((at.X * _size) - (_size * 0.18), (at.Y * _size) - (_size * 0.09), 0, 0),
        RenderTransformOrigin = new Point(0.5, 0.5),
        RenderTransform = new RotateTransform { Angle = -24 },
    });

    private Point ToPixels(Point unit) => new(unit.X * _size, unit.Y * _size);

    private PointCollection Scaled(Point[] unit)
    {
        PointCollection points = [];
        foreach (Point p in unit)
        {
            points.Add(ToPixels(p));
        }

        return points;
    }
}
