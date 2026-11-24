const
  WM_CLOSE = $0010;
  SW_RESTORE = 9;
  ProcessQueryAndWait = $101000; { PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE }
  WaitTimeout = 258;
  CloseTimeoutMs = 60000;

function FindWindowEx(Parent, After: HWND; ClassName: NativeUInt; WindowName: String): HWND;
  external 'FindWindowExW@user32.dll stdcall';
function GetWindowThreadProcessId(Window: HWND; var ProcessId: Cardinal): Cardinal;
  external 'GetWindowThreadProcessId@user32.dll stdcall';
function OpenProcess(Access: Cardinal; InheritHandle: BOOL; ProcessId: Cardinal): THandle;
  external 'OpenProcess@kernel32.dll stdcall';
function QueryFullProcessImageName(Process: THandle; Flags: Cardinal; ImagePath: String; var Length: Cardinal): BOOL;
  external 'QueryFullProcessImageNameW@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): BOOL;
  external 'CloseHandle@kernel32.dll stdcall';
function WaitForSingleObject(Handle: THandle; Milliseconds: Cardinal): Cardinal;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function GetTickCount64: Int64;
  external 'GetTickCount64@kernel32.dll stdcall';
function SetForegroundWindow(Window: HWND): BOOL;
  external 'SetForegroundWindow@user32.dll stdcall';
function IsIconic(Window: HWND): BOOL;
  external 'IsIconic@user32.dll stdcall';
function ShowWindowAsync(Window: HWND; ShowCommand: Integer): BOOL;
  external 'ShowWindowAsync@user32.dll stdcall';
function UpdateWindow(Window: HWND): BOOL;
  external 'UpdateWindow@user32.dll stdcall';

function FindInstalledTabForgeWindow(const InstalledExe: String; var Process: THandle; var ProcessId: Cardinal): HWND;
var
  ImagePath: String;
  ImageLength: Cardinal;
begin
  Result := 0;
  Process := 0;
  repeat
    Result := FindWindowEx(0, Result, 0, 'TabForge');
    if Result = 0 then
      Exit;
    if GetWindowThreadProcessId(Result, ProcessId) <> 0 then
    begin
      Process := OpenProcess(ProcessQueryAndWait, False, ProcessId);
      if Process <> 0 then
      begin
        ImageLength := 32768;
        SetLength(ImagePath, ImageLength);
        if QueryFullProcessImageName(Process, 0, ImagePath, ImageLength) then
          if CompareText(Copy(ImagePath, 1, ImageLength), InstalledExe) = 0 then
            Exit;
        CloseHandle(Process);
        Process := 0;
      end;
    end;
  until False;
end;

function RequestTabForgeClose(const InstalledExe: String): Boolean;
var
  Window: HWND;
  Process, NextProcess: THandle;
  ProcessId, CurrentId, NextProcessId, WaitResult: Cardinal;
  Deadline: Int64;
begin
  Result := False;
  Deadline := GetTickCount64 + CloseTimeoutMs;
  Window := FindInstalledTabForgeWindow(InstalledExe, Process, ProcessId);
  while Window <> 0 do
  begin
    try
      { The window's normal close flow may pause for an unsaved-score dialog. }
      if (GetWindowThreadProcessId(Window, CurrentId) <> 0) and (CurrentId = ProcessId) then
      begin
        if IsIconic(Window) then
          ShowWindowAsync(Window, SW_RESTORE);
        SetForegroundWindow(Window);
        if not PostMessage(Window, WM_CLOSE, 0, 0) then
          Exit;
        repeat
          WaitResult := WaitForSingleObject(Process, 100);
          if WaitResult = 0 then
            Break;
          if (WaitResult <> WaitTimeout) or (GetTickCount64 >= Deadline) then
            Exit;
        until (GetWindowThreadProcessId(Window, CurrentId) = 0) or (CurrentId <> ProcessId);
      end;
      Window := FindInstalledTabForgeWindow(InstalledExe, NextProcess, NextProcessId);
      { The last window can disappear before TabForge has finished closing its audio helper. }
      if (Window = 0) and (WaitForSingleObject(Process, 8000) <> 0) then
        Exit;
    finally
      CloseHandle(Process);
    end;
    Process := NextProcess;
    ProcessId := NextProcessId;
  end;
  Result := True;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  PreviousCaption: String;
begin
  Result := True;
  { Let the editor handle WM_CLOSE before Restart Manager shuts down file users. }
  if (CurPageID <> wpPreparing) or not WizardForm.PreparingMemo.Visible or
    not WizardForm.PreparingYesRadio.Visible or not WizardForm.PreparingYesRadio.Checked then
    Exit;
  PreviousCaption := WizardForm.PreparingLabel.Caption;
  WizardForm.PreparingLabel.Caption := 'Waiting for TabForge to close. Answer any save prompts in TabForge.';
  UpdateWindow(WizardForm.Handle);
  try
    Result := RequestTabForgeClose(ExpandConstant('{app}\TabForge.exe'));
  finally
    WizardForm.PreparingLabel.Caption := PreviousCaption;
  end;
  if not Result then
    SuppressibleMsgBox('TabForge is still open. Finish saving or close TabForge, then select Next to try again.',
      mbInformation, MB_OK, IDOK);
end;
