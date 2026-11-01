param(
  [Parameter(Mandatory=$true)][string]$AssemblyPath,
  [Parameter(Mandatory=$true)][string]$TypeName,
  [string]$MethodName,
  [int]$MaxLines = 400
)

$ErrorActionPreference = 'Stop'

# ---- opcode table -----------------------------------------------------------
$opcodes = @{}
foreach ($f in [System.Reflection.Emit.OpCodes].GetFields('Public,Static')) {
  $op = $f.GetValue($null)
  $key = ([int]$op.Value) -band 0xFFFF
  if (-not $opcodes.ContainsKey($key)) { $opcodes[$key] = $op }
}

function Format-Operand([System.Reflection.Emit.OpCode]$op, [byte[]]$il, [int]$pos, $module) {
  $t = $op.OperandType
  switch ($t) {
    'InlineNone'   { return @{ Text = ''; Advance = 0 } }
    'ShortInlineI' { $v = [int]$il[$pos]; if ($v -gt 127) { $v -= 256 }; return @{ Text = $v.ToString(); Advance = 1 } }
    'ShortInlineVar'{ return @{ Text = $il[$pos].ToString(); Advance = 1 } }
    'ShortInlineBrTarget' {
      $d = [sbyte]$il[$pos]
      return @{ Text = ('IL_{0:X4}' -f ($pos + 1 + $d)); Advance = 1 }
    }
    'InlineVar'    { $v = [BitConverter]::ToUInt16($il, $pos); return @{ Text = $v.ToString(); Advance = 2 } }
    'InlineI'      { $v = [BitConverter]::ToInt32($il, $pos); return @{ Text = $v.ToString(); Advance = 4 } }
    'InlineI8'     { $v = [BitConverter]::ToInt64($il, $pos); return @{ Text = $v.ToString(); Advance = 8 } }
    'InlineR'      { $v = [BitConverter]::ToDouble($il, $pos); return @{ Text = $v.ToString(); Advance = 8 } }
    'InlineBrTarget' {
      $d = [BitConverter]::ToInt32($il, $pos)
      return @{ Text = ('IL_{0:X4}' -f ($pos + 4 + $d)); Advance = 4 }
    }
    'InlineString' {
      $tok = [BitConverter]::ToInt32($il, $pos)
      $s = ''
      try { $s = $module.ResolveString($tok) } catch { $s = "<str 0x{0:X8}>" -f $tok }
      return @{ Text = ('"{0}"' -f ($s -replace "`r?`n", '\n')); Advance = 4 }
    }
    'InlineSwitch' {
      $n = [BitConverter]::ToInt32($il, $pos)
      $base = $pos + 4 + 4 * $n
      $targets = @()
      for ($i = 0; $i -lt $n; $i++) {
        $d = [BitConverter]::ToInt32($il, $pos + 4 + 4 * $i)
        $targets += ('IL_{0:X4}' -f ($base + $d))
      }
      return @{ Text = ('(' + ($targets -join ', ') + ')'); Advance = 4 + 4 * $n }
    }
    default {
      # InlineMethod / InlineField / InlineType / InlineTok / InlineSig
      $tok = [BitConverter]::ToInt32($il, $pos)
      $text = ''
      try {
        $m = $module.ResolveMember($tok)
        if ($m -is [System.Reflection.MethodBase]) {
          $ps = ($m.GetParameters() | ForEach-Object { if ($_.ParameterType.IsByRef) { $_.ParameterType.GetElementType().Name + '&' } else { $_.ParameterType.Name } }) -join ', '
          $decl = $m.DeclaringType.FullName
          $text = ('{0} {1}::{2}({3})' -f $m.ReturnType.Name, $decl, $m.Name, $ps)
        } elseif ($m -is [System.Reflection.FieldInfo]) {
          $text = ('{0} {1}::{2}' -f $m.FieldType.Name, $m.DeclaringType.FullName, $m.Name)
        } elseif ($m -is [System.Reflection.Type]) {
          $text = $m.FullName
        } else {
          $text = $m.ToString()
        }
      } catch {
        $text = ('<token 0x{0:X8}>' -f $tok)
      }
      return @{ Text = $text; Advance = 4 }
    }
  }
}

# ---- load -------------------------------------------------------------------
$asm = [Reflection.Assembly]::LoadFrom($AssemblyPath)
try { $types = $asm.GetTypes() } catch [Reflection.ReflectionTypeLoadException] { $types = $_.Exception.Types | Where-Object { $_ -ne $null } }

$targets = $types | Where-Object { $_.FullName -eq $TypeName }
if (-not $targets) { "TYPE NOT FOUND: $TypeName"; exit 1 }

$flags = [Reflection.BindingFlags]'Public,NonPublic,Static,Instance,DeclaredOnly'
foreach ($ty in $targets) {
  $methods = $ty.GetMethods($flags) | Where-Object { $_.IsSpecialName -eq $false -or $_.Name -eq 'MoveNext' }
  if ($MethodName) { $methods = $methods | Where-Object { $_.Name -eq $MethodName } }
  foreach ($m in $methods) {
    $body = $m.GetMethodBody()
    if ($null -eq $body) { continue }
    $il = $body.GetILAsByteArray()
    if ($null -eq $il) { continue }
    $ps = ($m.GetParameters() | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ', '
    "===== {0}::{1}({2})  [{3} bytes IL] =====" -f $ty.FullName, $m.Name, $ps, $il.Length
    $pos = 0
    $lines = 0
    while ($pos -lt $il.Length -and $lines -lt $MaxLines) {
      $offset = $pos
      $b = $il[$pos]
      $pos++
      if ($b -eq 0xFE) {
        $b2 = $il[$pos]; $pos++
        $key = 0xFE00 -bor $b2
      } else {
        $key = [int]$b
      }
      if (-not $opcodes.ContainsKey($key)) { "IL_{0:X4}:  <unknown 0x{1:X4}>" -f $offset, $key; break }
      $op = $opcodes[$key]
      $res = Format-Operand $op $il $pos $m.Module
      if ($res.Text) {
        "IL_{0:X4}:  {1,-12} {2}" -f $offset, $op.Name, $res.Text
      } else {
        "IL_{0:X4}:  {1}" -f $offset, $op.Name
      }
      $pos += $res.Advance
      $lines++
    }
    if ($lines -ge $MaxLines) { "  ... (truncated)" }
    ""
  }
}
