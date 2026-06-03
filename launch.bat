@echo off
echo Stopping existing processes...
taskkill /f /im "dotnet.exe" >nul 2>&1
taskkill /f /im "azurite" >nul 2>&1
taskkill /f /im "node.exe" >nul 2>&1

timeout /t 2 /nobreak > nul

echo Starting Azurite...
start "Azurite" cmd /k "azurite --silent --location C:\azurite --debug C:\azurite\debug.log"

timeout /t 2 /nobreak > nul

echo Starting MoneyBoardApi...
start "MoneyBoardApi" cmd /k "cd C:\Development\moneyboard\MoneyBoardApi && dotnet run"

timeout /t 3 /nobreak > nul

echo Starting MoneyBoard...
start "MoneyBoard" cmd /k "cd C:\Development\moneyboard\MoneyBoard && dotnet run"

echo All services started.