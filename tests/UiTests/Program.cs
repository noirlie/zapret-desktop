using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ZapretDesktop;
class Test {
 [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
 static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int size);
 [STAThread] static void Main(string[] args) {
 Directory.CreateDirectory("artifacts"); AppContext.SetSwitch("ZapretDesktop.TestMode",true); var app=new Application(); SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
 var w=new MainWindow { WindowStartupLocation=WindowStartupLocation.Manual,Left=-10000,Top=-10000,ShowInTaskbar=false };
 w.Show(); Pump(200);
 ((TextBlock)w.FindName("Progress")).Text="Движок не запущен"; // Neutral screenshot state; tests never start a service.
 w.UpdateLayout();Pump(80);
 var homeImage=new RenderTargetBitmap((int)((FrameworkElement)w.Content).ActualWidth,(int)((FrameworkElement)w.Content).ActualHeight,96,96,PixelFormats.Pbgra32);homeImage.Render((Visual)w.Content);
 var homeEncoder=new PngBitmapEncoder();homeEncoder.Frames.Add(BitmapFrame.Create(homeImage));using(var file=File.Create(Path.Combine("artifacts","home.png")))homeEncoder.Save(file);

 var windowSource=System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(w).Handle);
 if(windowSource?.CompositionTarget?.RenderMode!=System.Windows.Interop.RenderMode.SoftwareOnly)throw new Exception("Window not using fallback renderer");
 if(OperatingSystem.IsWindowsVersionAtLeast(10,0,22000)){
  var handle=new System.Windows.Interop.WindowInteropHelper(w).Handle;
  if(DwmGetWindowAttribute(handle,33,out var corner,sizeof(int))!=0||corner!=2)throw new Exception("Windows rounded window preference not active");
 }
 for(var reopen=0;reopen<5;reopen++){
  w.Hide();Pump(30);typeof(MainWindow).GetMethod("ShowFromTray",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(w,null);Pump(80);
  if(!w.IsVisible||((FrameworkElement)w.Content).ActualWidth<100)throw new Exception("Restore lost window content");
 }
 Console.WriteLine("PASS: software renderer and five hide/restore cycles");
 var power=(Button)w.FindName("Power");
 if(power.FocusVisualStyle==null) throw new Exception("Keyboard focus indicator missing");
 if(power.Template.FindName("FocusRing",power) is not null) throw new Exception("Unwanted outer ring remains"); if(w.Icon is null)throw new Exception("Window icon missing"); if(System.Windows.Shell.WindowChrome.GetWindowChrome(w) is null)throw new Exception("Custom chrome missing");
 var key=(DependencyPropertyKey)typeof(ButtonBase).GetField("IsPressedPropertyKey",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
 power.SetValue(key,true); Pump(160);
 var surface=(FrameworkElement)power.Template.FindName("PowerSurface",power);
 if(((ScaleTransform)surface.RenderTransform).ScaleX > .98) throw new Exception("Press animation did not shrink");
 power.SetValue(key,false); Pump(330);
 if(Math.Abs(((ScaleTransform)surface.RenderTransform).ScaleX-1)>.01) throw new Exception("Release animation did not restore");
 Console.WriteLine("PASS: no outer ring, custom icon/chrome, press and release animations");
 foreach(var name in new[]{"Diagnostics","Domains","Updates","Settings","Home"}) {
 var nav=(RadioButton)w.FindName("Nav"+name); nav.IsChecked=true; nav.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump(380);
 var panel=(FrameworkElement)w.FindName(name);
 if(panel.Visibility!=Visibility.Visible || panel.Opacity<.99) throw new Exception("Navigation failed: "+name);
 }
 foreach(var name in new[]{"Diagnostics","Settings","Updates","Home"}) { var nav=(RadioButton)w.FindName("Nav"+name); nav.IsChecked=true; nav.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump(15); }
 Pump(450);
 if(((FrameworkElement)w.FindName("Home")).Visibility!=Visibility.Visible) throw new Exception("Rapid navigation failed");

 var fixtureFlags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
 typeof(MainWindow).GetMethod("SetDomainDocument",fixtureFlags)!.Invoke(w,new object[]{new DomainDocument("example.com\napi.example.com\n","excluded.example\n","fixture",true)});
 var domainNav=(RadioButton)w.FindName("NavDomains");domainNav.IsChecked=true;domainNav.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));Pump(400);
 var editor=(TextBox)w.FindName("DomainEditor");editor.AppendText("new.example\n");Pump(80);
 if(!((Button)w.FindName("SaveDomainsButton")).IsEnabled)throw new Exception("Dirty domain save disabled");
 var excludeTab=(RadioButton)w.FindName("ExcludedTab");excludeTab.IsChecked=true;excludeTab.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));Pump(80);
 if(editor.Text!="excluded.example\n")throw new Exception("Excluded draft not restored");
 var includeTab=(RadioButton)w.FindName("IncludedTab");includeTab.IsChecked=true;includeTab.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));Pump(80);
 if(!editor.Text.Contains("new.example"))throw new Exception("Draft lost on tab switch");
 editor.Text="https://invalid.example";Pump(80);if(!((TextBlock)w.FindName("DomainNote")).Text.Contains("Строка"))throw new Exception("Invalid domain not identified");
 typeof(MainWindow).GetMethod("SetDomainDocument",fixtureFlags)!.Invoke(w,new object[]{new DomainDocument("example.com\napi.example.com\n","excluded.example\n","fixture",true)});
 w.UpdateLayout();var domainImage=new RenderTargetBitmap((int)((FrameworkElement)w.Content).ActualWidth,(int)((FrameworkElement)w.Content).ActualHeight,96,96,PixelFormats.Pbgra32);domainImage.Render((Visual)w.Content);var domainEncoder=new PngBitmapEncoder();domainEncoder.Frames.Add(BitmapFrame.Create(domainImage));using(var f=File.Create("artifacts/domains.png"))domainEncoder.Save(f);

 var includedBounds=includeTab.TransformToAncestor(w).Transform(new Point(0,0));var excludedBounds=excludeTab.TransformToAncestor(w).Transform(new Point(0,0));
 if(Math.Abs(includedBounds.Y-excludedBounds.Y)>.5||Math.Abs(includeTab.ActualHeight-excludeTab.ActualHeight)>.5)throw new Exception("Domain tabs not aligned");
 var searchBox=(TextBox)w.FindName("DomainSearch");searchBox.Focus();Pump(100);
 if(((TextBlock)w.FindName("DomainSearchPlaceholder")).Visibility!=Visibility.Collapsed)throw new Exception("Search placeholder overlaps caret");
 var searchHost=(FrameworkElement)searchBox.Template.FindName("PART_ContentHost",searchBox);var hostOrigin=searchHost.TransformToAncestor(searchBox).Transform(new Point(0,0));
 if(Math.Abs(hostOrigin.X-13)>.5)throw new Exception("Search text inset differs from placeholder");
 if(searchBox.ActualHeight!=44||searchBox.CaretIndex!=0)throw new Exception("Search dimensions/caret incorrect");
 searchBox.Text="example";Pump(40);if(((TextBlock)w.FindName("DomainSearchPlaceholder")).Visibility!=Visibility.Collapsed)throw new Exception("Placeholder overlaps text");
 searchBox.Clear();editor.Focus();Pump(80);if(((TextBlock)w.FindName("DomainSearchPlaceholder")).Visibility!=Visibility.Visible)throw new Exception("Placeholder not restored");
 Console.WriteLine("PASS: aligned domain tabs and focus-aware search placeholder");
 Console.WriteLine("PASS: domain drafts, tabs, dirty state, validation and native render");
 Console.WriteLine("PASS: all tabs and rapid navigation"); var settingsNav=(RadioButton)w.FindName("NavSettings");settingsNav.IsChecked=true;settingsNav.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));Pump(400);w.UpdateLayout();
 var startTray=(CheckBox)w.FindName("StartInTray"); startTray.ApplyTemplate();
 if(startTray.Template.FindName("Knob",startTray) is not FrameworkElement) throw new Exception("Custom toggle checkbox template missing");
 ((Expander)w.FindName("AdvancedSettings")).IsExpanded=true; w.UpdateLayout();
 var settingsScroll=(ScrollViewer)w.FindName("Settings"); settingsScroll.ScrollToEnd(); Pump(80); if(settingsScroll.VerticalOffset<=0) throw new Exception("Settings scrollbar did not scroll"); var bottomImage=new RenderTargetBitmap((int)((FrameworkElement)w.Content).ActualWidth,(int)((FrameworkElement)w.Content).ActualHeight,96,96,PixelFormats.Pbgra32); bottomImage.Render((Visual)w.Content); var bottomEncoder=new PngBitmapEncoder();bottomEncoder.Frames.Add(BitmapFrame.Create(bottomImage)); using(var fs=File.Create(Path.Combine("artifacts", "zapret-settings-bottom-ui.png")))bottomEncoder.Save(fs); settingsScroll.ScrollToHome(); Pump(80);
 ((Expander)w.FindName("AdvancedSettings")).IsExpanded=false;
 var autoMode=(CheckBox)w.FindName("AutoMode");
 var savedMode=autoMode.IsChecked;autoMode.IsChecked=false;Pump(280);
 if(((FrameworkElement)w.FindName("ManualStrategyPanel")).Visibility!=Visibility.Visible)throw new Exception("Manual strategy remains hidden");
 autoMode.IsChecked=true;Pump(280);
 if(((FrameworkElement)w.FindName("ManualStrategyPanel")).Visibility!=Visibility.Collapsed)throw new Exception("Manual strategy remains visible in auto mode");
 if(((Button)w.FindName("InstallServiceButton")).IsVisible)throw new Exception("Technical service controls exposed by default");
 autoMode.IsChecked=savedMode;w.UpdateLayout();
 Console.WriteLine("PASS: minimal menu and conditional manual strategy");
 var settingsImage=new RenderTargetBitmap((int)((FrameworkElement)w.Content).ActualWidth,(int)((FrameworkElement)w.Content).ActualHeight,96,96,PixelFormats.Pbgra32); settingsImage.Render((Visual)w.Content);
 var settingsEncoder=new PngBitmapEncoder();settingsEncoder.Frames.Add(BitmapFrame.Create(settingsImage));
 using(var fs=File.Create(Path.Combine("artifacts", "zapret-settings-ui.png")))settingsEncoder.Save(fs);
 // Standalone control: exercise the production template without changing saved settings.
 var toggle = new CheckBox { Style = startTray.Style, Template = startTray.Template };
 var testWindow = new Window { Content = toggle, Left = -10000, Top = -10000, ShowInTaskbar = false };
 testWindow.Show(); toggle.ApplyTemplate(); Pump(260);
 var knob = (FrameworkElement)toggle.Template.FindName("Knob", toggle);
 toggle.Focus(); Pump(40);
 var toggleBox = (Border)toggle.Template.FindName("Box", toggle);
 if(toggleBox.BorderThickness != new Thickness(0)) throw new Exception("OFF toggle has an outline");
 toggle.IsChecked = true; Pump(65);
 if(toggleBox.BorderThickness != new Thickness(0)) throw new Exception("Animating toggle has an outline");
 var middle = ((TranslateTransform)knob.RenderTransform).X;
 if(middle <= 0 || middle >= 16) throw new Exception("Toggle jumps instead of animating");
 toggle.IsChecked = false; Pump(280);
 if(Math.Abs(((TranslateTransform)knob.RenderTransform).X) > .01) throw new Exception("Interrupted toggle did not return");
 toggle.IsChecked = true; Pump(280);
 if(Math.Abs(((TranslateTransform)knob.RenderTransform).X - 16) > .01) throw new Exception("Toggle did not reach ON");
 if(toggleBox.BorderThickness != new Thickness(0)) throw new Exception("ON toggle has an outline");
 toggle.IsChecked = false; Pump(280);
 if(toggleBox.BorderThickness != new Thickness(0)) throw new Exception("OFF toggle retains an outline after click");
 testWindow.Close();
 Console.WriteLine("PASS: toggle interpolation and interrupted animation");
 Console.WriteLine("PASS: settings controls styled and scroll works");
 w.UpdateLayout();
 var content=(FrameworkElement)w.Content;
 var image=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32); image.Render(content);
 var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
 using(var fs=File.Create(Path.Combine("artifacts", "zapret-native-ui.png"))) encoder.Save(fs);
 var updatesNav=(RadioButton)w.FindName("NavUpdates");updatesNav.IsChecked=true;updatesNav.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));Pump(400);w.UpdateLayout();
 if(((Button)w.FindName("InstallComponentsButton")).IsEnabled)throw new Exception("Unprepared package installation enabled");
 if(w.FindName("RollbackComponentsButton") is not Button)throw new Exception("Rollback button missing");
 if(args.Contains("--updates-live")){
  foreach(var buttonName in new[]{"UpdateButton","DownloadUpdateButton"}){
   var button=(Button)w.FindName(buttonName);
   if(!button.IsEnabled)throw new Exception(buttonName+" disabled before live test");
   button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
   for(int i=0;!button.IsEnabled&&i<240;i++)Pump(250);
   if(!button.IsEnabled)throw new Exception("UI request timed out");
   var status=((TextBlock)w.FindName("UpdateStatus")).Text;
   Console.WriteLine("LIVE UI "+status);
   if(status.Contains("Не удалось")||status.Contains("не подготовлен")||status.Contains("отменена"))throw new Exception(status);
  }
  if(!((Button)w.FindName("InstallComponentsButton")).IsEnabled)throw new Exception("Verified package not installable");
 }
 var updatesImage=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);updatesImage.Render(content);
 var updatesEncoder=new PngBitmapEncoder();updatesEncoder.Frames.Add(BitmapFrame.Create(updatesImage));
 using(var fs=File.Create(Path.Combine("artifacts", "zapret-updates-ui.png")))updatesEncoder.Save(fs);
 Console.WriteLine("PASS: update install/rollback controls and native render");
 ((Button)w.FindName("CloseButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump(200);
 if(w.IsVisible)throw new Exception("Tray hide failed");
 var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
 var tray=typeof(MainWindow).GetField("tray",flags)!.GetValue(w)!;
 if(!(bool)tray.GetType().GetProperty("Visible")!.GetValue(tray)!)throw new Exception("Tray icon missing");
 var popup=(Popup)typeof(MainWindow).GetField("trayPopup",flags)!.GetValue(w)!;
 if(!popup.AllowsTransparency||popup.Child is not Border trayCard||trayCard.CornerRadius.TopLeft<10)throw new Exception("Smooth tray surface missing");
 typeof(MainWindow).GetMethod("ShowTrayPopup",flags)!.Invoke(w,null);Pump(100);
 if(!popup.IsOpen)throw new Exception("Tray menu does not open while main window is hidden");
 popup.IsOpen=false;
 trayCard.Measure(new System.Windows.Size(double.PositiveInfinity,double.PositiveInfinity));
 trayCard.Arrange(new Rect(new System.Windows.Point(0,0),trayCard.DesiredSize));
 var trayImage=new RenderTargetBitmap((int)Math.Ceiling(trayCard.DesiredSize.Width),(int)Math.Ceiling(trayCard.DesiredSize.Height),96,96,PixelFormats.Pbgra32);
 trayImage.Render(trayCard);
 var trayEncoder=new PngBitmapEncoder();trayEncoder.Frames.Add(BitmapFrame.Create(trayImage));
 using(var output=File.Create(Path.Combine("artifacts","tray-menu-qa.png")))trayEncoder.Save(output);
 var pixels=new byte[trayImage.PixelWidth*trayImage.PixelHeight*4];trayImage.CopyPixels(pixels,trayImage.PixelWidth*4,0);
 if(!pixels.Where((_,index)=>index%4==3).Any(alpha=>alpha is >0 and <255))throw new Exception("Tray corners have no alpha smoothing");
 typeof(MainWindow).GetMethod("ShowFromTray",flags)!.Invoke(w,null);Pump(100);
 if(!w.IsVisible)throw new Exception("Tray restore failed");
 typeof(MainWindow).GetField("exitRequested",flags)!.SetValue(w,true);w.Close();Pump(100);
 Console.WriteLine("PASS: per-pixel tray corners, close hides, reopen works, explicit close disposes");
 Console.WriteLine("PASS: rendered native UI; no engine started");
 }
 static void Pump(int ms) { var frame=new DispatcherFrame(); var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(ms)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame); }
}
