using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Shapes;
using TrayRandom.Dice;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace TrayRandom.Controls;

/// <summary>
/// A tray the dice are thrown into. Each die flies in from one side, bounces twice, skips, and
/// spins down to rest, its shadow tracking it on the table; all of that runs as composition
/// keyframe animations, so the UI thread only swaps the tumbling faces. Kept dice settle in
/// place, dropped ones dim and shrink, and a top roll flashes a gold ring. Decorative: the total
/// and the working stay in the text beside it.
/// </summary>
public sealed partial class DiceTray : Grid
{
    private const int Frames = 36;
    private const float ClipRadius = 7;
    private const float DroppedOpacity = 0.4f;
    private const float DroppedScale = 0.82f;
    private const float MoreTextRoom = 24;

    private static readonly TimeSpan FaceInterval = TimeSpan.FromMilliseconds(60);

    private readonly Canvas _canvas = new();
    private readonly TextBlock _moreText;
    private readonly DispatcherQueueTimer _faceTimer;
    private readonly Stopwatch _clock = new();
    private readonly Compositor _compositor;
    private readonly CompositionRoundedRectangleGeometry _clipShape;
    private readonly List<Landing> _landings = [];
    private IReadOnlyList<TrayDie> _dice = [];
    private bool? _pendingAnimate;

    public DiceTray()
    {
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        Children.Add(_canvas);

        _moreText = new TextBlock
        {
            Margin = new Thickness(10, 6, 10, 6),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            IsHitTestVisible = false,
        };
        Children.Add(_moreText);

        // Dice fly in from outside, so clip them to the card's rounded corners.
        _compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        _clipShape = _compositor.CreateRoundedRectangleGeometry();
        _clipShape.CornerRadius = new Vector2(ClipRadius);
        ElementCompositionPreview.GetElementVisual(_canvas).Clip = _compositor.CreateGeometricClip(_clipShape);

        _faceTimer = DispatcherQueue.CreateTimer();
        _faceTimer.Interval = FaceInterval;
        _faceTimer.Tick += (_, _) => OnFaceTick();

        SizeChanged += OnSizeChanged;
        Tapped += (_, e) =>
        {
            e.Handled = true;
            RollRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>The dice have all come to rest, or the roll was cut short.</summary>
    public event EventHandler? Settled;

    /// <summary>The tray was clicked: roll again.</summary>
    public event EventHandler? RollRequested;

    /// <summary>A throw is in the air, or waiting for the tray's first layout to start.</summary>
    public bool IsRolling => _faceTimer.IsRunning || _pendingAnimate == true;

    /// <summary>Throws <paramref name="dice"/> into the tray, or lays them at rest when not animating.</summary>
    /// <param name="moreCount">Dice in the roll beyond the ones drawn, shown as a count.</param>
    public void Show(IReadOnlyList<TrayDie> dice, int moreCount, bool animate)
    {
        Halt();
        _dice = dice;
        _moreText.Text = moreCount > 0 ? $"+{moreCount} more" : string.Empty;

        // The text under the tray just changed and may have taken some of its height.
        UpdateLayout();
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            // Just made visible; SizeChanged throws once the tray has a size.
            _pendingAnimate = animate;
            return;
        }

        Render(animate);
    }

    /// <summary>Lands every die now and stops the face timer. The flyout calls this when it hides.</summary>
    public void Stop()
    {
        bool rolling = IsRolling;
        Halt();
        if (rolling)
        {
            Settled?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Empties the tray.</summary>
    public void Clear()
    {
        Stop();
        _dice = [];
        _canvas.Children.Clear();
        _moreText.Text = string.Empty;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _clipShape.Size = new Vector2((float)e.NewSize.Width, (float)e.NewSize.Height);
        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0)
        {
            return;
        }

        if (_pendingAnimate is { } animate)
        {
            _pendingAnimate = null;
            Render(animate);
        }
        else if (!IsRolling && _dice.Count > 0)
        {
            Render(animate: false);
        }
    }

    private void Halt()
    {
        _pendingAnimate = null;
        _faceTimer.Stop();
        _clock.Reset();
        foreach (Landing landing in _landings)
        {
            landing.View.ShowFace(landing.View.Die.Value);
        }

        _landings.Clear();
    }

    private void Render(bool animate)
    {
        Halt();
        _canvas.Children.Clear();
        if (_dice.Count == 0)
        {
            return;
        }

        // Read the accent color when the dice are drawn, so a change in Windows shows up next roll.
        DiceArt art = new();
        Background = art.Spotlight;

        // Leave the bottom strip to the "+N more" count when there is one.
        float width = (float)ActualWidth;
        float height = (float)ActualHeight - (_moreText.Text.Length > 0 ? MoreTextRoom : 0);
        TrayArrangement layout = DiceTrayLayout.Arrange(_dice.Count, width, height, Random.Shared);
        float size = layout.DieSize;
        ThrowSide side = (ThrowSide)Random.Shared.Next(4);

        List<UIElement> shadows = [], glows = [], dice = [];
        for (int i = 0; i < _dice.Count; i++)
        {
            TrayDie die = _dice[i];
            DiePlacement place = layout.Placements[i];

            Ellipse shadow = new() { Width = size * 0.95, Height = size * 0.38, Fill = art.Shadow, IsHitTestVisible = false };
            Place(shadow, place.Center + new Vector2(0, size * 0.42f));
            shadows.Add(shadow);

            Ellipse? glow = null;
            if (die.IsCritical)
            {
                glow = new Ellipse { Width = size * 1.3, Height = size * 1.3, Fill = art.Glow, Stroke = art.GlowEdge, StrokeThickness = Math.Max(1.5, size / 18), IsHitTestVisible = false };
                Place(glow, place.Center);
                glows.Add(glow);
            }

            DieView view = new(die, size, art);
            Place(view, place.Center);
            dice.Add(view);

            Visual visual = ElementCompositionPreview.GetElementVisual(view);
            visual.CenterPoint = new Vector3(size / 2, size / 2, 0);
            ElementCompositionPreview.GetElementVisual(view.Body).CenterPoint = new Vector3(size / 2, size / 2, 0);
            if (glow is not null)
            {
                Visual glowVisual = ElementCompositionPreview.GetElementVisual(glow);
                glowVisual.CenterPoint = new Vector3((float)glow.Width / 2, (float)glow.Height / 2, 0);
                glowVisual.Opacity = 0;
            }

            if (animate)
            {
                Throw(view, shadow, glow, place, i, side, width, height, size);
            }
            else
            {
                visual.RotationAngleInDegrees = place.Rotation;
                if (!die.Kept)
                {
                    Visual body = ElementCompositionPreview.GetElementVisual(view.Body);
                    body.Opacity = DroppedOpacity;
                    body.Scale = new Vector3(DroppedScale, DroppedScale, 1);
                }
            }
        }

        foreach (UIElement element in shadows.Concat(glows).Concat(dice))
        {
            _canvas.Children.Add(element);
        }

        if (animate)
        {
            _clock.Restart();
            _faceTimer.Start();
        }
    }

    private void Throw(DieView view, Ellipse shadow, Ellipse? glow, DiePlacement place, int index, ThrowSide side, float width, float height, float size)
    {
        Random random = Random.Shared;
        TimeSpan delay = TimeSpan.FromMilliseconds(Math.Min(index * 35, 500) + random.Next(60));
        TimeSpan duration = TimeSpan.FromMilliseconds(1000 + random.Next(250));
        Vector2[] path = DiceTrayLayout.ThrowPath(place.Center, side, width, height, size, random);
        float spin = (random.Next(2) == 0 ? -1 : 1) * (420 + random.Next(480));
        TumbleFrame[] frames = DiceTumble.Sample(path, place.Rotation, spin, size, Frames);

        ElementCompositionPreview.SetIsTranslationEnabled(view, true);
        ElementCompositionPreview.SetIsTranslationEnabled(shadow, true);
        Visual visual = ElementCompositionPreview.GetElementVisual(view);
        Visual shadowVisual = ElementCompositionPreview.GetElementVisual(shadow);
        shadowVisual.CenterPoint = new Vector3((float)shadow.Width / 2, (float)shadow.Height / 2, 0);

        CompositionEasingFunction linear = _compositor.CreateLinearEasingFunction();
        Vector3KeyFrameAnimation translation = Keyframes(_compositor.CreateVector3KeyFrameAnimation(), duration, delay);
        Vector3KeyFrameAnimation scale = Keyframes(_compositor.CreateVector3KeyFrameAnimation(), duration, delay);
        ScalarKeyFrameAnimation rotation = Keyframes(_compositor.CreateScalarKeyFrameAnimation(), duration, delay);
        Vector3KeyFrameAnimation shadowTranslation = Keyframes(_compositor.CreateVector3KeyFrameAnimation(), duration, delay);
        Vector3KeyFrameAnimation shadowScale = Keyframes(_compositor.CreateVector3KeyFrameAnimation(), duration, delay);
        ScalarKeyFrameAnimation shadowOpacity = Keyframes(_compositor.CreateScalarKeyFrameAnimation(), duration, delay);
        foreach (TumbleFrame f in frames)
        {
            translation.InsertKeyFrame(f.Progress, new Vector3(f.Offset, 0), linear);
            scale.InsertKeyFrame(f.Progress, new Vector3(f.Scale, 1), linear);
            rotation.InsertKeyFrame(f.Progress, f.Rotation, linear);
            shadowTranslation.InsertKeyFrame(f.Progress, new Vector3(f.ShadowOffset, 0), linear);
            shadowScale.InsertKeyFrame(f.Progress, new Vector3(f.ShadowScale, f.ShadowScale, 1), linear);
            shadowOpacity.InsertKeyFrame(f.Progress, f.ShadowOpacity, linear);
        }

        visual.StartAnimation("Translation", translation);
        visual.StartAnimation(nameof(Visual.Scale), scale);
        visual.StartAnimation(nameof(Visual.RotationAngleInDegrees), rotation);
        shadowVisual.StartAnimation("Translation", shadowTranslation);
        shadowVisual.StartAnimation(nameof(Visual.Scale), shadowScale);
        shadowVisual.StartAnimation(nameof(Visual.Opacity), shadowOpacity);

        // After the last skip: dropped dice step back, a top roll pops and rings gold.
        TimeSpan landed = delay + (duration * 0.85);
        Visual body = ElementCompositionPreview.GetElementVisual(view.Body);
        if (!view.Die.Kept)
        {
            ScalarKeyFrameAnimation dim = Effect(_compositor.CreateScalarKeyFrameAnimation(), landed, 300);
            dim.InsertKeyFrame(0, 1);
            dim.InsertKeyFrame(1, DroppedOpacity);
            Vector3KeyFrameAnimation shrink = Effect(_compositor.CreateVector3KeyFrameAnimation(), landed, 300);
            shrink.InsertKeyFrame(0, Vector3.One);
            shrink.InsertKeyFrame(1, new Vector3(DroppedScale, DroppedScale, 1));
            body.StartAnimation(nameof(Visual.Opacity), dim);
            body.StartAnimation(nameof(Visual.Scale), shrink);
        }
        else if (glow is not null)
        {
            Vector3KeyFrameAnimation pop = Effect(_compositor.CreateVector3KeyFrameAnimation(), landed, 420);
            pop.InsertKeyFrame(0, Vector3.One);
            pop.InsertKeyFrame(0.35f, new Vector3(1.22f, 1.22f, 1));
            pop.InsertKeyFrame(1, Vector3.One);
            body.StartAnimation(nameof(Visual.Scale), pop);

            Visual glowVisual = ElementCompositionPreview.GetElementVisual(glow);
            Vector3KeyFrameAnimation ring = Effect(_compositor.CreateVector3KeyFrameAnimation(), landed, 750);
            ring.InsertKeyFrame(0, new Vector3(0.6f, 0.6f, 1));
            ring.InsertKeyFrame(1, new Vector3(1.9f, 1.9f, 1));
            ScalarKeyFrameAnimation flash = Effect(_compositor.CreateScalarKeyFrameAnimation(), landed, 750);
            flash.InsertKeyFrame(0, 0);
            flash.InsertKeyFrame(0.15f, 1);
            flash.InsertKeyFrame(1, 0);
            glowVisual.StartAnimation(nameof(Visual.Scale), ring);
            glowVisual.StartAnimation(nameof(Visual.Opacity), flash);
        }

        _landings.Add(new Landing(view, delay + (duration * DiceTumble.SecondBounce), delay + duration));
    }

    // Holds the first frame through the delay, so a die waiting its turn stays out of the tray.
    private static T Keyframes<T>(T animation, TimeSpan duration, TimeSpan delay)
        where T : KeyFrameAnimation
    {
        animation.Duration = duration;
        animation.DelayTime = delay;
        animation.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        return animation;
    }

    private static T Effect<T>(T animation, TimeSpan delay, int milliseconds)
        where T : KeyFrameAnimation
    {
        animation.Duration = TimeSpan.FromMilliseconds(milliseconds);
        animation.DelayTime = delay;
        return animation;
    }

    private static void Place(FrameworkElement element, Vector2 center)
    {
        Canvas.SetLeft(element, center.X - (element.Width / 2));
        Canvas.SetTop(element, center.Y - (element.Height / 2));
    }

    // While a die is in the air its face flickers through random values; once it lands it shows
    // the real one. Settled fires when the last die stops sliding.
    private void OnFaceTick()
    {
        TimeSpan now = _clock.Elapsed;
        bool moving = false;
        foreach (Landing landing in _landings)
        {
            if (now < landing.FaceAt)
            {
                landing.View.ShowFace(Random.Shared.Next(1, landing.View.Die.Sides + 1));
                moving = true;
            }
            else
            {
                landing.View.ShowFace(landing.View.Die.Value);
                moving |= now < landing.RestAt;
            }
        }

        if (!moving)
        {
            Stop();
        }
    }

    private sealed record Landing(DieView View, TimeSpan FaceAt, TimeSpan RestAt);
}
