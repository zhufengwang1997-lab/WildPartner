# Prints a file-name-safe GPU name (first real display adapter), e.g. NVIDIA-GeForce-RTX-5060-Ti
$a = Get-CimInstance Win32_VideoController | Where-Object { $_.Name -notmatch 'Basic|Virtual|Remote' } | Select-Object -First 1
if ($a) { ($a.Name -replace '[^A-Za-z0-9]+', '-').Trim('-') } else { 'GPU' }
