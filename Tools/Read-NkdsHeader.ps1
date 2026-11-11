#Requires -Version 5.1
<#
.SYNOPSIS
    Reads and dumps the binary header of an NKDS index file.
    Handles both separate-mode (.nkds index only) and embedded-mode (shard+index+footer).

.PARAMETER FilePath
    Path to the .nkds file.

.EXAMPLE
    .\Read-NkdsHeader.ps1 -FilePath C:\Users\craig\Downloads\Nanook\PS2.nkds
#>
param(
    [Parameter(Mandatory)]
    [string]$FilePath
)

function Read-BE-UInt16([byte[]]$b, [int]$off) { ([int]$b[$off] -shl 8) -bor $b[$off+1] }
function Read-BE-Int32([byte[]]$b, [int]$off) {
    [long]$v = ([long]$b[$off] -shl 24) -bor ([long]$b[$off+1] -shl 16) -bor ([long]$b[$off+2] -shl 8) -bor $b[$off+3]
    if ($v -band 0x80000000L) { $v = $v - 0x100000000L }
    return $v
}
function Read-BE-UInt32([byte[]]$b, [int]$off) {
    ([long]$b[$off] -shl 24) -bor ([long]$b[$off+1] -shl 16) -bor ([long]$b[$off+2] -shl 8) -bor $b[$off+3]
}
function Read-BE-Int64([byte[]]$b, [int]$off) {
    [long]$hi = Read-BE-UInt32 $b $off
    [long]$lo = Read-BE-UInt32 $b ($off+4)
    return ($hi -shl 32) -bor ($lo -band 0xFFFFFFFFL)
}

$FOOTER_SIZE  = 12
$FOOTER_MAGIC = 0x4E4B4453
$HEADER_MAGIC = 0x4E4B4453
$HEADER_SIZE  = 256

if (-not (Test-Path $FilePath)) { Write-Error "File not found: $FilePath"; exit 1 }

$fileInfo = Get-Item $FilePath
$fileSize = $fileInfo.Length
Write-Host "File:  $FilePath"
Write-Host "Size:  $("{0:N0}" -f $fileSize) bytes  ($("{0:F3}" -f ($fileSize / 1GB)) GiB)"

# -----------------------------------------------------------------------
# 1. Detect embedded vs separate mode
# -----------------------------------------------------------------------
$fs = [System.IO.FileStream]::new($FilePath, [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)

$footerBytes = New-Object byte[] $FOOTER_SIZE
$fs.Seek(-$FOOTER_SIZE, [System.IO.SeekOrigin]::End) | Out-Null
$fs.Read($footerBytes, 0, $FOOTER_SIZE) | Out-Null
$footerMagic = Read-BE-UInt32 $footerBytes 8

$isEmbedded = ($footerMagic -eq $FOOTER_MAGIC)
$baseOffset = 0L

if ($isEmbedded) {
    $footerIndexSize = Read-BE-Int64 $footerBytes 0
    $baseOffset = $fileSize - $FOOTER_SIZE - $footerIndexSize
    Write-Host "`nMode:  EMBEDDED  (IndexSize=$("{0:N0}" -f $footerIndexSize), ShardData=$("{0:F3}" -f ($baseOffset / 1GB)) GiB)"
    if ($baseOffset -lt 0) {
        Write-Warning "Computed base offset is negative — footer is corrupted."
        $fs.Close(); exit 1
    }
} else {
    Write-Host "`nMode:  SEPARATE  (standalone index file, no embedded footer)"
}

# -----------------------------------------------------------------------
# 2. Read primary header at baseOffset
# -----------------------------------------------------------------------
Write-Host "`n--- PRIMARY HEADER (at file offset $("{0:N0}" -f $baseOffset)) ---"

$hdr = New-Object byte[] $HEADER_SIZE
$fs.Seek($baseOffset, [System.IO.SeekOrigin]::Begin) | Out-Null
$fs.Read($hdr, 0, $HEADER_SIZE) | Out-Null
$fs.Close()

$magic   = Read-BE-UInt32  $hdr 0x00
$mjv     = Read-BE-UInt16  $hdr 0x04
$mnv     = Read-BE-UInt16  $hdr 0x06
$dirOff  = Read-BE-Int64   $hdr 0x08
$dirSz   = Read-BE-Int32   $hdr 0x10
$dirUSz  = Read-BE-Int32   $hdr 0x14
$biOff   = Read-BE-Int64   $hdr 0x18
$biSz    = Read-BE-Int64   $hdr 0x20
$dcnt    = Read-BE-Int32   $hdr 0x28
$dhead   = Read-BE-Int64   $hdr 0x2C
$fend    = Read-BE-Int64   $hdr 0x34
$shSz    = Read-BE-Int64   $hdr 0x3C
$blkSz   = Read-BE-Int32   $hdr 0x44
$maxOff  = Read-BE-Int32   $hdr 0x48
$imgCnt  = Read-BE-Int32   $hdr 0x4C
$dirCap  = Read-BE-Int32   $hdr 0x50

# Checksum stored as 8 bytes at 0x54 — print as hex
$chkHi   = Read-BE-UInt32  $hdr 0x54
$chkLo   = Read-BE-UInt32  $hdr 0x58
$chkHex  = "0x{0:X8}{1:X8}" -f $chkHi, $chkLo

# Index region size for bounds checking
$indexRegionSize = if ($isEmbedded) { $fileSize - $FOOTER_SIZE - $baseOffset } else { $fileSize }

$magicOk = ($magic -eq $HEADER_MAGIC)

Write-Host ("Magic:                     0x{0:X8}  ({1})" -f $magic, $(if ($magicOk) {"OK"} else {"BAD — expected 0x4E4B4453"}))
Write-Host ("Version:                   {0}.{1}  (expected 1.0)" -f $mjv, $mnv)
Write-Host ("ImageDirectory:            offset={0:N0}  compressedSize={1:N0}  uncompressedSize={2:N0}" -f $dirOff, $dirSz, $dirUSz)
Write-Host ("BlockIndex:                offset={0:N0}  size={1:N0}  ({2:F1} MiB)" -f $biOff, $biSz, ($biSz / 1MB))
Write-Host ("BlockIndexDeltaCount:      {0}" -f $dcnt)
Write-Host ("BlockIndexDeltaHeadOffset: {0:N0}" -f $dhead)
Write-Host ("FileEndOffset:             {0:N0}  ({1:F3} GiB)" -f $fend, ($fend / 1GB))
Write-Host ("ShardSize:                 {0}  (0 = embedded/unlimited)" -f $shSz)
Write-Host ("BlockSize:                 {0:N0} bytes  ({1} KiB)" -f $blkSz, ($blkSz / 1024))
Write-Host ("MaxOffsetBlocks:           {0}" -f $maxOff)
Write-Host ("ImageCount:                {0}" -f $imgCnt)
Write-Host ("DirectoryRegionCapacity:   {0:N0} bytes" -f $dirCap)
Write-Host ("HeaderChecksum (stored):   {0}" -f $chkHex)

# -----------------------------------------------------------------------
# 3. Validation
# -----------------------------------------------------------------------
Write-Host "`n--- VALIDATION (index region = $("{0:N0}" -f $indexRegionSize) bytes) ---"
$issues  = @()
$warns   = @()

if (-not $magicOk)                                          { $issues += "Header magic invalid (0x{0:X8})" -f $magic }
if ($mjv -ne 1)                                             { $issues += "MajorVersion is $mjv (expected 1)" }
if ($mnv -gt 0)                                             { $issues += "MinorVersion is $mnv (expected 0)" }
if ($dirSz -lt 0)                                           { $issues += "ImageDirectorySize is negative" }
if ($dirUSz -lt 0)                                          { $issues += "ImageDirectoryUncompressedSize is negative" }
if ($dirOff -lt 512)                                        { $issues += "ImageDirectoryOffset ($dirOff) < 512" }
if ($dirOff + $dirSz -gt $indexRegionSize)                  { $issues += "ImageDirectory extends beyond index (off=$dirOff sz=$dirSz region=$indexRegionSize)" }
if ($biOff -lt 512)                                         { $issues += "BlockIndexOffset ($biOff) < 512" }
if ($biSz -lt 0)                                            { $issues += "BlockIndexSize is negative" }
if ($biOff + $biSz -gt $indexRegionSize)                    { $issues += "BlockIndex extends beyond index region ($biOff + $biSz > $indexRegionSize)" }
if ($fend -lt 512)                                          { $issues += "FileEndOffset ($fend) < 512" }
if ($fend -gt $indexRegionSize)                             { $issues += "FileEndOffset ($fend) > index region ($indexRegionSize)" }
if ($dhead -ne 0 -and $dhead -lt 512)                       { $issues += "DeltaHeadOffset ($dhead) < 512 but non-zero" }
if ($dhead -gt $indexRegionSize)                            { $issues += "DeltaHeadOffset ($dhead) > index region" }
if ($blkSz -le 0)                                           { $issues += "BlockSize <= 0" }
if ($maxOff -le 0)                                          { $issues += "MaxOffsetBlocks <= 0" }
if ($dirCap -lt 0)                                          { $issues += "DirectoryRegionCapacity < 0" }
if ($imgCnt -lt 0)                                          { $issues += "ImageCount < 0" }

if ($dcnt -ne 0)                                            { $warns  += "BlockIndexDeltaCount is $dcnt (should be 0 after compact — index needs CompactSetFast)" }
if ($dirCap -eq 0)                                          { $warns  += "DirectoryRegionCapacity is 0 — every directory update overflows to end of file (may fragment index rapidly)" }
if ($dirUSz -eq 0)                                          { $warns  += "ImageDirectoryUncompressedSize is 0 — directory treated as uncompressed on open" }
if ($shSz -ne 0 -and -not $isEmbedded)                     { $warns  += "ShardSize=$shSz but file has no embedded footer — separate-mode set" }

foreach ($i in $issues) { Write-Host "  ERROR: $i" -ForegroundColor Red }
foreach ($w in $warns)  { Write-Host "  WARN:  $w" -ForegroundColor Yellow }

if ($issues.Count -eq 0 -and $warns.Count -eq 0) {
    Write-Host "  All checks passed." -ForegroundColor Green
} elseif ($issues.Count -eq 0) {
    Write-Host "  No structural errors (warnings above)." -ForegroundColor Cyan
}

# -----------------------------------------------------------------------
# 4. Compare primary vs secondary header
# -----------------------------------------------------------------------
Write-Host "`n--- SECONDARY HEADER (at file offset $("{0:N0}" -f ($baseOffset + $HEADER_SIZE))) ---"
$fs2 = [System.IO.FileStream]::new($FilePath, [System.IO.FileMode]::Open,
         [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
$hdr2 = New-Object byte[] $HEADER_SIZE
$fs2.Seek($baseOffset + $HEADER_SIZE, [System.IO.SeekOrigin]::Begin) | Out-Null
$fs2.Read($hdr2, 0, $HEADER_SIZE) | Out-Null
$fs2.Close()

$same = $true
for ($i = 0; $i -lt $HEADER_SIZE; $i++) {
    if ($hdr[$i] -ne $hdr2[$i]) { $same = $false; break }
}
if ($same) {
    Write-Host "  Primary and secondary headers are identical." -ForegroundColor Green
} else {
    Write-Host "  Headers DIFFER — possible crash mid-AtomicCommit." -ForegroundColor Yellow
    for ($i = 0; $i -lt $HEADER_SIZE; $i++) {
        if ($hdr[$i] -ne $hdr2[$i]) {
            Write-Host ("  First diff at byte 0x{0:X2}: primary=0x{1:X2}  secondary=0x{2:X2}" -f $i, $hdr[$i], $hdr2[$i])
            break
        }
    }
}

Write-Host "`nDone."
