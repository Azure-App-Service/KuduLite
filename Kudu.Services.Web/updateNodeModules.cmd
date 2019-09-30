@echo off
setlocal enabledelayedexpansion

pushd %1

echo Hello World
echo %1 

set attempts=5
set counter=0

echo.
echo Installing Kudu Script
echo.

:retry
set /a counter+=1
echo Attempt %counter% out of %attempts%

cmd /c npm --loglevel=error install https://github.com/projectkudu/KuduScript/tarball/536355cdbe75807ce97b8b4e2d22f36106e9008e
IF %ERRORLEVEL% NEQ 0 goto error
if not exist "KuduConsole" mkdir KuduConsole
xcopy node_modules KuduConsole
goto end

:error
if %counter% GEQ %attempts% goto :lastError
goto retry

:lastError
popd
echo An error has occured during npm install.
exit /b 1


:end
popd
echo Finished successfully.
exit /b 0