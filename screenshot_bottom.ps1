Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$screen = [System.Windows.Forms.Screen]::PrimaryScreen
# 화면 하단 200px만 캡처
$captureHeight = 200
$captureTop = $screen.Bounds.Height - $captureHeight

$bmp = New-Object System.Drawing.Bitmap($screen.Bounds.Width, $captureHeight)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen(0, $captureTop, 0, 0, (New-Object System.Drawing.Size($screen.Bounds.Width, $captureHeight)))
$bmp.Save("H:\Claude_work\Taskbar Expand\screenshot_bottom.png")
$g.Dispose()
$bmp.Dispose()
Write-Host "Screenshot saved to H:\Claude_work\Taskbar Expand\screenshot_bottom.png"
Write-Host "Captured: Y=$captureTop to Y=$($screen.Bounds.Height), Height=$captureHeight"
