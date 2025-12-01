Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$screen = [System.Windows.Forms.Screen]::PrimaryScreen
$bmp = New-Object System.Drawing.Bitmap($screen.Bounds.Width, $screen.Bounds.Height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($screen.Bounds.Location, [System.Drawing.Point]::Empty, $screen.Bounds.Size)
$bmp.Save("H:\Claude_work\Taskbar Expand\screenshot.png")
$g.Dispose()
$bmp.Dispose()
Write-Host "Screenshot saved to H:\Claude_work\Taskbar Expand\screenshot.png"
