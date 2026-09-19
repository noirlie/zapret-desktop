#define AppName "zapret"
[Setup]
AppId={{842BCF73-E4AC-44C1-B1E8-0651A6E72A60}
AppName={#AppName}
AppVersion=0.1.0
AppVerName=zapret 0.1.0
DefaultDirName={autopf}\Zapret
DisableDirPage=yes
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
function PrepareToInstall(var NeedsRestart: Boolean): String;
var Code: Integer; Owner: AnsiString; Helper, OwnerFile, PipeName: String;
begin
  Result := '';
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
  if not Exec(Helper, '--setup "' + ExpandConstant('{tmp}\setup-components') + '" "' + Trim(String(Owner)) + '"', '', SW_HIDE, ewWaitUntilTerminated, Code) then begin
    Result := 'Не удалось запустить установку службы.'; exit;
  end;
  if Code <> 0 then Result := 'Служба не установлена. Исправьте указанную ошибку и повторите установку. Существующие настройки сохранены. Подробности: C:\Program Files\ZapretDesktopService\installer-error.log';
end;

function InitializeUninstall(): Boolean;
var Code: Integer;
begin
  Result := True;
  if RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\ZapretDesktopService') then begin
    Result := Exec(ExpandConstant('{app}\service\ZapretService.exe'), '--uninstall', '', SW_HIDE, ewWaitUntilTerminated, Code);
    if Result then Result := Code = 0;
    if not Result then MsgBox('Не удалось остановить службу. Удаление отменено; приложение и настройки сохранены.', mbError, MB_OK);
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
