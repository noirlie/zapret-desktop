using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace ZapretDesktop;
public partial class MainWindow
{
    string currentPage = "Home";
    int transitionVersion;
    void Navigate(object sender, RoutedEventArgs e)
    {
        var page = (string)((RadioButton)sender).Tag;
        if (page == currentPage) return;
        currentPage = page;
        if(page=="Domains"&&!domainsLoaded)_=LoadDomains();
        var version = ++transitionVersion;
        var panels = new FrameworkElement[] { Home, Diagnostics, Updates, Settings, Domains };
        var incoming = panels.Single(p => p.Name == page);
        foreach (var panel in panels)
        {
            panel.IsHitTestVisible = panel == incoming;
            if (!SystemParameters.ClientAreaAnimation)
            {
                panel.BeginAnimation(OpacityProperty, null);
                panel.Opacity = 1;
                panel.RenderTransform = new TranslateTransform();
                panel.Visibility = panel == incoming ? Visibility.Visible : Visibility.Collapsed;
                continue;
            }
            if (panel != incoming && panel.Visibility != Visibility.Visible) continue;
            var entering = panel == incoming;
            var wasHidden = panel.Visibility != Visibility.Visible;
            var opacity = wasHidden ? 0 : panel.Opacity;
            var transform = panel.RenderTransform as TranslateTransform;
            if (transform is null) panel.RenderTransform = transform = new TranslateTransform();
            var y = wasHidden ? 6 : transform.Y;
            panel.Visibility = Visibility.Visible;
            Panel.SetZIndex(panel, entering ? 1 : 0);
            var fade = new DoubleAnimation(opacity, entering ? 1 : 0, TimeSpan.FromMilliseconds(entering ? 260 : 160))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            fade.Completed += (_, _) =>
            {
                if (version != transitionVersion || closing) return;
                panel.BeginAnimation(OpacityProperty, null);
                panel.Opacity = 1;
                panel.Visibility = entering ? Visibility.Visible : Visibility.Collapsed;
            };
            panel.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
            transform.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(y, entering ? 0 : -3, TimeSpan.FromMilliseconds(260))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }, HandoffBehavior.SnapshotAndReplace);
        }
    }
}
