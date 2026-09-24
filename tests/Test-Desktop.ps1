param(
    [Parameter(Mandatory = $true)][int]$AppProcessId,
    [Parameter(Mandatory = $true)][string]$VideoPath,
    [string]$ReportPath = 'tests\artifacts\desktop-report.json',
    [switch]$CloseWhileConverting,
    [switch]$FileAlreadySelected
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class DesktopNative {
 [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr SendMessageTimeout(IntPtr h,uint m,IntPtr w,IntPtr l,uint f,uint t,out IntPtr r);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int c);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr a,int x,int y,int w,int z,uint f);
}
'@
$script:Steps = [System.Collections.Generic.List[object]]::new()
$desktop = [System.Windows.Automation.AutomationElement]::RootElement
$app = Get-Process -Id $AppProcessId
$app.Refresh()
$windowHandle = $app.MainWindowHandle
if ($windowHandle -eq [IntPtr]::Zero) { throw 'The process has no top-level window.' }
$script:Window = [System.Windows.Automation.AutomationElement]::FromHandle($windowHandle)

function Find-Control([string]$Id, [string]$Name, [System.Windows.Automation.AutomationElement]$Root = $script:Window) {
    if ($Id) { $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id) }
    else { $condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, $Name) }
    return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Wait-Control([string]$Id, [string]$Name, [int]$TimeoutSeconds = 15) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        $element = Find-Control -Id $Id -Name $Name
        if ($element -and -not $element.Current.IsOffscreen) { return $element }
        Start-Sleep -Milliseconds 200
    }
    throw "Control not found: id=$Id name=$Name"
}

function Activate-Control($Element) {
    if (-not $Element) { throw 'Cannot activate a missing control.' }
    $pattern = $null
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) { $pattern.Invoke(); return }
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) { $pattern.Select(); return }
    throw "Control does not expose Invoke or SelectionItem: $($Element.Current.Name)"
}

function Record-Ping([string]$Name) {
    $value = [IntPtr]::Zero
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $ok = [DesktopNative]::SendMessageTimeout($windowHandle, 0, [IntPtr]::Zero, [IntPtr]::Zero, 2, 2000, [ref]$value)
    $watch.Stop()
    if ($ok -eq [IntPtr]::Zero) { throw "Window stopped answering WM_NULL during $Name" }
    $script:Steps.Add([pscustomobject]@{ step = $Name; utc = [DateTimeOffset]::UtcNow; windowPingMilliseconds = $watch.Elapsed.TotalMilliseconds })
    Write-Output "PASS $Name ($([Math]::Round($watch.Elapsed.TotalMilliseconds, 2)) ms)"
}

function Choose-Video {
    Activate-Control (Wait-Control -Id 'ChooseFileButton')
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $dialog = $null
    while ($watch.Elapsed.TotalSeconds -lt 20 -and -not $dialog) {
        $dialogCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ClassNameProperty, '#32770')
        $windows = $script:Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $dialogCondition)
        foreach ($candidate in $windows) {
            if ($candidate.Current.ClassName -eq '#32770') { $dialog = $candidate; break }
        }
        if (-not $dialog) { Start-Sleep -Milliseconds 200 }
    }
    if (-not $dialog) { throw 'Native file picker was not found.' }
    $edit = Find-Control -Id '1148' -Root $dialog
    $pattern = $null
    if (-not $edit -or -not $edit.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$pattern)) {
        $editCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
        $edits = $dialog.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCondition)
        foreach ($candidate in $edits) {
            if ($candidate.Current.Name -match 'File name|Nome') { $edit = $candidate; break }
        }
    }
    $pattern = $null
    if ($edit -and $edit.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$pattern)) { $pattern.SetValue((Resolve-Path -LiteralPath $VideoPath).Path) }
    else { throw 'Native picker filename field does not expose ValuePattern.' }
    Activate-Control (Find-Control -Id '1' -Root $dialog)
    $null = Wait-Control -Id 'ConvertButton'
    $null = Wait-Control -Name ([IO.Path]::GetFileName($VideoPath))
    Start-Sleep -Milliseconds 500
    $comboCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ComboBox)
    $combo = $script:Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $comboCondition)
    $selection = $combo.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).GetCurrentSelection()
    if ($selection.Count -gt 0 -and $selection[0].Current.Name -eq 'GIF') {
        $script:Steps.Add([pscustomobject]@{ step = 'Native file picker and compatible GIF selection'; utc = [DateTimeOffset]::UtcNow })
        return
    }
    $expand = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    Start-Sleep -Milliseconds 200
    $gif = Find-Control -Name 'GIF'
    if (-not $gif) { $gif = Find-Control -Name 'GIF' -Root $desktop }
    Activate-Control $gif
    $script:Steps.Add([pscustomobject]@{ step = 'Native file picker and GIF selection'; utc = [DateTimeOffset]::UtcNow })
}

try {
    $null = [DesktopNative]::ShowWindow($windowHandle, 9)
    $null = [DesktopNative]::SetForegroundWindow($windowHandle)
    Record-Ping 'Idle window'
    if (-not $FileAlreadySelected) { Choose-Video }
    else { $null = Wait-Control -Name ([IO.Path]::GetFileName($VideoPath)) }
    Activate-Control (Wait-Control -Id 'ConvertButton')
    $null = Wait-Control -Id 'CancelButton'
    Start-Sleep -Seconds 2
    Record-Ping 'Active conversion'

    $historyName = 'Hist' + [char]0x00F3 + 'rico'
    Activate-Control (Wait-Control -Name $historyName)
    Start-Sleep -Milliseconds 250
    Record-Ping 'History navigation during conversion'

    $settingsName = 'Configura' + [char]0x00E7 + [char]0x00F5 + 'es'
    $settings = Find-Control -Name $settingsName
    if (-not $settings) { $settings = Find-Control -Name 'Settings' }
    Activate-Control $settings
    Start-Sleep -Milliseconds 250
    Record-Ping 'Settings navigation during conversion'

    $rect = $script:Window.Current.BoundingRectangle
    $null = [DesktopNative]::SetWindowPos($windowHandle, [IntPtr]::Zero, [int]($rect.X + 20), [int]($rect.Y + 20), 0, 0, 5)
    Record-Ping 'Move window during conversion'
    $null = [DesktopNative]::ShowWindow($windowHandle, 6)
    Start-Sleep -Milliseconds 300
    $null = [DesktopNative]::ShowWindow($windowHandle, 9)
    Record-Ping 'Minimize and restore during conversion'
    Activate-Control (Wait-Control -Name 'Converter')
    Activate-Control (Wait-Control -Id 'CancelButton')

    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt 15) {
        $cancel = Find-Control -Id 'CancelButton'
        if (-not $cancel -or $cancel.Current.IsOffscreen) { break }
        Start-Sleep -Milliseconds 200
    }
    if ($watch.Elapsed.TotalSeconds -ge 15) { throw 'Cancel button remained visible for more than 15 seconds.' }
    Record-Ping 'Cancellation completed'
    $script:Steps.Add([pscustomobject]@{ step = 'Cancellation latency'; milliseconds = $watch.Elapsed.TotalMilliseconds })

    if ($CloseWhileConverting) {
        Activate-Control (Wait-Control -Id 'ConvertButton')
        $null = Wait-Control -Id 'CancelButton'
        Start-Sleep -Seconds 1
        $app.Refresh()
        $null = $app.CloseMainWindow()
        if (-not $app.WaitForExit(15000)) { throw 'Application did not close within 15 seconds while converting.' }
        $script:Steps.Add([pscustomobject]@{ step = 'Graceful close during active conversion'; utc = [DateTimeOffset]::UtcNow })
    }
    $status = 'passed'
} catch {
    $status = 'failed'
    $script:Steps.Add([pscustomobject]@{ step = 'Failure'; error = $_.Exception.ToString() })
    throw
} finally {
    $directory = Split-Path -Parent $ReportPath
    if ($directory) { $null = New-Item -ItemType Directory -Force -Path $directory }
    [pscustomobject]@{ utc = [DateTimeOffset]::UtcNow; status = $status; appProcessId = $AppProcessId; videoPath = $VideoPath; nativePickerExercised = -not $FileAlreadySelected; steps = $script:Steps; limitation = 'WM_NULL verifies message-loop responses, not frame pacing. Navigation, minimize/restore and cancellation were exercised through UI Automation. Native picker is only exercised when FileAlreadySelected is absent. Drag-and-drop remains a separate check.' } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
}
