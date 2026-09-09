param([string]$AssemblyPath = (Join-Path $PSScriptRoot '..\bin\x64\Debug\TIA_Project_v11.exe'))
$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $AssemblyPath).Path)
$logic = $assembly.GetType('TIA_Project_v11.OpennessLogic', $true)
$flags = [Reflection.BindingFlags]'Static,NonPublic'
$allocate = $logic.GetMethod('HikNextFreeAddress', $flags)
$readCsv = $logic.GetMethod('ReadHik3050Csv', $flags)
function Assert-Equal($actual, $expected, $label) {
    if ($actual -ne $expected) { throw "$label : expected $expected, got $actual" }
}
$ranges = New-Object 'System.Collections.Generic.List[System.Tuple[int,int]]'
$ranges.Add([Tuple]::Create(120, 130))
$ranges.Add([Tuple]::Create(100, 110))
Assert-Equal ($allocate.Invoke($null, @([int]90, [int]10, $ranges.PSObject.BaseObject))) 90 'Exact free gap'
Assert-Equal ($allocate.Invoke($null, @([int]95, [int]10, $ranges.PSObject.BaseObject))) 110 'Skip collision'
Assert-Equal ($allocate.Invoke($null, @([int]105, [int]15, $ranges.PSObject.BaseObject))) 130 'Skip multiple ranges'
Assert-Equal ($allocate.Invoke($null, @([int]110, [int]10, $ranges.PSObject.BaseObject))) 110 'Touching boundary'
$ranges.Add([Tuple]::Create(105, 125))
Assert-Equal ($allocate.Invoke($null, @([int]100, [int]1, $ranges.PSObject.BaseObject))) 130 'Overlapping reservations'
$csvPath = [IO.Path]::GetTempFileName()
try {
    foreach ($separator in @(';', ',', "`t")) {
        $header = 'Name;DeviceName;Type;IpAddress;SubnetName;ProfinetNumber;PlcName;GroupName;StartAddress'
        $first = 'hik01;hik01_DEV;;192.168.0.101;PN;101;PLC1;Kamery;1000'
        $second = 'hik02;hik02_DEV;;192.168.0.102;PN;102;PLC1;Kamery;ignored'
        [IO.File]::WriteAllLines($csvPath, @($header.Replace(';', $separator), $first.Replace(';', $separator), $second.Replace(';', $separator)))
        $arguments = [object[]]@($csvPath, 0, 0)
        $cameras = $readCsv.Invoke($null, $arguments)
        Assert-Equal $cameras.Count 2 'CSV row count'
        Assert-Equal $arguments[1] 1000 'First input base'
        Assert-Equal $arguments[2] 1000 'First output base'
        Assert-Equal $cameras[0].Name 'hik01' 'DAP name'
        Assert-Equal $cameras[0].DeviceName 'hik01_DEV' 'Device name'
        Assert-Equal $cameras[0].Type 'GSD:GSDML-V2.34-HIKVISION-MV-20210719.XML/DAP/DAP 3' 'Default DAP'
        Assert-Equal $cameras[1].ProfinetNumber 102 'PROFINET number'
        Assert-Equal $cameras[1].GroupName 'Kamery' 'Device group'
        Assert-Equal $cameras[1].SubnetName 'PN' 'Subnet'
        Assert-Equal $cameras[1].PlcName 'PLC1' 'PLC'
    }
    [IO.File]::WriteAllLines($csvPath, @($header, $first.Replace('1000', 'invalid')))
    $rejected = $false
    try { $readCsv.Invoke($null, [object[]]@($csvPath, 0, 0)) | Out-Null }
    catch { $rejected = $true }
    Assert-Equal $rejected $true 'Reject invalid first address'
    Write-Output 'PASS: address allocation and CSV mapping (3 separators, first address only).'
}
finally { Remove-Item -LiteralPath $csvPath }
