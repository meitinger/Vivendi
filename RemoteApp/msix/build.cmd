@SET ROOT=%~dp0
@SET BIN=%ROOT%..\bin\Installer\
@SET OBJ=%ROOT%..\obj\Installer\
IF NOT EXIST "%BIN%" MKDIR "%BIN%"
IF NOT EXIST "%OBJ%" MKDIR "%OBJ%"
makepri.exe new /ConfigXml "%ROOT%PriConfig.xml" /ProjectRoot "%ROOT%." /Manifest "%ROOT%AppxManifest.xml" /OutputFile "%OBJ%Resources.pri" /Overwrite
makeappx.exe build /v /o /f "%ROOT%PackagingLayout.xml" /op "%BIN%."
