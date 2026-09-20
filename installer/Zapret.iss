#define AppName "zapret"
[Setup]
AppId={{842BCF73-E4AC-44C1-B1E8-0651A6E72A60}
AppName={#AppName}
AppVersion=0.1.2
AppVerName=zapret 0.1.2
DefaultDirName={autopf}\Zapret
DisableDirPage=no
DirExistsWarning=no
DisableProgramGroupPage=yes
DisableWelcomePage=no
DisableReadyPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\artifacts\release
OutputBaseFilename=ZapretSetup
SetupIconFile=..\src\ZapretDesktop\Assets\zapret.ico
UninstallDisplayIcon={app}\ZapretDesktop.exe
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
WizardBackColor=white
WizardImageFile=assets\wizard-z.bmp
WizardSmallImageFile=assets\wizard-z-small.bmp
WizardImageStretch=yes
AppMutex=Local\ZapretDesktop.SetupGuard
CloseApplications=yes
RestartApplications=no
UninstallDisplayName=zapret

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Messages]
WelcomeLabel1=Всё начинается с одной кнопки.
WelcomeLabel2=Установим zapret и всё необходимое для подключения.%n%nЕсли приложение уже установлено, обновим его на прежнем месте. Ваши настройки и списки сохранятся.%n%nНа время установки подключение будет остановлено. Перед установкой закройте zapret через «Выход» в трее.
FinishedHeadingLabel=zapret готов
FinishedLabel=Откройте приложение и нажмите круг подключения.

[Files]
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion recursesubdirs
Source: "..\artifacts\app\ZapretDesktop.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\app\service\ZapretService.exe"; DestDir: "{app}\service"; Flags: ignoreversion
Source: "..\artifacts\app\service\ZapretService.exe"; DestDir: "{tmp}\setup-service"; Flags: dontcopy
Source: "payload\components\*"; DestDir: "{tmp}\setup-components"; Flags: dontcopy recursesubdirs

[Icons]
Name: "{autoprograms}\zapret"; Filename: "{app}\ZapretDesktop.exe"
Name: "{autodesktop}\zapret"; Filename: "{app}\ZapretDesktop.exe"

[Run]
Filename: "{app}\ZapretDesktop.exe"; Description: "Открыть zapret"; Flags: postinstall nowait skipifsilent runasoriginaluser

[Code]
var PurgeUserData: Boolean;

procedure InitializeWizard();
begin
  WizardForm.SelectDirLabel.Caption := 'Выберите диск и папку приложения. Системная служба и компоненты останутся в защищённой папке ' + ExpandConstant('{autopf}\ZapretDesktopService') + '.';
end;

function ExistingLocationError(): String;
var Previous: String;
begin
  Result := '';
  if RegQueryStringValue(HKLM, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{842BCF73-E4AC-44C1-B1E8-0651A6E72A60}_is1', 'InstallLocation', Previous) then
    if (Previous <> '') and (CompareText(RemoveBackslashUnlessRoot(Previous), RemoveBackslashUnlessRoot(ExpandConstant('{app}'))) <> 0) then
      Result := 'Для переноса на другой диск сначала удалите установленное приложение без очистки настроек. Затем установите его в выбранную папку.';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var Error: String;
begin
  Result := True;
  if CurPageID = wpSelectDir then begin
    Error := ExistingLocationError();
    if Error <> '' then begin MsgBox(Error, mbInformation, MB_OK); Result := False; end;
  end;
end;
function PrepareToInstall(var NeedsRestart: Boolean): String;
var Code: Integer; Owner: AnsiString; Helper, OwnerFile, PipeName: String;
begin
  Result := ExistingLocationError();
  if Result <> '' then exit;
  ExtractTemporaryFiles('{tmp}\setup-service\ZapretService.exe');
  ExtractTemporaryFiles('{tmp}\setup-components\*');
  Helper := ExpandConstant('{tmp}\setup-service\ZapretService.exe');
  OwnerFile := ExpandConstant('{tmp}\zapret-owner.sid');
  PipeName := 'Zapret.Setup.' + GetSHA256OfString(OwnerFile);
  if not Exec(Helper, '--capture-owner "' + PipeName + '" "' + OwnerFile + '"', '', SW_HIDE, ewNoWait, Code) then begin
    Result := 'Не удалось запустить помощник установщика.'; exit;
  end;
  if not ExecAsOriginalUser(Helper, '--identify-owner "' + PipeName + '"', '', SW_HIDE, ewWaitUntilTerminated, Code) then begin
    Result := 'Не удалось определить пользователя для установки.'; exit;
  end;
  if (Code <> 0) or not LoadStringFromFile(OwnerFile, Owner) then begin
    Result := 'Не удалось подготовить установку. Запустите установщик от обычного пользователя и подтвердите запрос Windows.'; exit;
  end;
  if not Exec(Helper, '--setup "' + ExpandConstant('{tmp}\setup-components') + '" "' + Trim(String(Owner)) + '" "' + ExpandConstant('{app}') + '"', '', SW_HIDE, ewWaitUntilTerminated, Code) then begin
    Result := 'Не удалось запустить установку службы.'; exit;
  end;
  if Code <> 0 then Result := 'Служба не установлена. Исправьте указанную ошибку и повторите установку. Существующие настройки сохранены. Подробности: C:\Program Files\ZapretDesktopService\installer-error.log';
end;

function InitializeUninstall(): Boolean;
var Form: TSetupForm; Check: TNewCheckBox; Note: TNewStaticText;
    RemoveButton, CancelButton: TNewButton; I: Integer;
begin
  Result := False;
  if CheckForMutexes('Local\ZapretDesktop.SetupGuard') then begin
    if not UninstallSilent then MsgBox('Сначала закройте zapret через «Выйти» в трее, затем повторите удаление.', mbInformation, MB_OK);
    exit;
  end;
  PurgeUserData := False;
  if UninstallSilent then begin
    for I := 1 to ParamCount do
      if CompareText(ParamStr(I), '/PURGEDATA') = 0 then PurgeUserData := True;
    Result := True; exit;
  end;
  Form := CreateCustomForm(ScaleX(510), ScaleY(245), False, True);
  try
    Form.Caption := 'Удаление — zapret';
    Form.Color := clWhite;
    Note := TNewStaticText.Create(Form); Note.Parent := Form;
    Note.SetBounds(ScaleX(24), ScaleY(22), ScaleX(462), ScaleY(52));
    Note.AutoSize := False; Note.WordWrap := True;
    Note.Caption := 'Приложение и служба будут удалены. По умолчанию настройки сохраняются для повторной установки.';
    Check := TNewCheckBox.Create(Form); Check.Parent := Form;
    Check.SetBounds(ScaleX(24), ScaleY(86), ScaleX(462), ScaleY(25));
    Check.Caption := 'Удалить все настройки и выбранные конфигурации';
    Check.Checked := False;
    Note := TNewStaticText.Create(Form); Note.Parent := Form;
    Note.SetBounds(ScaleX(44), ScaleY(120), ScaleX(442), ScaleY(54));
    Note.AutoSize := False; Note.WordWrap := True;
    Note.Caption := 'Также будут удалены ваши домены, исключения, журналы, загруженные пакеты и резервные копии этой установки. Отменить очистку нельзя.';
    RemoveButton := TNewButton.Create(Form); RemoveButton.Parent := Form;
    RemoveButton.SetBounds(ScaleX(274), ScaleY(195), ScaleX(100), ScaleY(30));
    RemoveButton.Caption := 'Продолжить'; RemoveButton.ModalResult := mrOk; RemoveButton.Default := True;
    CancelButton := TNewButton.Create(Form); CancelButton.Parent := Form;
    CancelButton.SetBounds(ScaleX(386), ScaleY(195), ScaleX(100), ScaleY(30));
    CancelButton.Caption := 'Отмена'; CancelButton.ModalResult := mrCancel; CancelButton.Cancel := True;
    Result := Form.ShowModal() = mrOk;
    if Result then PurgeUserData := Check.Checked;
  finally Form.Free(); end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Code: Integer; Arguments: String;
begin
  if CurUninstallStep = usUninstall then begin
    Arguments := '--remove-installation "' + ExpandConstant('{app}') + '"';
    if PurgeUserData then Arguments := Arguments + ' --purge';
    if not Exec(ExpandConstant('{app}\service\ZapretService.exe'), Arguments, '', SW_HIDE, ewWaitUntilTerminated, Code) then
      RaiseException('Не удалось запустить удаление службы. Файлы приложения сохранены.');
    if Code <> 0 then RaiseException('Служба или данные не удалены полностью. Удаление остановлено; устраните ошибку и повторите.');
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var Code: Integer;
begin
  if CurStep = ssPostInstall then begin
    if not ExecAsOriginalUser(ExpandConstant('{app}\service\ZapretService.exe'), '--migrate-startup "' + ExpandConstant('{app}\ZapretDesktop.exe') + '"', '', SW_HIDE, ewWaitUntilTerminated, Code) then
      Log('Autorun migration could not run; the app will retry on first launch.');
  end;
end;
