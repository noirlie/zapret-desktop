using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace ZapretDesktop;
public partial class MainWindow {
 DomainDocument? domainOriginal;
 string includedDraft="",excludedDraft="";
 bool domainsLoaded,domainsBusy,domainSwitching,editingExcluded;
 bool DomainsDirty=>domainOriginal is not null&&(includedDraft!=domainOriginal.Included||excludedDraft!=domainOriginal.Excluded);
 async Task LoadDomains(){
  if(AppContext.TryGetSwitch("ZapretDesktop.TestMode",out var testing)&&testing)return;
  if(domainsBusy)return;domainsBusy=true;RefreshDomainButtons();
  try{
   var state=await engine.Send(new("domains-read"),lifetime.Token);
   if(state.Error is not null||state.Domains is null)throw new IOException(state.Error??"Обновите службу новым установщиком.");
   SetDomainDocument(state.Domains);DomainNote.Text="Списки загружены. Базовые домены компонентов остаются без изменений.";
  }catch(OperationCanceledException){}catch(Exception ex){DomainNote.Text=ex.Message;}
  finally{domainsBusy=false;RefreshDomainButtons();}
 }
 void SetDomainDocument(DomainDocument document){domainOriginal=document;includedDraft=document.Included;excludedDraft=document.Excluded;domainsLoaded=true;ShowDomainDraft();}
 void ShowDomainDraft(){domainSwitching=true;DomainEditor.Text=editingExcluded?excludedDraft:includedDraft;domainSwitching=false;DomainTextChanged(DomainEditor,null!);}
 void DomainTabChanged(object sender,RoutedEventArgs e){editingExcluded=ExcludedTab.IsChecked==true;ShowDomainDraft();}
 void DomainTextChanged(object sender,TextChangedEventArgs e){
  if(domainSwitching||DomainNote is null)return;
  if(editingExcluded)excludedDraft=DomainEditor.Text;else includedDraft=DomainEditor.Text;
  UpdateDomainLines();DomainNote.Text=DomainsDirty?"Есть несохранённые изменения.":"Изменений нет.";
  try{DomainValidation.Normalize(DomainEditor.Text);DomainEditor.Foreground=new SolidColorBrush(Color.FromRgb(22,22,22));}
  catch(IOException ex){DomainNote.Text=ex.Message;DomainEditor.Foreground=new SolidColorBrush(Color.FromRgb(145,45,45));}
  RefreshDomainButtons();
 }
 void UpdateDomainLines(){if(DomainLineNumbers is not null)DomainLineNumbers.Text=string.Join("\n",Enumerable.Range(1,DomainEditor.Text.Count(c=>c=='\n')+1));}
 void DomainScrolled(object sender,ScrollChangedEventArgs e){DomainLineScroll?.ScrollToVerticalOffset(e.VerticalOffset);}
 void RefreshDomainButtons(){if(SaveDomainsButton is null)return;SaveDomainsButton.IsEnabled=domainsLoaded&&!domainsBusy&&DomainsDirty;ResetDomainsButton.IsEnabled=domainsLoaded&&!domainsBusy;RestoreDomainsButton.IsEnabled=domainsLoaded&&!domainsBusy&&domainOriginal?.HasBackup==true;DomainEditor.IsReadOnly=!domainsLoaded||domainsBusy;}
 void FindDomain(object sender,RoutedEventArgs e){var query=DomainSearch.Text.Trim();if(query.Length==0)return;var start=Math.Min(DomainEditor.Text.Length,DomainEditor.SelectionStart+DomainEditor.SelectionLength);var index=DomainEditor.Text.IndexOf(query,start,StringComparison.OrdinalIgnoreCase);if(index<0)index=DomainEditor.Text.IndexOf(query,StringComparison.OrdinalIgnoreCase);if(index<0){DomainNote.Text="Совпадений нет.";return;}DomainEditor.Focus();DomainEditor.Select(index,query.Length);DomainEditor.ScrollToLine(DomainEditor.GetLineIndexFromCharacterIndex(index));}
 async void ReloadDomains(object sender,RoutedEventArgs e){if(DomainsDirty&&MessageBox.Show("Отбросить несохранённые изменения и загрузить списки заново?","Домены",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;await LoadDomains();}
 async void SaveDomains(object sender,RoutedEventArgs e)=>await CommitDomains("domains-save");
 async void ResetDomains(object sender,RoutedEventArgs e){if(MessageBox.Show("Удалить ваши дополнения и исключения? Базовые списки установленной версии останутся. Будет сохранена резервная копия.","Вернуть по умолчанию",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes)await CommitDomains("domains-reset");}
 async void RestoreDomains(object sender,RoutedEventArgs e){if(MessageBox.Show("Заменить оба списка предыдущей сохранённой копией? Текущие списки станут резервной копией.","Восстановление доменов",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes)await CommitDomains("domains-restore");}
 async Task CommitDomains(string command){
  if(domainsBusy||domainOriginal is null)return;
  try{if(command=="domains-save"){DomainValidation.Normalize(includedDraft);DomainValidation.Normalize(excludedDraft);}}catch(IOException ex){DomainNote.Text=ex.Message;return;}
  domainsBusy=true;Power.IsEnabled=false;RefreshDomainButtons();ServiceSnapshot? previous=null;bool stopped=false;
  try{
   previous=await engine.Send(new("status"),lifetime.Token);
   if(previous.Busy||previous.RecoveryPending){DomainNote.Text="Дождитесь завершения подбора или отключите подключение.";return;}
   if(previous.Running){
    if(MessageBox.Show("Для применения списков подключение будет ненадолго остановлено и запущено снова. Продолжить?","Применить домены",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
    await engine.StopAsync();stopped=true;
   }
   var result=await engine.Send(new(command,Domains:new(includedDraft,excludedDraft,domainOriginal.Revision)),lifetime.Token);
   if(result.Error is not null||result.Domains is null)throw new IOException(result.Error??"Служба не подтвердила сохранение.");
   SetDomainDocument(result.Domains);DomainNote.Text=command=="domains-reset"?"Возвращены базовые списки. Ваши предыдущие записи сохранены в копии.":"Сохранено. Повторы удалены, резервная копия создана.";
  }catch(OperationCanceledException){}catch(Exception ex){DomainNote.Text=ex.Message;}
  finally{
   if(stopped&&previous?.Strategy is not null&&!closingRequested){try{var result=await engine.Send(new("start",preferences.Automatic,previous.Strategy,previous.Strategy),lifetime.Token);ApplyServiceState(result);if(result.Error is not null)DomainNote.Text+=" Подключение: "+result.Error;}catch(Exception ex){DomainNote.Text+=" Не удалось восстановить подключение: "+ex.Message;}}
   domainsBusy=false;Power.IsEnabled=true;RefreshDomainButtons();
  }
 }
}
