using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Interop;
using Forms=System.Windows.Forms;
namespace ZapretDesktop;
public partial class MainWindow {
 Forms.NotifyIcon? tray;
 System.Drawing.Icon? trayIcon;
 Popup? trayPopup;
 Button? trayConnect;
 bool exitRequested,trayExiting;
 void InitializeTray(){
  using var resource=Application.GetResourceStream(new Uri("pack://application:,,,/ZapretDesktop;component/Assets/zapret.ico"))!.Stream;
  using var loaded=new System.Drawing.Icon(resource);
  trayIcon=(System.Drawing.Icon)loaded.Clone();
  trayPopup=CreateTrayPopup();
  tray=new Forms.NotifyIcon{Icon=trayIcon,Text="zapret — открыть двойным щелчком",Visible=true};
  tray.MouseUp+=(_,e)=>{if(e.Button==Forms.MouseButtons.Right)Dispatcher.BeginInvoke(ShowTrayPopup);};
  tray.DoubleClick+=(_,_)=>Dispatcher.BeginInvoke(ShowFromTray);
  Closed+=(_,_)=>{trayPopup.IsOpen=false;tray.Visible=false;tray.Dispose();trayIcon.Dispose();};
 }
 Popup CreateTrayPopup(){
  var style=new Style(typeof(Button));
  var template=new ControlTemplate(typeof(Button));
  var surface=new FrameworkElementFactory(typeof(Border));surface.Name="Surface";
  surface.SetValue(Border.BackgroundProperty,Brushes.Transparent);
  surface.SetValue(Border.CornerRadiusProperty,new CornerRadius(7));
  var content=new FrameworkElementFactory(typeof(ContentPresenter));
  content.SetValue(HorizontalAlignmentProperty,HorizontalAlignment.Left);
  content.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);
  content.SetValue(MarginProperty,new Thickness(12,0,0,0));surface.AppendChild(content);
  template.VisualTree=surface;
  var hover=new Trigger{Property=IsMouseOverProperty,Value=true};
  hover.Setters.Add(new Setter(Border.BackgroundProperty,new SolidColorBrush(Color.FromRgb(243,244,246)),"Surface"));
  template.Triggers.Add(hover);
  var pressed=new Trigger{Property=Button.IsPressedProperty,Value=true};
  pressed.Setters.Add(new Setter(Border.BackgroundProperty,new SolidColorBrush(Color.FromRgb(232,233,235)),"Surface"));
  template.Triggers.Add(pressed);
  style.Setters.Add(new Setter(Button.TemplateProperty,template));
  var items=new StackPanel();
  Button Add(string title,Action action){
   var button=new Button{Content=title,Width=182,Height=34,HorizontalAlignment=HorizontalAlignment.Left,
    Background=Brushes.Transparent,Foreground=new SolidColorBrush(Color.FromRgb(22,22,22)),
    FontFamily=new FontFamily("Segoe UI"),FontSize=14,BorderThickness=new Thickness(0),Style=style,Cursor=System.Windows.Input.Cursors.Hand};
   button.Click+=(_,_)=>{trayPopup!.IsOpen=false;action();};items.Children.Add(button);return button;
  }
  Add("Открыть",ShowFromTray);
  trayConnect=Add("Подключить",()=>ToggleEngine(this,new RoutedEventArgs()));
  items.Children.Add(new Border{Height=1,Margin=new Thickness(12,4,12,4),Background=new SolidColorBrush(Color.FromRgb(233,233,235))});
  Add("Выйти",ExitFromTray);
  var card=new Border{Background=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(226,226,229)),
   BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(11),Padding=new Thickness(5),
   Margin=new Thickness(10),Child=items,
   Effect=new DropShadowEffect{Color=Colors.Black,BlurRadius=14,ShadowDepth=3,Opacity=.16}};
  return new Popup{AllowsTransparency=true,StaysOpen=false,PopupAnimation=PopupAnimation.Fade,
   Placement=PlacementMode.AbsolutePoint,Child=card};
 }
 void ShowTrayPopup(){
  if(trayPopup is null||trayConnect is null)return;
  trayConnect.Content=operation is not null||(engine.State.Busy||engine.State.RecoveryPending)?"Отменить подбор":connected?"Отключить":"Подключить";
  trayConnect.IsEnabled=!trayExiting;
  var cursor=Forms.Cursor.Position;
  var area=Forms.Screen.FromPoint(cursor).WorkingArea;
  var source=PresentationSource.FromVisual(this);
  var transform=source?.CompositionTarget?.TransformFromDevice??Matrix.Identity;
  var anchor=transform.Transform(new Point(cursor.X,cursor.Y));
  var topLeft=transform.Transform(new Point(area.Left,area.Top));
  var bottomRight=transform.Transform(new Point(area.Right,area.Bottom));
  const double width=214,height=142;
  trayPopup.HorizontalOffset=Math.Clamp(anchor.X-width/2,topLeft.X,bottomRight.X-width);
  trayPopup.VerticalOffset=anchor.Y-height>=topLeft.Y?anchor.Y-height:Math.Min(anchor.Y,bottomRight.Y-height);
  trayPopup.IsOpen=true;
 }
 internal void ShowFromTray(){
  if(trayPopup is not null)trayPopup.IsOpen=false;
  Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();
  _=Dispatcher.BeginInvoke(DispatcherPriority.Render,new Action(()=>{if(closing)return;InvalidateVisual();if(Content is UIElement content)content.InvalidateVisual();WindowRendering.Log("Window restored from tray");}));
 }
 void HideToTray(){
  if(tray is null){ShowFromTray();return;}
  Hide();
 }
 async void ExitFromTray(){
  if(domainsBusy){ShowFromTray();DomainNote.Text="Дождитесь сохранения списков.";return;}
  if(DomainsDirty&&MessageBox.Show("В редакторе доменов есть несохранённые изменения. Выйти без сохранения?","zapret",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes){ShowFromTray();return;}
  if(installerBusy){ShowFromTray();Progress.Text="Дождитесь завершения установки, затем повторите выход.";return;}
  if(trayExiting)return;trayExiting=true;monitor.Stop();
  try{
   operation?.Cancel();
   if(operationTask is not null)await operationTask.WaitAsync(TimeSpan.FromSeconds(20));
   // A lost status connection must not skip the stop command.
   await engine.StopAsync();
  }catch(Exception ex){
   AddLog("Выход: "+ex.Message);
   using var installed=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+ServicePaths.Name);
   if(serviceAvailable||connected||installed is not null){
    ShowFromTray();
    if(MessageBox.Show("Служба не подтвердила отключение. Закрыть только приложение? Подключение может остаться активным.\n\n"+ex.Message,"zapret — выход",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes){
     trayExiting=false;monitor.Start();return;
    }
   }
  }
  // Explicitly stop the WPF message loop, even if another hidden window still exists.
  exitRequested=true;closingRequested=true;closing=true;lifetime.Cancel();operation?.Cancel();
  Application.Current.Shutdown();
 }
}
