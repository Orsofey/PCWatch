# PC Watch — монитор нагрузки без драйверов: только встроенные счётчики Windows (CIM/WMI).
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

try {
# ---------- Тема ----------
function C($hex) { [Drawing.ColorTranslator]::FromHtml($hex) }
$T = @{
    Bg = C '#16171C'; Card = C '#22242B'; Card2 = C '#2B2E37'; Text = C '#ECECF1'; Sub = C '#9496A1'
    Line = C '#30333C'; Sel = C '#3A4A6B'; Ok = C '#1F3A2A'; OkText = C '#6FE3A0'; Bad = C '#45222A'; BadText = C '#FF8A9A'
    Danger = C '#D9434F'; Btn = C '#2F323C'; Heat = C '#E8833A'
}
$F = @{
    Base = New-Object Drawing.Font('Segoe UI', 10)
    Small = New-Object Drawing.Font('Segoe UI', 8.5)
    Big = New-Object Drawing.Font('Segoe UI Semibold', 20)
    Bold = New-Object Drawing.Font('Segoe UI Semibold', 10)
}
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

function Set-DoubleBuffer($ctl) {
    $ctl.GetType().GetProperty('DoubleBuffered', [Reflection.BindingFlags]'NonPublic,Instance').SetValue($ctl, $true, $null)
}

# ---------- Сбор данных в фоне, чтобы окно не подвисало ----------
$sync = [hashtable]::Synchronized(@{ Data = $null; Run = $true; Error = $null })
$collector = {
    $cores = (Get-CimInstance Win32_ComputerSystem).NumberOfLogicalProcessors
    while ($sync.Run) {
        try {
            $cpu = [int](Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor -Filter "Name='_Total'").PercentProcessorTime
            $os = Get-CimInstance Win32_OperatingSystem
            $ramTotal = $os.TotalVisibleMemorySize / 1MB
            $ramUsed = $ramTotal - $os.FreePhysicalMemory / 1MB
            $disk = Get-CimInstance Win32_PerfFormattedData_PerfDisk_PhysicalDisk -Filter "Name='_Total'"
            $c = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'"

            $gpu = $null
            try {
                $eng = Get-CimInstance Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine -ErrorAction Stop
                if ($eng) {
                    $gpu = ($eng | Group-Object { $_.Name -replace '^.*engtype_', '' } |
                        ForEach-Object { ($_.Group | Measure-Object UtilizationPercentage -Sum).Sum } |
                        Measure-Object -Maximum).Maximum
                    $gpu = [int][math]::Min(100, $gpu)
                }
            } catch {}

            $net = Get-CimInstance Win32_PerfFormattedData_Tcpip_NetworkInterface |
                Measure-Object BytesReceivedPersec, BytesSentPersec -Sum

            $paths = @{}
            foreach ($p in Get-CimInstance Win32_Process -Property ProcessId, ExecutablePath) {
                if ($p.ExecutablePath) { $paths[[int]$p.ProcessId] = $p.ExecutablePath }
            }
            $procs = Get-CimInstance Win32_PerfFormattedData_PerfProc_Process |
                Where-Object { $_.Name -ne '_Total' -and $_.Name -ne 'Idle' -and $_.IDProcess -ne 0 } |
                Group-Object { $_.Name -replace '#\d+$', '' } |
                ForEach-Object {
                    $pids = @($_.Group | ForEach-Object { [int]$_.IDProcess })
                    $path = $pids | ForEach-Object { $paths[$_] } | Where-Object { $_ } | Select-Object -First 1
                    [pscustomobject]@{
                        Name = $_.Name; Pids = $pids; Count = $pids.Count; Path = $path
                        Cpu = [math]::Round(($_.Group | Measure-Object PercentProcessorTime -Sum).Sum / $cores, 1)
                        Ram = [math]::Round(($_.Group | Measure-Object WorkingSetPrivate -Sum).Sum / 1MB)
                    }
                }

            $sync.Data = @{
                Cpu = $cpu; RamUsed = $ramUsed; RamTotal = $ramTotal; RamPct = [int](100 * $ramUsed / $ramTotal)
                DiskPct = [int][math]::Min(100, 100 - $disk.PercentIdleTime)
                DiskIO = $disk.DiskReadBytesPersec + $disk.DiskWriteBytesPersec
                CFree = [int](100 * $c.FreeSpace / $c.Size); Gpu = $gpu
                NetIn = $net[0].Sum; NetOut = $net[1].Sum; Procs = @($procs)
            }
            $sync.Error = $null
        } catch { $sync.Error = $_.Exception.Message }
        Start-Sleep -Milliseconds 1500
    }
}
$rs = [runspacefactory]::CreateRunspace(); $rs.Open()
$rs.SessionStateProxy.SetVariable('sync', $sync)
$bg = [powershell]::Create(); $bg.Runspace = $rs
[void]$bg.AddScript($collector); [void]$bg.BeginInvoke()

# ---------- Окно ----------
$form = New-Object Windows.Forms.Form
$form.Text = 'PC Watch' + $(if ($isAdmin) { '  —  администратор' } else { '' })
$form.Size = New-Object Drawing.Size(1040, 760)
$form.MinimumSize = New-Object Drawing.Size(900, 560)
$form.StartPosition = 'CenterScreen'
$form.BackColor = $T.Bg; $form.ForeColor = $T.Text; $form.Font = $F.Base

# Иконка окна: синий квадрат с пульсом
$bmp = New-Object Drawing.Bitmap(32, 32)
$g = [Drawing.Graphics]::FromImage($bmp); $g.SmoothingMode = 'AntiAlias'
$g.FillRectangle((New-Object Drawing.SolidBrush (C '#3D7BFF')), 2, 2, 28, 28)
$g.DrawLines((New-Object Drawing.Pen([Drawing.Color]::White, 2.5)),
    [Drawing.Point[]]@((New-Object Drawing.Point(5, 17)), (New-Object Drawing.Point(11, 17)), (New-Object Drawing.Point(14, 9)),
        (New-Object Drawing.Point(18, 24)), (New-Object Drawing.Point(21, 15)), (New-Object Drawing.Point(27, 15))))
$g.Dispose()
$form.Icon = [Drawing.Icon]::FromHandle($bmp.GetHicon())

# Тёмный заголовок окна (Windows 10 20H1+/11)
try {
    Add-Type -Namespace PCW -Name Dwm -MemberDefinition '[DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(System.IntPtr h, int a, ref int v, int s);'
    $form.Add_HandleCreated({ $v = 1; [void][PCW.Dwm]::DwmSetWindowAttribute($form.Handle, 20, [ref]$v, 4) })
} catch {}

# ---------- Карточки показателей ----------
$top = New-Object Windows.Forms.FlowLayoutPanel
$top.Dock = 'Top'; $top.Height = 148; $top.Padding = New-Object Windows.Forms.Padding(10, 12, 10, 4)
$top.BackColor = $T.Bg; $top.WrapContents = $false

$sparkPaint = {
    param($s, $e)
    $d = $s.Tag; $h = $d.Hist
    $g = $e.Graphics; $g.SmoothingMode = 'AntiAlias'
    $barY = 92
    $b1 = New-Object Drawing.SolidBrush $T.Card2
    $g.FillRectangle($b1, 14, $barY, $s.Width - 28, 4); $b1.Dispose()
    $max = 100
    if ($h.Count -gt 0) {
        $max = if ($d.Max) { $d.Max } else { [math]::Max(1KB, ($h | Measure-Object -Maximum).Maximum) }
        $w = [math]::Min(1, $h[$h.Count - 1] / $max) * ($s.Width - 28)
        $b2 = New-Object Drawing.SolidBrush $d.Color
        $g.FillRectangle($b2, 14, $barY, [single]$w, 4); $b2.Dispose()
    }
    if ($h.Count -lt 2) { return }
    $n = 60; $bottom = $s.Height - 2; $ch = 26; $step = ($s.Width - 4) / ($n - 1)
    $pts = New-Object 'System.Collections.Generic.List[System.Drawing.PointF]'
    $start = $n - $h.Count
    for ($i = 0; $i -lt $h.Count; $i++) {
        $pts.Add((New-Object Drawing.PointF([single](2 + ($start + $i) * $step), [single]($bottom - [math]::Min(1, $h[$i] / $max) * $ch))))
    }
    $poly = New-Object 'System.Collections.Generic.List[System.Drawing.PointF]'
    $poly.AddRange($pts)
    $poly.Add((New-Object Drawing.PointF($pts[$pts.Count - 1].X, [single]$bottom)))
    $poly.Add((New-Object Drawing.PointF($pts[0].X, [single]$bottom)))
    $fill = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(45, $d.Color))
    $pen = New-Object Drawing.Pen($d.Color, 1.8)
    $g.FillPolygon($fill, $poly.ToArray()); $g.DrawLines($pen, $pts.ToArray())
    $fill.Dispose(); $pen.Dispose()
}

function New-Card($title, $hex, $max) {
    $p = New-Object Windows.Forms.Panel
    $p.Size = New-Object Drawing.Size(186, 128); $p.Margin = New-Object Windows.Forms.Padding(6, 0, 6, 0)
    $p.BackColor = $T.Card
    $p.Tag = @{ Hist = New-Object 'System.Collections.Generic.List[double]'; Color = (C $hex); Max = $max }
    Set-DoubleBuffer $p
    $p.Add_Paint($sparkPaint)
    $lt = New-Object Windows.Forms.Label
    $lt.Text = $title.ToUpper(); $lt.Font = $F.Small; $lt.ForeColor = (C $hex); $lt.BackColor = 'Transparent'
    $lt.Location = New-Object Drawing.Point(12, 10); $lt.AutoSize = $true
    $lv = New-Object Windows.Forms.Label
    $lv.Text = '—'; $lv.Font = $F.Big; $lv.ForeColor = $T.Text; $lv.BackColor = 'Transparent'
    $lv.Location = New-Object Drawing.Point(9, 26); $lv.AutoSize = $true
    $ls = New-Object Windows.Forms.Label
    $ls.Text = ''; $ls.Font = $F.Small; $ls.ForeColor = $T.Sub; $ls.BackColor = 'Transparent'
    $ls.Location = New-Object Drawing.Point(12, 68); $ls.Size = New-Object Drawing.Size(168, 18)
    $p.Controls.AddRange(@($lt, $lv, $ls))
    $top.Controls.Add($p)
    @{ Panel = $p; Value = $lv; Sub = $ls }
}
$cCpu  = New-Card 'Процессор'  '#4FA3FF' 100
$cRam  = New-Card 'Память'     '#B07CFF' 100
$cDisk = New-Card 'Диск'       '#4CD27A' 100
$cGpu  = New-Card 'Видеокарта' '#FFB347' 100
$cNet  = New-Card 'Сеть'       '#FF6B9D' $null

function Set-Card($c, $histVal, $value, $sub) {
    $h = $c.Panel.Tag.Hist
    $h.Add([double]$histVal); while ($h.Count -gt 60) { $h.RemoveAt(0) }
    $c.Value.Text = $value; $c.Sub.Text = $sub
    $c.Panel.Invalidate()
}

# ---------- Плашка-диагноз ----------
$banner = New-Object Windows.Forms.Panel
$banner.Dock = 'Top'; $banner.Height = 44; $banner.Padding = New-Object Windows.Forms.Padding(16, 0, 16, 6)
$banner.BackColor = $T.Bg
$diag = New-Object Windows.Forms.Label
$diag.Dock = 'Fill'; $diag.TextAlign = 'MiddleLeft'; $diag.Font = $F.Bold
$diag.Padding = New-Object Windows.Forms.Padding(12, 0, 0, 0)
$diag.BackColor = $T.Ok; $diag.ForeColor = $T.OkText; $diag.Text = 'Собираю данные...'
$banner.Controls.Add($diag)

# ---------- Таблица приложений ----------
$grid = New-Object Windows.Forms.DataGridView
$grid.Dock = 'Fill'
$grid.BackgroundColor = $T.Bg; $grid.BorderStyle = 'None'; $grid.GridColor = $T.Line
$grid.CellBorderStyle = 'SingleHorizontal'; $grid.RowHeadersVisible = $false
$grid.AllowUserToAddRows = $false; $grid.AllowUserToDeleteRows = $false; $grid.AllowUserToResizeRows = $false
$grid.ReadOnly = $true; $grid.SelectionMode = 'FullRowSelect'; $grid.MultiSelect = $false
$grid.EnableHeadersVisualStyles = $false; $grid.ColumnHeadersBorderStyle = 'None'
$grid.ColumnHeadersHeightSizeMode = 'DisableResizing'; $grid.ColumnHeadersHeight = 34
$grid.RowTemplate.Height = 34; $grid.AutoSizeColumnsMode = 'Fill'
$grid.DefaultCellStyle.BackColor = $T.Bg; $grid.DefaultCellStyle.ForeColor = $T.Text
$grid.DefaultCellStyle.SelectionBackColor = $T.Sel; $grid.DefaultCellStyle.SelectionForeColor = $T.Text
$grid.DefaultCellStyle.Font = $F.Base
$grid.DefaultCellStyle.Padding = New-Object Windows.Forms.Padding(6, 0, 6, 0)
$grid.ColumnHeadersDefaultCellStyle.BackColor = $T.Card; $grid.ColumnHeadersDefaultCellStyle.ForeColor = $T.Sub
$grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = $T.Card
$grid.ColumnHeadersDefaultCellStyle.Font = $F.Small
$grid.ColumnHeadersDefaultCellStyle.Padding = New-Object Windows.Forms.Padding(6, 0, 6, 0)
Set-DoubleBuffer $grid

$colIcon = New-Object Windows.Forms.DataGridViewImageColumn
$colIcon.Name = 'Icon'; $colIcon.HeaderText = ''; $colIcon.ImageLayout = 'Zoom'
$colIcon.AutoSizeMode = 'None'; $colIcon.Width = 44
$colIcon.DefaultCellStyle.Padding = New-Object Windows.Forms.Padding(10, 6, 6, 6)
[void]$grid.Columns.Add($colIcon)
foreach ($c in @(
    @('Title', 'ПРИЛОЖЕНИЕ', 40), @('Exe', 'ПРОЦЕСС', 22), @('Count', 'ЭКЗ.', 8),
    @('Cpu', 'ЦП', 12), @('Ram', 'ПАМЯТЬ', 14), @('Key', 'Key', 1))) {
    $col = New-Object Windows.Forms.DataGridViewTextBoxColumn
    $col.Name = $c[0]; $col.HeaderText = $c[1]; $col.FillWeight = $c[2]; $col.SortMode = 'NotSortable'
    [void]$grid.Columns.Add($col)
}
$grid.Columns['Key'].Visible = $false
$grid.Columns['Exe'].DefaultCellStyle.ForeColor = $T.Sub
foreach ($n in 'Count', 'Cpu', 'Ram') {
    $grid.Columns[$n].DefaultCellStyle.Alignment = 'MiddleRight'
    $grid.Columns[$n].HeaderCell.Style.Alignment = 'MiddleRight'
}

# ---------- Нижняя панель ----------
$bottom = New-Object Windows.Forms.Panel
$bottom.Dock = 'Bottom'; $bottom.Height = 58; $bottom.BackColor = $T.Card

function New-Btn($text, $x, $w, $color) {
    $b = New-Object Windows.Forms.Button
    $b.Text = $text; $b.Location = New-Object Drawing.Point($x, 11); $b.Size = New-Object Drawing.Size($w, 36)
    $b.FlatStyle = 'Flat'; $b.FlatAppearance.BorderSize = 0; $b.BackColor = $color; $b.ForeColor = $T.Text
    $b.Font = $F.Bold; $b.Cursor = 'Hand'
    $bottom.Controls.Add($b); $b
}
$btnKill = New-Btn 'Снять задачу' 14 150 $T.Danger
$btnSortCpu = New-Btn 'По процессору' 178 140 $T.Sel
$btnSortRam = New-Btn 'По памяти' 322 120 $T.Btn
$btnAdmin = New-Btn 'Режим администратора' 456 200 $T.Btn
$btnAdmin.Visible = -not $isAdmin

$chkLog = New-Object Windows.Forms.CheckBox
$chkLog.Text = 'Лог в CSV'; $chkLog.ForeColor = $T.Sub; $chkLog.AutoSize = $true
$chkLog.Top = 19
$bottom.Controls.Add($chkLog)
$bottom.Add_Resize({ $chkLog.Left = $bottom.Width - $chkLog.Width - 16 })

# ---------- Контекстное меню ----------
$menu = New-Object Windows.Forms.ContextMenuStrip
$menu.BackColor = $T.Card; $menu.ForeColor = $T.Text; $menu.ShowImageMargin = $false
$miKill = $menu.Items.Add('Снять задачу')
$miOpen = $menu.Items.Add('Открыть расположение файла')
$miSearch = $menu.Items.Add('Что это за процесс? (поиск в интернете)')
$grid.ContextMenuStrip = $menu

# Порядок: последний добавленный докается первым (самым внешним)
$form.Controls.AddRange(@($grid, $bottom, $banner, $top))
$grid.BringToFront()

# ---------- Логика ----------
$script:sortBy = 'Cpu'
$script:procMap = @{}
$script:lastData = $null
$script:iconCache = @{}
$script:defaultIcon = [Drawing.SystemIcons]::Application.ToBitmap()
$script:logPath = Join-Path $PSScriptRoot ("pcwatch_" + (Get-Date -Format yyyyMMdd_HHmmss) + ".csv")
$critical = 'System', 'Registry', 'smss', 'csrss', 'wininit', 'winlogon', 'services', 'lsass', 'svchost', 'dwm',
    'fontdrvhost', 'Memory Compression', 'MsMpEng', 'SecurityHealthService', 'LsaIso', 'sihost', 'conhost', 'WmiPrvSE'

function Get-AppInfo($path, $name) {
    $key = if ($path) { $path } else { "?$name" }
    if (-not $script:iconCache.ContainsKey($key)) {
        $icon = $script:defaultIcon; $title = $name
        if ($path -and (Test-Path -LiteralPath $path)) {
            try { $icon = [Drawing.Icon]::ExtractAssociatedIcon($path).ToBitmap() } catch {}
            try {
                $desc = [Diagnostics.FileVersionInfo]::GetVersionInfo($path).FileDescription
                if ($desc -and $desc.Trim()) { $title = $desc.Trim() }
            } catch {}
        }
        $script:iconCache[$key] = @{ Icon = $icon; Title = $title }
    }
    $script:iconCache[$key]
}

function Blend($a, $b, $t) {
    $t = [math]::Max(0, [math]::Min(1, $t))
    [Drawing.Color]::FromArgb([int]($a.R + ($b.R - $a.R) * $t), [int]($a.G + ($b.G - $a.G) * $t), [int]($a.B + ($b.B - $a.B) * $t))
}

function Format-Speed($bytes) {
    if ($bytes -ge 1MB) { '{0:N1} МБ/с' -f ($bytes / 1MB) } else { '{0:N0} КБ/с' -f ($bytes / 1KB) }
}

function Render($d) {
    Set-Card $cCpu $d.Cpu "$($d.Cpu)%" "$((Get-Date).ToString('HH:mm:ss'))"
    Set-Card $cRam $d.RamPct "$($d.RamPct)%" ('{0:N1} из {1:N1} ГБ' -f $d.RamUsed, $d.RamTotal)
    Set-Card $cDisk $d.DiskPct "$($d.DiskPct)%" ("$(Format-Speed $d.DiskIO) · C: свободно $($d.CFree)%")
    if ($null -ne $d.Gpu) { Set-Card $cGpu $d.Gpu "$($d.Gpu)%" 'max по движкам' } else { Set-Card $cGpu 0 'н/д' 'нет счётчиков GPU' }
    Set-Card $cNet ($d.NetIn + $d.NetOut) (Format-Speed ($d.NetIn + $d.NetOut)) ("↓ $(Format-Speed $d.NetIn)  ↑ $(Format-Speed $d.NetOut)")

    $procs = if ($script:sortBy -eq 'Cpu') { $d.Procs | Sort-Object Cpu, Ram -Descending } else { $d.Procs | Sort-Object Ram -Descending }
    $procs = @($procs | Select-Object -First 60)
    $script:procMap = @{}; foreach ($p in $d.Procs) { $script:procMap[$p.Name] = $p }

    # Диагноз
    $topCpu = $d.Procs | Sort-Object Cpu -Descending | Select-Object -First 1
    $topRam = $d.Procs | Sort-Object Ram -Descending | Select-Object -First 1
    $msg = @()
    if ($d.Cpu -ge 85) { $msg += "Процессор $($d.Cpu)% — больше всех «$((Get-AppInfo $topCpu.Path $topCpu.Name).Title)» ($($topCpu.Cpu)%)" }
    if ($d.RamPct -ge 85) { $msg += "Память $($d.RamPct)% — больше всех «$((Get-AppInfo $topRam.Path $topRam.Name).Title)» ($($topRam.Ram) МБ)" }
    if ($d.DiskPct -ge 90) { $msg += "Диск занят на $($d.DiskPct)% (обновления, антивирус, индексация)" }
    if ($d.CFree -lt 10) { $msg += "На C: осталось $($d.CFree)% места" }
    if ($d.Gpu -ge 90) { $msg += "Видеокарта $($d.Gpu)%" }
    if ($msg.Count) { $diag.BackColor = $T.Bad; $diag.ForeColor = $T.BadText; $diag.Text = '⚠  ' + ($msg -join '   ·   ') }
    else { $diag.BackColor = $T.Ok; $diag.ForeColor = $T.OkText; $diag.Text = '✓  Всё в норме — система не перегружена' }

    # Таблица: сохраняем выделение и прокрутку
    $sel = if ($grid.CurrentRow -and $grid.CurrentRow.Selected) { $grid.CurrentRow.Cells['Key'].Value } else { $null }
    $first = $grid.FirstDisplayedScrollingRowIndex
    $grid.SuspendLayout()
    $grid.Rows.Clear()
    foreach ($p in $procs) {
        $info = Get-AppInfo $p.Path $p.Name
        $i = $grid.Rows.Add($info.Icon, $info.Title, "$($p.Name).exe", $p.Count, ('{0:N1} %' -f $p.Cpu), ('{0:N0} МБ' -f $p.Ram), $p.Name)
        $row = $grid.Rows[$i]
        $row.Cells['Cpu'].Style.BackColor = Blend $T.Bg $T.Heat ($p.Cpu / 40)
        $row.Cells['Ram'].Style.BackColor = Blend $T.Bg $T.Heat ($p.Ram / 3000)
    }
    $grid.ClearSelection()
    if ($sel) {
        foreach ($row in $grid.Rows) { if ($row.Cells['Key'].Value -eq $sel) { $grid.CurrentCell = $row.Cells['Title']; $row.Selected = $true; break } }
    }
    if ($first -ge 0 -and $first -lt $grid.Rows.Count) { $grid.FirstDisplayedScrollingRowIndex = $first }
    $grid.ResumeLayout()

    if ($chkLog.Checked) {
        if (-not (Test-Path $script:logPath)) { Add-Content $script:logPath 'time;cpu;ram;disk;gpu;net_kbs;top_process' -Encoding UTF8 }
        Add-Content $script:logPath ('{0};{1};{2};{3};{4};{5:N0};{6}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'),
            $d.Cpu, $d.RamPct, $d.DiskPct, $d.Gpu, (($d.NetIn + $d.NetOut) / 1KB), $topCpu.Name) -Encoding UTF8
    }
}

function Get-SelectedProc {
    if (-not $grid.CurrentRow -or -not $grid.CurrentRow.Selected) { return $null }
    $script:procMap[$grid.CurrentRow.Cells['Key'].Value]
}

function Stop-SelectedApp {
    $p = Get-SelectedProc
    if (-not $p) { return }
    $title = (Get-AppInfo $p.Path $p.Name).Title
    if ($p.Pids -contains $PID) {
        [void][Windows.Forms.MessageBox]::Show('Это сам PC Watch. Закрой окно, если хочешь его остановить.', 'PC Watch'); return
    }
    if ($critical -contains $p.Name) {
        [void][Windows.Forms.MessageBox]::Show("«$title» ($($p.Name).exe) — системный процесс.`nЕго завершение приведёт к сбою или перезагрузке Windows.", 'PC Watch', 'OK', 'Warning'); return
    }
    $extra = if ($p.Count -gt 1) { " (процессов: $($p.Count))" } else { '' }
    $ans = [Windows.Forms.MessageBox]::Show("Снять задачу «$title»$extra?`nНесохранённые данные в этом приложении будут потеряны.", 'PC Watch', 'YesNo', 'Question')
    if ($ans -ne 'Yes') { return }
    $failed = 0
    foreach ($id in $p.Pids) { try { Stop-Process -Id $id -Force -ErrorAction Stop } catch { if (Get-Process -Id $id -ErrorAction SilentlyContinue) { $failed++ } } }
    if ($failed) {
        $hint = if ($isAdmin) { 'Процесс защищён системой.' } else { 'Нажми «Режим администратора» и попробуй снова.' }
        [void][Windows.Forms.MessageBox]::Show("Не удалось завершить процессов: $failed.`n$hint", 'PC Watch', 'OK', 'Warning')
    }
}

$btnKill.Add_Click({ Stop-SelectedApp })
$miKill.Add_Click({ Stop-SelectedApp })
$grid.Add_KeyDown({ param($s, $e) if ($e.KeyCode -eq 'Delete') { Stop-SelectedApp } })
$grid.Add_CellMouseDown({
    param($s, $e)
    if ($e.Button -eq 'Right' -and $e.RowIndex -ge 0) {
        $grid.ClearSelection(); $grid.CurrentCell = $grid.Rows[$e.RowIndex].Cells['Title']; $grid.Rows[$e.RowIndex].Selected = $true
    }
})
$menu.Add_Opening({
    param($s, $e)
    $p = Get-SelectedProc
    if (-not $p) { $e.Cancel = $true; return }
    $miOpen.Enabled = [bool]$p.Path
})
$miOpen.Add_Click({ $p = Get-SelectedProc; if ($p.Path) { Start-Process explorer.exe "/select,`"$($p.Path)`"" } })
$miSearch.Add_Click({ $p = Get-SelectedProc; if ($p) { Start-Process ("https://www.google.com/search?q=" + [uri]::EscapeDataString("$($p.Name).exe что это за процесс")) } })

$btnSortCpu.Add_Click({ $script:sortBy = 'Cpu'; $btnSortCpu.BackColor = $T.Sel; $btnSortRam.BackColor = $T.Btn; if ($script:lastData) { Render $script:lastData } })
$btnSortRam.Add_Click({ $script:sortBy = 'Ram'; $btnSortRam.BackColor = $T.Sel; $btnSortCpu.BackColor = $T.Btn; if ($script:lastData) { Render $script:lastData } })
$btnAdmin.Add_Click({
    try {
        Start-Process powershell.exe -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$PSCommandPath`""
        $form.Close()
    } catch {}
})

$timer = New-Object Windows.Forms.Timer
$timer.Interval = 500
$timer.Add_Tick({
    $d = $sync.Data
    if ($d -and -not [object]::ReferenceEquals($d, $script:lastData)) {
        $script:lastData = $d
        try { Render $d } catch { $diag.BackColor = $T.Bad; $diag.ForeColor = $T.BadText; $diag.Text = "Ошибка отображения: $($_.Exception.Message)" }
    } elseif ($sync.Error) {
        $diag.BackColor = $T.Bad; $diag.ForeColor = $T.BadText; $diag.Text = "Ошибка сбора данных: $($sync.Error)"
    }
})
$form.Add_Shown({ $timer.Start() })
$form.Add_FormClosing({ $timer.Stop(); $sync.Run = $false; try { $bg.Stop() } catch {} })

[void]$form.ShowDialog()
}
catch {
    [System.Windows.Forms.MessageBox]::Show("PC Watch не запустился:`n$($_.Exception.Message)", 'PC Watch') | Out-Null
}
