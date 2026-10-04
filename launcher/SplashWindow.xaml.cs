using System.Windows;
namespace PlutoniumLauncher;
public partial class SplashWindow : Window
{
    public SplashWindow() => InitializeComponent();
    public void SetStatus(string text) => StatusText.Text = text;
    private void Close_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
