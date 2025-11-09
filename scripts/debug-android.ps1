
# Avvia emulatore Android se nessun device fisico è collegato e lancia il debug MAUI
$adb = "$env:ANDROID_HOME\platform-tools\adb.exe"
$emulator = "$env:ANDROID_HOME\emulator\emulator.exe"

# Trova il primo AVD disponibile
if (!(Test-Path $emulator)) {
    Write-Error "emulator.exe non trovato. Verifica che ANDROID_HOME sia impostato correttamente."
    exit 1
}
$avdList = & $emulator -list-avds
if ($avdList.Count -eq 0) {
    Write-Error "Nessun emulatore AVD trovato. Creane uno con Android Studio."
    exit 1
}
$avdName = $avdList[0]
Write-Host "Userò l'emulatore: $avdName"


# Controlla se adb è disponibile
if (!(Test-Path $adb)) {
    Write-Error "adb non trovato. Verifica che ANDROID_HOME sia impostato correttamente."
    exit 1
}

# Controlla se c'è un device fisico collegato
$devices = & $adb devices | Select-String -Pattern "^([a-zA-Z0-9]+)\s+device$" | ForEach-Object { $_.Matches[0].Groups[1].Value }
if ($devices.Count -gt 0) {
    Write-Host "Device fisico collegato: $($devices -join ", "). Avvio debug su device."
} else {
    # Controlla se l'emulatore è già avviato
    $emulators = & $adb devices | Select-String -Pattern "^emulator-([0-9]+)\s+device$"
    if ($emulators.Count -eq 0) {
        Write-Host "Nessun emulatore avviato. Avvio emulatore $avdName..."
        Start-Process -NoNewWindow -FilePath $emulator -ArgumentList "-avd $avdName" | Out-Null
        Start-Sleep -Seconds 10
        # Attendi che l'emulatore sia pronto
        $maxTries = 30
        $tries = 0
        do {
            Start-Sleep -Seconds 2
            $emulators = & $adb devices | Select-String -Pattern "^emulator-([0-9]+)\s+device$"
            $tries++
        } while ($emulators.Count -eq 0 -and $tries -lt $maxTries)
        if ($emulators.Count -eq 0) {
            Write-Error "Emulatore non avviato."
            exit 1
        }
        Write-Host "Emulatore avviato."
    } else {
        Write-Host "Emulatore già avviato."
    }
}

# Avvia il debug MAUI Android
Write-Host "Avvio debug MAUI Android..."
dotnet build ClickPay/ClickPay.csproj -f net9.0-android
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build fallita."
    exit 1
}
dotnet run --project ClickPay/ClickPay.csproj -f net9.0-android
