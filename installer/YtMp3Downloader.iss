; Inštalátor YtMp3 Downloader.
; Aplikácia je publikovaná ako self-contained (.NET runtime je súčasťou), takže inštalátor nepotrebuje
; žiadny prerequisite. yt-dlp, ffmpeg a deno si aplikácia stiahne pri prvom spustení do %LocalAppData%.
; Verziu a zdrojový priečinok je možné prepísať z príkazového riadka: ISCC /DMyAppVersion=1.2.3 /DPublishDir=...

#define MyAppName "YtMp3 Downloader"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif
#define MyAppExe "YtMp3Downloader.exe"

[Setup]
AppId={{6B0F5C1E-3D52-4B7A-9A64-2F1C8E7D4A19}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Juraj Suchán
AppContact=juraj.suchan@gmail.com
VersionInfoVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Predvolene inštalácia len pre aktuálneho používateľa (bez admin práv); používateľ si môže zvoliť pre všetkých.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\artifacts\installer
OutputBaseFilename=YtMp3Downloader-Setup-{#MyAppVersion}
SetupIconFile=..\src\YtMp3Downloader\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "slovak"; MessagesFile: "compiler:Languages\Slovak.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
const
  DataDirName = 'YtMp3Downloader';

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: string;
  Question: string;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  DataDir := ExpandConstant('{localappdata}\') + DataDirName;
  if (not DirExists(DataDir)) or UninstallSilent then
    Exit;

  if ActiveLanguage = 'slovak' then
    Question := 'Odstrániť aj stiahnuté nástroje (yt-dlp, ffmpeg, deno), nastavenia a frontu?' + #13#10 +
                'Stiahnuté MP3 súbory zostanú zachované.'
  else
    Question := 'Also remove downloaded tools (yt-dlp, ffmpeg, deno), settings and queue?' + #13#10 +
                'Downloaded MP3 files will be kept.';

  if MsgBox(Question, mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    DelTree(DataDir, True, True, True);
end;
