@echo off
echo Publishing WAVFileUploaderUpdater...
dotnet publish -r win-x64 -c Release /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false
echo Publishing completed.
pause