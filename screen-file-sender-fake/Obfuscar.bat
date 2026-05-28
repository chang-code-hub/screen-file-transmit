@echo off

set cmd_str="%1" "%2"
set "dotnetPath=C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref"
set "searchPrefix=8.0"

for /d %%d in ("%dotnetPath%\%searchPrefix%*") do (
	set "SDK_DIR=%%d\ref\net8.0\"
)

echo SDKDIR = %SDK_DIR% 
echo CMD_STR = %cmd_str%

cmd /C "%cmd_str%"