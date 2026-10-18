# End-to-end check on a Windows runner: builds a SQL Server database that
# exercises every type mapping, exports it three ways (live database, .bak,
# .mdf) through the real exe, and verifies the SQLite output value by value.
param(
    [Parameter(Mandatory = $true)] [string] $Exe,
    [string] $Work = (Join-Path $env:RUNNER_TEMP 'smoke')
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $Work | Out-Null
$server = '(localdb)\MSSQLLocalDB'

SqlLocalDB start MSSQLLocalDB | Out-Host

function Invoke-Sql([string] $database, [string] $sql) {
    $connection = New-Object System.Data.SqlClient.SqlConnection "Server=$server;Database=$database;Integrated Security=true;Pooling=false"
    $connection.Open()
    try {
        foreach ($batch in ($sql -split '(?m)^\s*GO\s*$')) {
            if ($batch.Trim().Length -eq 0) { continue }
            $command = $connection.CreateCommand()
            $command.CommandText = $batch
            $command.CommandTimeout = 0
            [void] $command.ExecuteNonQuery()
        }
    } finally { $connection.Close() }
}

function Invoke-Exe([string[]] $arguments, [string] $name) {
    $log = Join-Path $Work "$name.log"
    $process = Start-Process -FilePath $Exe -ArgumentList ($arguments + @('--log', "`"$log`"")) -Wait -PassThru -NoNewWindow
    if (Test-Path $log) { Get-Content $log -Encoding UTF8 | Out-Host }
    if ($process.ExitCode -ne 0) { throw "$name exited with $($process.ExitCode)" }
}

Invoke-Sql 'master' (Get-Content (Join-Path $PSScriptRoot 'smoke-schema.sql') -Raw -Encoding UTF8)

# 1. Live database
$live = Join-Path $Work 'live.db'
Invoke-Exe @('--server', "`"$server`"", '--database', 'SmokeShop', '--out', "`"$live`"") 'live'
python (Join-Path $PSScriptRoot 'verify.py') $live
if ($LASTEXITCODE -ne 0) { throw 'live export failed verification' }

# 2. Backup file, restored temporarily and dropped afterwards
$bak = Join-Path $Work 'SmokeShop.bak'
Invoke-Sql 'master' "BACKUP DATABASE SmokeShop TO DISK = N'$bak' WITH INIT"
$fromBak = Join-Path $Work 'bak.db'
Invoke-Exe @('--server', "`"$server`"", '--bak', "`"$bak`"", '--out', "`"$fromBak`"") 'bak'
python (Join-Path $PSScriptRoot 'verify.py') $fromBak
if ($LASTEXITCODE -ne 0) { throw 'bak export failed verification' }

# 3. Detached data file; the original must come back byte-identical
$mdf = (New-Object System.Data.SqlClient.SqlConnection "Server=$server;Database=master;Integrated Security=true;Pooling=false")
$mdf.Open()
$cmd = $mdf.CreateCommand()
$cmd.CommandText = "SELECT physical_name FROM sys.master_files WHERE database_id = DB_ID('SmokeShop') AND type = 0"
$mdfPath = [string] $cmd.ExecuteScalar()
$cmd.CommandText = "SELECT physical_name FROM sys.master_files WHERE database_id = DB_ID('SmokeShop') AND type = 1"
$ldfPath = [string] $cmd.ExecuteScalar()
$mdf.Close()
Invoke-Sql 'master' "ALTER DATABASE SmokeShop SET SINGLE_USER WITH ROLLBACK IMMEDIATE`nGO`nEXEC sp_detach_db N'SmokeShop'"
$mdfCopy = Join-Path $Work 'SmokeShop.mdf'
Copy-Item $mdfPath $mdfCopy
$before = (Get-FileHash $mdfCopy).Hash
$fromMdf = Join-Path $Work 'mdf.db'
Invoke-Exe @('--server', "`"$server`"", '--mdf', "`"$mdfCopy`"", '--out', "`"$fromMdf`"") 'mdf'
python (Join-Path $PSScriptRoot 'verify.py') $fromMdf
if ($LASTEXITCODE -ne 0) { throw 'mdf export failed verification' }
if ((Get-FileHash $mdfCopy).Hash -ne $before) { throw 'the original .mdf was modified' }

# 3b. Same, with its log file beside it (attached as-is instead of rebuilt)
$withLog = Join-Path $Work 'withlog'
New-Item -ItemType Directory -Force $withLog | Out-Null
Copy-Item $mdfPath (Join-Path $withLog 'SmokeShop.mdf')
Copy-Item $ldfPath (Join-Path $withLog 'SmokeShop_log.ldf')
$fromMdfLog = Join-Path $Work 'mdflog.db'
Invoke-Exe @('--server', "`"$server`"", '--mdf', "`"$(Join-Path $withLog 'SmokeShop.mdf')`"", '--out', "`"$fromMdfLog`"") 'mdflog'
python (Join-Path $PSScriptRoot 'verify.py') $fromMdfLog
if ($LASTEXITCODE -ne 0) { throw 'mdf+ldf export failed verification' }

# No temporary databases or copied files may be left behind.
$check = New-Object System.Data.SqlClient.SqlConnection "Server=$server;Database=master;Integrated Security=true;Pooling=false"
$check.Open()
$cmd = $check.CreateCommand()
$cmd.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name LIKE 'MssqlToSqlite[_]%'"
$leftover = [int] $cmd.ExecuteScalar()
$check.Close()
if ($leftover -ne 0) { throw "$leftover temporary database(s) left behind" }
if (Get-ChildItem $Work, (Split-Path $mdfPath) -Recurse -Filter 'MssqlToSqlite_*') { throw 'temporary data file copies left behind' }

# 4. Table selection and the list command
$subset = Join-Path $Work 'subset.db'
Invoke-Sql 'master' "CREATE DATABASE SmokeShop ON (FILENAME = N'$mdfPath'), (FILENAME = N'$ldfPath') FOR ATTACH"
Invoke-Exe @('--server', "`"$server`"", '--database', 'SmokeShop', '--tables', 'CAR_PART,sales.ORDERS', '--out', "`"$subset`"") 'subset'
python (Join-Path $PSScriptRoot 'tables.py') $subset CAR_PART _export_info _export_tables sales_ORDERS
if ($LASTEXITCODE -ne 0) { throw '--tables selected the wrong tables' }
Invoke-Exe @('--server', "`"$server`"", '--list') 'list'

Write-Host 'SMOKE TEST PASSED'
