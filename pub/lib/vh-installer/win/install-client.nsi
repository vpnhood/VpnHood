; The Windows setup of a VpnHood desktop app, one script for every app: Publish-WinApp.ps1 compiles
; it with the app's names, its publish output and the install root's two launchers (the build's
; VhRootLaunchersDir). Per machine, under Program Files:
;
;   <root>\<App>.exe, <command>.exe   the root's launchers, over current\<App>.dll
;   <root>\current                    a junction naming the version in use
;   <root>\<version>\                 each version's own folder; the one before is kept
;   <root>\<version>\publish.json     the release's feed file, whose feed the app's updater reads
;   <root>\vhupdate.ps1               the updater, which its task runs at boot and every 12 hours
;   <root>\updates\, update.log       the updater's and the app's downloads, and what the updater last did
;   <root>\update.missed              there while the updater's last run could not reach the server
;   <root>\uninstall\uninstall.exe    out of the root, which is on the PATH
;
; Shortcuts, the tray's sign-in entry, the service and the PATH name the root, never a version, so
; changing the version only re-points "current". The app registers its own service and puts the
; root on the PATH ("service install"); this runs it, and "service uninstall" on the way out. The
; app does its own setup, so a zip or another packager gets the same service and PATH. The updater's
; task, <INSTANCE_NAME>Updater, is this setup's, as Linux's updater unit is its installer's: on for a
; new install, as an update leaves it, or as /AUTOUPDATE and /NOAUTOUPDATE say. Add no page that asks:
; modern apps' setups do not, and Settings has the switch.

Unicode true
ManifestDPIAware true
RequestExecutionLevel admin
SetCompressor /SOLID lzma

!include MUI2.nsh
!include LogicLib.nsh
!include x64.nsh
!include WinVer.nsh
!include WordFunc.nsh
!include FileFunc.nsh

; What Publish-WinApp.ps1 passes (/D<name>=<value>). LEGACY_MSI_UPGRADE_CODE is optional: the MSI an
; app shipped before this setup, which is removed first. So is WELCOMEFINISHPAGE_BITMAP, the app's
; picture for the Welcome and Finish pages (VhAppSetupImage), else NSIS's own.
!macro VhRequire NAME
	!ifndef ${NAME}
		!error "${NAME} is not defined: Publish-WinApp.ps1 passes it as /D${NAME}=..."
	!endif
!macroend
!insertmacro VhRequire PRODUCT_NAME    ; the name a person sees: VpnHood! CLIENT
!insertmacro VhRequire INSTANCE_NAME   ; the window's executable, the service, the folder: VpnHoodClient
!insertmacro VhRequire COMMAND_NAME    ; the console launcher: vhclient
!insertmacro VhRequire VERSION         ; 8.2.860
!insertmacro VhRequire VERSION4        ; 8.2.860.0, as Windows' version resource wants it
!insertmacro VhRequire COMPANY
!insertmacro VhRequire COPYRIGHT
!insertmacro VhRequire ICON
!insertmacro VhRequire SOURCE_DIR      ; the publish output: the version's folder
!insertmacro VhRequire LAUNCHERS_DIR   ; the root's two launchers
!insertmacro VhRequire PUBLISH_INFO_FILE ; the release's feed file: <title>-win-x64.json
!insertmacro VhRequire OUTPUT_FILE

; SIGN_SCRIPT (optional, when signing is configured): signs the uninstaller before it is packed. The
; setup is signed after makensis, but the uninstaller is written from inside it at install time, so
; only this reaches it; a failure fails the build.
!ifdef SIGN_SCRIPT
	!uninstfinalize 'pwsh -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "${SIGN_SCRIPT}" -file "%1"' = 0
!endif

!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${INSTANCE_NAME}"
!define RUN_KEY "Software\Microsoft\Windows\CurrentVersion\Run"
!define CURRENT_FOLDER "current"   ; WindowsDesktopPaths.CurrentFolderName
!define UNINSTALL_FOLDER "uninstall"

Name "${PRODUCT_NAME}"
OutFile "${OUTPUT_FILE}"
InstallDir "$PROGRAMFILES64\${INSTANCE_NAME}"
BrandingText "${PRODUCT_NAME} ${VERSION}"
ShowInstDetails show
ShowUninstDetails show

VIProductVersion "${VERSION4}"
VIFileVersion "${VERSION4}"
VIAddVersionKey "ProductName" "${PRODUCT_NAME}"
VIAddVersionKey "CompanyName" "${COMPANY}"
VIAddVersionKey "LegalCopyright" "${COPYRIGHT}"
VIAddVersionKey "FileDescription" "${PRODUCT_NAME} Setup"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"

!define MUI_ICON "${ICON}"
!define MUI_UNICON "${ICON}"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_TEXT "Open ${PRODUCT_NAME}"
!define MUI_FINISHPAGE_RUN_FUNCTION OpenApp
!ifdef WELCOMEFINISHPAGE_BITMAP
	!define MUI_WELCOMEFINISHPAGE_BITMAP "${WELCOMEFINISHPAGE_BITMAP}"
!endif

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!define MUI_PAGE_CUSTOMFUNCTION_SHOW FinishPageShow
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Var PreviousVersion
Var IsServerCore
Var JunctionLink
Var JunctionTarget
Var JunctionError
Var SetupMutex
Var AutoUpdateChoice
Var CloseExtraArgs

; One setup at a time - the app's updater runs one silently, a person one by hand: beside another, each would close
; what the other runs and write over its files. A silent one waits for the other; a person is asked to. A setup that
; ended without letting go leaves the mutex abandoned, which is as good as free. Held until this setup ends.
Function TakeSetupMutex
	System::Call 'kernel32::CreateMutexW(p 0, i 0, w "Global\${INSTANCE_NAME}-setup") p .s'
	Pop $SetupMutex
	${Do}
		StrCpy $1 0
		${If} ${Silent}
			StrCpy $1 600000
		${EndIf}
		System::Call 'kernel32::WaitForSingleObject(p $SetupMutex, i $1) i .r0'
		${If} $0 = 0
		${OrIf} $0 = 0x80
			${Break}
		${EndIf}
		MessageBox MB_RETRYCANCEL|MB_ICONEXCLAMATION "Another setup of ${PRODUCT_NAME} is running. Wait for it to finish, then try again." /SD IDCANCEL IDRETRY retry
		SetErrorLevel 5
		Abort
		retry:
	${Loop}
FunctionEnd

Function .onInit
	SetShellVarContext all
	SetRegView 64

	${IfNot} ${RunningX64}
		MessageBox MB_ICONSTOP "${PRODUCT_NAME} needs 64-bit Windows." /SD IDOK
		SetErrorLevel 1
		Abort
	${EndIf}
	${IfNot} ${AtLeastWin10}
		MessageBox MB_ICONSTOP "${PRODUCT_NAME} needs Windows 10 or later." /SD IDOK
		SetErrorLevel 1
		Abort
	${EndIf}
	Call TakeSetupMutex

	; the updater's task: as told, else as an earlier install left it, else on (install-client-task.ps1)
	${GetParameters} $R0
	ClearErrors
	${GetOptions} $R0 "/NOAUTOUPDATE" $R1
	${IfNot} ${Errors}
		StrCpy $AutoUpdateChoice "off"
	${EndIf}
	ClearErrors
	${GetOptions} $R0 "/AUTOUPDATE" $R1
	${IfNot} ${Errors}
		StrCpy $AutoUpdateChoice "on"
	${EndIf}

	; Server Core has no desktop: no shortcuts, no tray at sign-in, no window to open, as Linux's
	; installer adds none on a machine with no desktop
	ReadRegStr $0 HKLM "SOFTWARE\Microsoft\Windows NT\CurrentVersion" "InstallationType"
	${If} $0 == "Server Core"
		StrCpy $IsServerCore 1
	${EndIf}

	; an earlier install keeps its folder, and is never replaced by an older version
	ReadRegStr $0 HKLM "${UNINSTALL_KEY}" "InstallLocation"
	${If} $0 != ""
		StrCpy $INSTDIR $0
	${EndIf}
	ReadRegStr $PreviousVersion HKLM "${UNINSTALL_KEY}" "DisplayVersion"
	${If} $PreviousVersion != ""
		${VersionCompare} $PreviousVersion "${VERSION}" $1
		${If} $1 == 1
			MessageBox MB_ICONSTOP "${PRODUCT_NAME} $PreviousVersion is installed, which is newer than ${VERSION}." /SD IDOK
			SetErrorLevel 2
			Abort
		${EndIf}
	${EndIf}
FunctionEnd

; Ends every process run from the folder on the stack (install-client-close.ps1), by its path.
!macro VhCloseProcessesFunction UN
Function ${UN}CloseProcesses
	Exch $0
	Push $1
	; a folder that ends in "\", as an MSI's InstallLocation does, would escape the quote closing it on
	; the command line, and close.ps1 would get the quote in the path
	StrCpy $1 $0 1 -1
	${If} $1 == "\"
		StrCpy $0 $0 -1
	${EndIf}
	InitPluginsDir
	File "/oname=$PLUGINSDIR\close.ps1" "${__FILEDIR__}\install-client-close.ps1"
	nsExec::ExecToLog '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\close.ps1" -folder "$0" $CloseExtraArgs'
	Pop $1
	Pop $1
	Pop $0
FunctionEnd
!macroend
!insertmacro VhCloseProcessesFunction ""
!insertmacro VhCloseProcessesFunction "un."

; Points the folder $JunctionLink at $JunctionTarget: makes it a junction, or re-points the junction it
; is, in one call on the folder itself - FSCTL_SET_REPARSE_POINT replaces a junction's target in place,
; so there is no moment without one: the root's launchers, the service and the PATH run the version
; through it, and a power cut mid-switch must still leave them one. $JunctionError says why not.
Function SetJunction
	StrCpy $JunctionError ""
	${IfNot} ${FileExists} "$JunctionLink\*.*"
		CreateDirectory "$JunctionLink"
	${EndIf}

	; REPARSE_DATA_BUFFER for a mount point: the tag, the data's length, a reserved USHORT, then the
	; substitute name ("\??\<target>") and the print name ("<target>"), each ending in a null
	StrCpy $R0 "\??\$JunctionTarget"
	StrCpy $R1 $JunctionTarget
	StrLen $R2 $R0
	IntOp $R2 $R2 * 2                  ; substitute name, bytes
	StrLen $R3 $R1
	IntOp $R3 $R3 * 2                  ; print name, bytes
	IntOp $R4 $R2 + $R3
	IntOp $R4 $R4 + 12                 ; the data: four USHORTs and both names with their nulls
	IntOp $R5 $R4 + 8                  ; the buffer
	IntOp $R6 $R2 + 2                  ; where the print name starts
	System::Alloc $R5
	Pop $R7
	System::Call "*$R7(i 0xA0000003, &i2 $R4, &i2 0, &i2 0, &i2 $R2, &i2 $R6, &i2 $R3)"
	IntOp $R8 $R7 + 16
	System::Call "kernel32::lstrcpyW(p R8, w R0)"
	IntOp $R8 $R8 + $R6
	System::Call "kernel32::lstrcpyW(p R8, w R1)"

	; GENERIC_WRITE on the folder itself: FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS
	System::Call "kernel32::CreateFileW(w '$JunctionLink', i 0x40000000, i 0, p 0, i 3, i 0x02200000, p 0) i .R8 ?e"
	Pop $R9
	${If} $R8 = -1
		StrCpy $JunctionError "could not open $JunctionLink (error $R9)"
	${Else}
		System::Call "kernel32::DeviceIoControl(i R8, i 0x000900A4, p R7, i R5, p 0, i 0, *i .R6, p 0) i .R0 ?e"
		Pop $R9
		${If} $R0 = 0
			StrCpy $JunctionError "could not point $JunctionLink at $JunctionTarget (error $R9)"
		${EndIf}
		System::Call "kernel32::CloseHandle(i R8)"
	${EndIf}
	System::Free $R7
FunctionEnd

!ifdef LEGACY_MSI_UPGRADE_CODE
; Every install of the MSI that the app's earlier releases shipped, found by its upgrade code: what it
; runs closed, then removed quietly. Its settings stay where it kept them; the app imports them.
Function RemoveLegacyMsi
	StrCpy $R9 0
	${Do}
		System::Call 'msi::MsiEnumRelatedProductsW(w "${LEGACY_MSI_UPGRADE_CODE}", i 0, i 0, w .r1) i .r0'
		${If} $0 != 0
			${Break}
		${EndIf}

		DetailPrint "Removing the earlier ${PRODUCT_NAME} ($1)..."
		System::Call 'msi::MsiGetProductInfoW(w r1, w "InstallLocation", w .r2, *i ${NSIS_MAX_STRLEN})'
		${If} $2 != ""
			Push $2
			Call CloseProcesses
		${EndIf}
		ExecWait '"$SYSDIR\msiexec.exe" /x $1 /qn /norestart' $3
		${If} $3 != 0
		${AndIf} $3 != 3010
			DetailPrint "The earlier ${PRODUCT_NAME} could not be removed (msiexec exit $3). Remove it in Installed apps."
			${Break}
		${EndIf}

		; each one removed shrinks the list; a code that never goes cannot hold the setup for ever
		IntOp $R9 $R9 + 1
		${If} $R9 >= 5
			${Break}
		${EndIf}
	${Loop}
FunctionEnd
!endif

; Version folders other than this one and the one before go.
Function RemoveOldVersions
	FindFirst $0 $1 "$INSTDIR\*"
	${DoWhile} $1 != ""
		${If} $1 != "."
		${AndIf} $1 != ".."
		${AndIf} $1 != "${VERSION}"
		${AndIf} $1 != "$PreviousVersion"
		${AndIf} $1 != "${CURRENT_FOLDER}"
		${AndIf} $1 != "${UNINSTALL_FOLDER}"
		${AndIf} ${FileExists} "$INSTDIR\$1\${INSTANCE_NAME}.dll"
			DetailPrint "Removing version $1"
			RMDir /r "$INSTDIR\$1"
		${EndIf}
		FindNext $0 $1
	${Loop}
	FindClose $0
FunctionEnd

; The window, as the person signed in: Explorer starts it, so it does not inherit this setup's
; elevation.
Function OpenApp
	Exec '"$WINDIR\explorer.exe" "$INSTDIR\${INSTANCE_NAME}.exe"'
FunctionEnd

; On Server Core the finish page offers no window to open.
Function FinishPageShow
	${If} $IsServerCore == 1
		SendMessage $mui.FinishPage.Run ${BM_SETCHECK} ${BST_UNCHECKED} 0
		ShowWindow $mui.FinishPage.Run ${SW_HIDE}
	${EndIf}
FunctionEnd

Section "Install"
	SetOutPath "$INSTDIR"

	!ifdef LEGACY_MSI_UPGRADE_CODE
		Call RemoveLegacyMsi
	!endif

	; an install of this app already here: its service stopped and what runs from it closed, so
	; nothing the setup replaces is in use - closed even where the command is gone, as an uninstall
	; that could not finish leaves the tray running without it. A version's own folder stays, but a
	; process sees its files through "current" (the launcher's app root), so one left open past the
	; switch would load the new version's assemblies beside its own as it first used each
	${If} ${FileExists} "$INSTDIR\${COMMAND_NAME}.exe"
		DetailPrint "Stopping ${INSTANCE_NAME}..."
		nsExec::ExecToLog '"$INSTDIR\${COMMAND_NAME}.exe" service stop'
		Pop $0
	${EndIf}
	; the windows and trays it closes are listed for the new service to open again; this setup's own process, which
	; the app's updater runs from the install's updates folder, is spared
	System::Call 'kernel32::GetCurrentProcessId() i .r1'
	StrCpy $CloseExtraArgs '-exceptProcessId $1 -relaunchFile "$INSTDIR\relaunch.json" -uiExe "$INSTDIR\${INSTANCE_NAME}.exe"'
	Push "$INSTDIR"
	Call CloseProcesses
	StrCpy $CloseExtraArgs ""

	; the version's own folder, then the root's launchers and the updater over it
	SetOutPath "$INSTDIR\${VERSION}"
	File /r "${SOURCE_DIR}\*.*"
	File "/oname=publish.json" "${PUBLISH_INFO_FILE}"
	SetOutPath "$INSTDIR"
	File "${LAUNCHERS_DIR}\*.exe"
	File "/oname=vhupdate.ps1" "${__FILEDIR__}\install-client-updater.ps1"

	StrCpy $JunctionLink "$INSTDIR\${CURRENT_FOLDER}"
	StrCpy $JunctionTarget "$INSTDIR\${VERSION}"
	Call SetJunction
	${If} $JunctionError != ""
		DetailPrint "Could not set ${CURRENT_FOLDER}: $JunctionError"
		MessageBox MB_ICONSTOP "${PRODUCT_NAME} could not be installed: $JunctionError" /SD IDOK
		SetErrorLevel 3
		Abort
	${EndIf}

	; Installed apps, the Start menu, the desktop, and the tray at every administrator's sign-in
	CreateDirectory "$INSTDIR\${UNINSTALL_FOLDER}"
	WriteUninstaller "$INSTDIR\${UNINSTALL_FOLDER}\uninstall.exe"
	WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayName" "${PRODUCT_NAME}"
	WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayVersion" "${VERSION}"
	WriteRegStr HKLM "${UNINSTALL_KEY}" "Publisher" "${COMPANY}"
	WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\${INSTANCE_NAME}.exe,0"
	WriteRegStr HKLM "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
	WriteRegStr HKLM "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\${UNINSTALL_FOLDER}\uninstall.exe"'
	WriteRegStr HKLM "${UNINSTALL_KEY}" "QuietUninstallString" '"$INSTDIR\${UNINSTALL_FOLDER}\uninstall.exe" /S'
	WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoModify" 1
	WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoRepair" 1
	${GetSize} "$INSTDIR\${VERSION}" "/S=0K" $0 $1 $2
	WriteRegDWORD HKLM "${UNINSTALL_KEY}" "EstimatedSize" $0

	${If} $IsServerCore != 1
		CreateShortcut "$SMPROGRAMS\${PRODUCT_NAME}.lnk" "$INSTDIR\${INSTANCE_NAME}.exe"
		CreateShortcut "$DESKTOP\${PRODUCT_NAME}.lnk" "$INSTDIR\${INSTANCE_NAME}.exe"
		WriteRegStr HKLM "${RUN_KEY}" "${INSTANCE_NAME}" '"$INSTDIR\${INSTANCE_NAME}.exe" ui --tray'
	${EndIf}

	; the service, and the root on the PATH, by the app itself
	DetailPrint "Registering the ${INSTANCE_NAME} service..."
	nsExec::ExecToLog '"$INSTDIR\${COMMAND_NAME}.exe" service install'
	Pop $0
	${If} $0 != 0
		MessageBox MB_ICONSTOP "${PRODUCT_NAME} could not register its service (exit $0). The details above say why." /SD IDOK
		SetErrorLevel 4
		Abort
	${EndIf}
	; through the service manager: the app's own start waits for a setup that holds the mutex, as this one does
	nsExec::ExecToLog '"$SYSDIR\sc.exe" start "${INSTANCE_NAME}"'
	Pop $0
	${If} $0 != 0
		DetailPrint "The service did not start (exit $0); the window starts it when opened."
	${EndIf}

	; the updater's task, as the choice says
	InitPluginsDir
	File "/oname=$PLUGINSDIR\task.ps1" "${__FILEDIR__}\install-client-task.ps1"
	nsExec::ExecToLog '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "$PLUGINSDIR\task.ps1" -taskName "${INSTANCE_NAME}Updater" -scriptPath "$INSTDIR\vhupdate.ps1" -choice "$AutoUpdateChoice"'
	Pop $0
	${If} $0 != 0
		DetailPrint "The updater's task could not be registered (exit $0): ${PRODUCT_NAME} updates when asked to."
	${EndIf}

	Call RemoveOldVersions
SectionEnd

; An install that failed after it stopped the service leaves none running: the one there is started again, as the
; Linux install script's exit does, and opens again the windows and trays this closed.
Function .onInstFailed
	nsExec::Exec '"$SYSDIR\sc.exe" start "${INSTANCE_NAME}"'
	Pop $0
FunctionEnd

Function un.onInit
	SetShellVarContext all
	SetRegView 64
	; the uninstaller sits a folder below the root
	ReadRegStr $INSTDIR HKLM "${UNINSTALL_KEY}" "InstallLocation"
	${If} $INSTDIR == ""
		MessageBox MB_ICONSTOP "${PRODUCT_NAME} is not installed." /SD IDOK
		Abort
	${EndIf}
FunctionEnd

; The settings, the profiles and the log stay in the service's storage (ProgramData) for a later
; install, as the Linux uninstaller keeps them.
Section "Uninstall"
	; the updater's task first, which would install the app again; then its downloads that BITS goes on with, SYSTEM's
	; jobs, named as vhupdate.ps1's Get-JobName names them
	nsExec::ExecToLog '"$SYSDIR\schtasks.exe" /Delete /TN "${INSTANCE_NAME}Updater" /F'
	Pop $0
	nsExec::ExecToLog `"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -Command "Get-BitsTransfer -AllUsers | Where-Object DisplayName -like '${INSTANCE_NAME} update *' | Remove-BitsTransfer"`
	Pop $0

	; the service, and the root off the PATH, by the app itself
	nsExec::ExecToLog '"$INSTDIR\${COMMAND_NAME}.exe" service uninstall'
	Pop $0
	Push "$INSTDIR"
	Call un.CloseProcesses

	DeleteRegValue HKLM "${RUN_KEY}" "${INSTANCE_NAME}"
	Delete "$SMPROGRAMS\${PRODUCT_NAME}.lnk"
	Delete "$DESKTOP\${PRODUCT_NAME}.lnk"
	DeleteRegKey HKLM "${UNINSTALL_KEY}"

	; the junction first, as a link: removing it never reaches into the folder it names
	RMDir "$INSTDIR\${CURRENT_FOLDER}"
	RMDir /r /REBOOTOK "$INSTDIR"
SectionEnd
