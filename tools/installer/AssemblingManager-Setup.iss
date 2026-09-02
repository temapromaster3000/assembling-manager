#define AppName "Assembling Manager"
#define AppURL "https://github.com/temapromaster3000/assembling-manager"
#define RepoOwner "temapromaster3000"
#define RepoName "assembling-manager"

#ifndef AppVersion
 #define AppVersion Trim(FileRead(FileOpen("..\..\Version.txt"), 0))
#endif

[Setup]
AppId={{6F2A4B1C-3D5E-4F60-8A9B-1C2D3E4F5A6B}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#RepoOwner}
AppPublisherURL={#AppURL}
DefaultDirName={userappdata}\AssemblingManager
UninstallDisplayName={#AppName} {#AppVersion}
UninstallDisplayIcon={app}\AssemblingManager.Updater.exe
OutputDir=..\..\artifacts
OutputBaseFilename=AssemblingManager-{#AppVersion}-setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
DisableDirPage=yes
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
russian.NoRevitFound=На этой системе не найдены поддерживаемые версии Revit (2021-2025).%n%nУстановка будет продолжена, но плагин не будет зарегистрирован в Revit автоматически.
english.NoRevitFound=No supported Revit versions (2021-2025) were found on this system.%n%nInstallation will proceed, but the plugin will not be automatically registered in Revit.

[Files]
; --- Revit 2021 (net48) ---
Source: "..\..\artifacts\publish\AssemblingManager-R21\*"; DestDir: "{app}\2021"; Check: Need2021; Flags: ignoreversion recursesubdirs
; --- Revit 2022 (net48) ---
Source: "..\..\artifacts\publish\AssemblingManager-R22\*"; DestDir: "{app}\2022"; Check: Need2022; Flags: ignoreversion recursesubdirs
; --- Revit 2023 (net48) ---
Source: "..\..\artifacts\publish\AssemblingManager-R23\*"; DestDir: "{app}\2023"; Check: Need2023; Flags: ignoreversion recursesubdirs
; --- Revit 2024 (net48) ---
Source: "..\..\artifacts\publish\AssemblingManager-R24\*"; DestDir: "{app}\2024"; Check: Need2024; Flags: ignoreversion recursesubdirs
; --- Revit 2025 (net8.0-windows) ---
Source: "..\..\artifacts\publish\AssemblingManager-R25\*"; DestDir: "{app}\2025"; Check: Need2025; Flags: ignoreversion recursesubdirs
; --- Updater (net48, общий) ---
Source: "..\..\artifacts\publish\Updater\AssemblingManager.Updater.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\artifacts\publish\Updater\AssemblingManager.Updater.pdb"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Code]
var
 Revit2021Installed: Boolean;
 Revit2022Installed: Boolean;
 Revit2023Installed: Boolean;
 Revit2024Installed: Boolean;
 Revit2025Installed: Boolean;

function IsRevitInstalled(Version: Integer): Boolean;
var
 Keys: TArrayOfString;
 Key, InstallPath: String;
 I: Integer;
begin
 Result := False;

 if RegGetSubkeyNames(HKLM64, 'SOFTWARE\Autodesk\Revit\' + IntToStr(Version), Keys) then
 begin
   for I := 0 to GetArrayLength(Keys) - 1 do
   begin
     Key := 'SOFTWARE\Autodesk\Revit\' + IntToStr(Version) + '\' + Keys[I];
     if RegQueryStringValue(HKLM64, Key, 'InstallationLocation', InstallPath) then
     begin
       if (InstallPath <> '') and DirExists(InstallPath) then
       begin
         Result := True;
         Exit;
       end;
     end;
   end;
 end;

 if RegGetSubkeyNames(HKLM32, 'SOFTWARE\Autodesk\Revit\' + IntToStr(Version), Keys) then
 begin
   for I := 0 to GetArrayLength(Keys) - 1 do
   begin
     Key := 'SOFTWARE\Autodesk\Revit\' + IntToStr(Version) + '\' + Keys[I];
     if RegQueryStringValue(HKLM32, Key, 'InstallationLocation', InstallPath) then
     begin
       if (InstallPath <> '') and DirExists(InstallPath) then
       begin
         Result := True;
         Exit;
       end;
     end;
   end;
 end;

 InstallPath := 'C:\Program Files\Autodesk\Revit ' + IntToStr(Version) + '\';
 if DirExists(InstallPath) then
   Result := True;
end;

procedure DetectRevitVersions;
begin
 Revit2021Installed := IsRevitInstalled(2021);
 Revit2022Installed := IsRevitInstalled(2022);
 Revit2023Installed := IsRevitInstalled(2023);
 Revit2024Installed := IsRevitInstalled(2024);
 Revit2025Installed := IsRevitInstalled(2025);
end;

function Need2021: Boolean;
begin
 Result := Revit2021Installed;
end;

function Need2022: Boolean;
begin
 Result := Revit2022Installed;
end;

function Need2023: Boolean;
begin
 Result := Revit2023Installed;
end;

function Need2024: Boolean;
begin
 Result := Revit2024Installed;
end;

function Need2025: Boolean;
begin
 Result := Revit2025Installed;
end;

function InitializeSetup: Boolean;
begin
 DetectRevitVersions;
 if not Need2021 and not Need2022 and not Need2023 and not Need2024 and not Need2025 then
   MsgBox(CustomMessage('NoRevitFound'), mbInformation, MB_OK);
 Result := True;
end;

procedure WriteAddinFile(const RevitVersion: String);
var
 AddinContent, AddinPath, AppDataDir: String;
begin
 AppDataDir := ExpandConstant('{userappdata}');
 AddinPath := AppDataDir + '\Autodesk\Revit\Addins\' + RevitVersion + '\AssemblingManager.addin';
 ForceDirectories(ExtractFilePath(AddinPath));
 AddinContent :=
   '<?xml version="1.0" encoding="utf-8" standalone="no"?>' + #13#10 +
   '<RevitAddIns>' + #13#10 +
   '  <AddIn Type="Application">' + #13#10 +
   '    <Name>Assembling Manager</Name>' + #13#10 +
   '    <Assembly>' + AppDataDir + '\AssemblingManager\' + RevitVersion + '\AssemblingManager.dll</Assembly>' + #13#10 +
   '    <AddInId>01958C2F-6E03-4812-AEC1-B3362506B1CC</AddInId>' + #13#10 +
   '    <FullClassName>AssemblingManager.Revit.App</FullClassName>' + #13#10 +
   '    <VendorId>YOURCOMPANY</VendorId>' + #13#10 +
   '    <VendorDescription>Your company description</VendorDescription>' + #13#10 +
   '  </AddIn>' + #13#10 +
   '</RevitAddIns>' + #13#10;
 SaveStringToFile(AddinPath, AddinContent, False);
end;

procedure RemoveAddinFile(const RevitVersion: String);
var
 AppDataDir: String;
begin
 AppDataDir := ExpandConstant('{userappdata}');
 DeleteFile(AppDataDir + '\Autodesk\Revit\Addins\' + RevitVersion + '\AssemblingManager.addin');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
 if CurStep = ssPostInstall then
 begin
   if Revit2021Installed then WriteAddinFile('2021');
   if Revit2022Installed then WriteAddinFile('2022');
   if Revit2023Installed then WriteAddinFile('2023');
   if Revit2024Installed then WriteAddinFile('2024');
   if Revit2025Installed then WriteAddinFile('2025');
 end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
 if CurUninstallStep = usPostUninstall then
 begin
   RemoveAddinFile('2021');
   RemoveAddinFile('2022');
   RemoveAddinFile('2023');
   RemoveAddinFile('2024');
   RemoveAddinFile('2025');
 end;
end;

[UninstallDelete]
Type: filesandordirs; Name: "{app}\2021"
Type: filesandordirs; Name: "{app}\2022"
Type: filesandordirs; Name: "{app}\2023"
Type: filesandordirs; Name: "{app}\2024"
Type: filesandordirs; Name: "{app}\2025"
Type: filesandordirs; Name: "{app}\staging"
Type: files; Name: "{app}\update-pending.txt"
Type: filesandordirs; Name: "{app}"
