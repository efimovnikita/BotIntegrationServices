@echo off

:: Read the current version from version.txt
set /p VERSION=<version.txt

:: Split the version into major, minor, and patch
for /f "tokens=1,2,3 delims=." %%a in ("%VERSION%") do (
    set MAJOR=%%a
    set MINOR=%%b
    set /a PATCH=%%c + 1
)

:: Create the new version string
set NEW_VERSION=%MAJOR%.%MINOR%.%PATCH%

:: Update the version.txt file
echo %NEW_VERSION%>version.txt

:: Run the publish command with the new version
dotnet publish -r win-x64 -c Release /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false /p:Version=%NEW_VERSION%

echo Published version: %NEW_VERSION%