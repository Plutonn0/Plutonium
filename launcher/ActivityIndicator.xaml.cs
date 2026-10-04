using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PlutoniumLauncher;

public partial class ActivityIndicator : UserControl
{
    public static readonly DependencyProperty IsRunningProperty = DependencyProperty.Register(nameof(IsRunning), typeof(bool), typeof(ActivityIndicator), new PropertyMetadata(true, Changed));
    public bool IsRunning { get => (bool)GetValue(IsRunningProperty); set => SetValue(IsRunningProperty, value); }
    public ActivityIndicator()
    {
        InitializeComponent(); Loaded += (_, _) => Animate(); Unloaded += (_, _) => Stop(); IsVisibleChanged += (_, _) => Animate();
    }
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((ActivityIndicator)sender).Animate();
    private void Stop() => Rotor?.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, null);
    private void Animate()
    {
        if (Rotor is null) return;
        if (!IsLoaded || !IsVisible || !IsRunning || !SystemParameters.ClientAreaAnimation) { Stop(); return; }
        Rotor.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever });
    }
}
