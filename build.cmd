@echo Off
set config=%1
if "%config%" == "" (
   set config=Debug
)

set version=
set versionPlaceholder=
if not "%PackageVersion%" == "" (
   set version=-Version %PackageVersion%
   set versionPlaceholder=-p packageVersion=%PackageVersion%
) else (
   set version=-Version 1.0.0-build
   set versionPlaceholder=-p packageVersion=1.0.0-build
)


REM Determine msbuild path
set msbuildtmp="%ProgramFiles%\MSBuild\14.0\bin\msbuild"
if exist %msbuildtmp% set msbuild=%msbuildtmp%
set msbuildtmp="%ProgramFiles(x86)%\MSBuild\14.0\bin\msbuild"
if exist %msbuildtmp% set msbuild=%msbuildtmp%
set VisualStudioVersion=14.0


REM Package restore
call :ExecuteCmd tools\nuget.exe restore NuGet.Server.sln -OutputDirectory %cd%\packages -NonInteractive
IF %ERRORLEVEL% NEQ 0 goto error


REM Build
call :ExecuteCmd %msbuild% "NuGet.Server.sln" /p:Configuration="%config%" /m /v:M /fl /flp:LogFile=msbuild.log;Verbosity=Normal /nr:false
IF %ERRORLEVEL% NEQ 0 goto error


REM Test
call :ExecuteCmd tools\nuget.exe install xunit.runner.console -Version 2.1.0 -OutputDirectory packages
call :ExecuteCmd packages\xunit.runner.console.2.1.0\tools\xunit.console.exe test\NuGet.Server.Core.Tests\bin\%config%\NuGet.Server.Core.Tests.dll
IF %ERRORLEVEL% NEQ 0 goto error
call :ExecuteCmd packages\xunit.runner.console.2.1.0\tools\xunit.console.exe test\NuGet.Server.V2.Tests\bin\%config%\NuGet.Server.V2.Tests.dll
IF %ERRORLEVEL% NEQ 0 goto error
call :ExecuteCmd packages\xunit.runner.console.2.1.0\tools\xunit.console.exe test\NuGet.Server.Tests\bin\%config%\NuGet.Server.Tests.dll
IF %ERRORLEVEL% NEQ 0 goto error


REM Package
mkdir artifacts
mkdir artifacts\packages
call :ExecuteCmd tools\nuget.exe pack "src\NuGet.Server.Core\NuGet.Server.Core.csproj" -symbols -o artifacts\packages -p Configuration=%config% %versionPlaceholder% %version%
IF %ERRORLEVEL% NEQ 0 goto error
call :ExecuteCmd tools\nuget.exe pack "src\NuGet.Server.V2\NuGet.Server.V2.csproj" -symbols -o artifacts\packages -p Configuration=%config% %versionPlaceholder% %version%
IF %ERRORLEVEL% NEQ 0 goto error
call :ExecuteCmd tools\nuget.exe pack "src\NuGet.Server\NuGet.Server.nuspec" -symbols -o artifacts\packages -p Configuration=%config% %versionPlaceholder% %version%
IF %ERRORLEVEL% NEQ 0 goto error


goto end

:: Execute command routine that will echo out when error
:ExecuteCmd
setlocal
set _CMD_=%*
call %_CMD_%
if "%ERRORLEVEL%" NEQ "0" echo Failed exitCode=%ERRORLEVEL%, command=%_CMD_%
exit /b %ERRORLEVEL%

:error
endlocal
echo An error has occurred during build.
call :exitSetErrorLevel
call :exitFromFunction 2>nul

:exitSetErrorLevel
exit /b 1

:exitFromFunction
()

:end
endlocal
echo Build finished successfully.
