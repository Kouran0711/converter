using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using System.Numerics;

namespace NithConverter.Views;

public sealed partial class SplashView : UserControl
{
    private readonly bool _animate = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
    private CompositionScopedBatch? _exitBatch;
    private Visual? _logoVisual;
    private Visual? _ringVisual;
    private Visual? _rootVisual;
    public event EventHandler? Finished;

    public SplashView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        Loaded -= OnLoaded;
        if (!_animate) return;

        _logoVisual = ElementCompositionPreview.GetElementVisual(LogoHolder);
        _logoVisual.CenterPoint = new((float)LogoHolder.ActualWidth / 2, (float)LogoHolder.ActualHeight / 2, 0);
        _logoVisual.Opacity = 0;

        using var scale = _logoVisual.Compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0, new Vector3(0.72f, 0.72f, 1));
        scale.InsertKeyFrame(0.65f, new Vector3(1.04f, 1.04f, 1));
        scale.InsertKeyFrame(1, Vector3.One);
        scale.Duration = TimeSpan.FromMilliseconds(700);

        using var opacity = _logoVisual.Compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(0, 0);
        opacity.InsertKeyFrame(0.35f, 1);
        opacity.InsertKeyFrame(1, 1);
        opacity.Duration = scale.Duration;

        using var rotation = _logoVisual.Compositor.CreateScalarKeyFrameAnimation();
        rotation.InsertKeyFrame(0, -8);
        rotation.InsertKeyFrame(1, 0);
        rotation.Duration = scale.Duration;

        _logoVisual.StartAnimation("Scale", scale);
        _logoVisual.StartAnimation("Opacity", opacity);
        _logoVisual.StartAnimation("RotationAngleInDegrees", rotation);

        _ringVisual = ElementCompositionPreview.GetElementVisual(Orbit);
        _ringVisual.CenterPoint = new((float)Orbit.ActualWidth / 2, (float)Orbit.ActualHeight / 2, 0);
        using var ring = _ringVisual.Compositor.CreateScalarKeyFrameAnimation();
        ring.InsertKeyFrame(0, 0);
        ring.InsertKeyFrame(1, 360);
        ring.Duration = TimeSpan.FromMilliseconds(2200);
        ring.IterationBehavior = AnimationIterationBehavior.Forever;
        _ringVisual.StartAnimation("RotationAngleInDegrees", ring);
    }

    public void Dismiss()
    {
        IsHitTestVisible = false;
        if (!_animate) { Finished?.Invoke(this, EventArgs.Empty); return; }
        _rootVisual = ElementCompositionPreview.GetElementVisual(this);
        _exitBatch = _rootVisual.Compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        _exitBatch.Completed += OnExitCompleted;
        using var fade = _rootVisual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 1);
        fade.InsertKeyFrame(1, 0);
        fade.Duration = TimeSpan.FromMilliseconds(350);
        _rootVisual.StartAnimation("Opacity", fade);
        _exitBatch.End();
    }

    private void OnExitCompleted(object sender, CompositionBatchCompletedEventArgs args)
    {
        ReleaseAnimations();
        Finished?.Invoke(this, EventArgs.Empty);
    }

    private void OnUnloaded(object sender, RoutedEventArgs args) => ReleaseAnimations();

    private void ReleaseAnimations()
    {
        if (_exitBatch is not null) { _exitBatch.Completed -= OnExitCompleted; _exitBatch.Dispose(); _exitBatch = null; }
        _logoVisual?.StopAnimation("Scale");
        _logoVisual?.StopAnimation("Opacity");
        _logoVisual?.StopAnimation("RotationAngleInDegrees");
        _ringVisual?.StopAnimation("RotationAngleInDegrees");
        _rootVisual?.StopAnimation("Opacity");
        _logoVisual = null; _ringVisual = null; _rootVisual = null;
    }
}
