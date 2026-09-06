# Validates vibeos-app/config/*.jsonc + apps/*.jsonc without running VibeOS.
# Usage: pwsh scripts/validate-config.ps1
# Keep the name lists in sync with ChordParser.cs, KeyGesture.cs and ActionRouter.cs.

$ErrorActionPreference = 'Stop'
$configDir = Join-Path (Join-Path $PSScriptRoot '..') 'config'

$buttons = @('A','B','X','Y','LB','RB','BACK','START','L3','R3','LT','RT',
  'DPAD_UP','DPADUP','DPAD_DOWN','DPADDOWN','DPAD_LEFT','DPADLEFT','DPAD_RIGHT','DPADRIGHT')
$modes = @('Press','Release','Hold','DoubleTap','WhileHeld','Chord')
$actions = @('copy','paste','cut','select-all','undo','redo','save','find','quick-open',
  'cmd-palette','new-tab','reopen-tab','address-bar','next-tab','prev-tab','task-switch',
  'show-desktop','clipboard-history','screenshot','prev-desktop','next-desktop',
  'enter','escape','tab','space','backspace','none',
  'up','down','left','right','home','end','pageup','pagedown',
  'sticky-shift','sticky-ctrl','sticky-win','sticky-alt',
  'launch-chrome','launch-edge','launch-vscode','launch-cursor','launch-terminal','launch-explorer')
$modKeys = @('CTRL','CONTROL','SHIFT','ALT','WIN','WINDOWS')
$namedKeys = @('ENTER','ESCAPE','ESC','TAB','SPACE','BACKSPACE','BACK','DELETE','DEL','INSERT',
  'HOME','END','PAGEUP','PAGEDOWN','UP','DOWN','LEFT','RIGHT') +
  (1..24 | ForEach-Object { "F$_" }) + ((65..90 + 48..57) | ForEach-Object { ([char]$_).ToString() }) +
  @('`','-','=','[',']','\',';',"'",',','.','/')
$errors = 0

function Fail($msg) { Write-Host "ERROR: $msg" -ForegroundColor Red; $script:errors++ }

function Test-Gesture($text, $ctx) {
  $tokens = $text -split '\+'
  if ($tokens.Count -eq 0) { Fail "$ctx : empty gesture"; return }
  for ($i = 0; $i -lt $tokens.Count - 1; $i++) {
    if ($modKeys -notcontains $tokens[$i].ToUpper()) { Fail "$ctx : unknown modifier '$($tokens[$i])'" }
  }
  $key = $tokens[-1].ToUpper()
  if ($namedKeys -notcontains $key -and $key -notmatch '^[A-Z0-9`\-=\[\]\\;'',\./]$') {
    Fail "$ctx : unknown key '$($tokens[-1])'"
  }
}

function Test-ActionValue($value, $ctx) {
  if ($value -is [string]) {
    if ($actions -notcontains $value -and $value -notlike 'voice-*' -and $value -notlike '__gesture:*') {
      # A raw gesture string is only valid via { "key": ... }; a bare string must be an action.
      Fail "$ctx : unknown action '$value'"
    }
    return
  }
  if ($value.key) { Test-Gesture $value.key $ctx; return }
  if ($value.action) {
    if ($actions -notcontains $value.action -and $value.action -notlike 'voice-*') {
      Fail "$ctx : unknown action '$($value.action)'"
    }
    if ($value.mode -and ($modes -notcontains $value.mode)) { Fail "$ctx : unknown mode '$($value.mode)'" }
    return
  }
  Fail "$ctx : needs 'key' or 'action'"
}

function Test-Chord($chord, $ctx) {
  $tokens = $chord -split '\+'
  if ($tokens.Count -eq 0) { Fail "$ctx : empty chord"; return }
  foreach ($t in $tokens) {
    if ($buttons -notcontains $t.ToUpper()) { Fail "$ctx : unknown button '$t'" }
  }
  if (($tokens | Select-Object -Unique).Count -ne $tokens.Count) { Fail "$ctx : duplicate button" }
}

function Get-Jsonc($file) {
  $text = Get-Content $file -Raw
  $text = $text -replace '(?m)^\s*//.*$', ''
  return $text | ConvertFrom-Json
}

function Test-File($file) {
  $name = Split-Path $file -Leaf
  try { $json = Get-Jsonc $file }
  catch { Fail "$name : invalid JSONC ($_)"; return }
  if ($json.bindings) {
    foreach ($prop in $json.bindings.PSObject.Properties) {
      Test-Chord $prop.Name "$name binding '$($prop.Name)'"
      Test-ActionValue $prop.Value "$name binding '$($prop.Name)'"
    }
  }
  $wheels = @()
  if ($json.wheels) { $wheels = $json.wheels }
  if ($json.wheel) { $wheels = @($json.wheel) }
  foreach ($w in $wheels) {
    if ($null -eq $w.slots) { Fail "$name : wheel '$($w.name)' needs a slots array"; continue }
    if ($w.slots.Count -eq 0) { Write-Host "NOTE: $name wheel '$($w.name)' is empty (falls back to wheel 1)" -ForegroundColor Yellow; continue }
    foreach ($s in $w.slots) {
      if (-not $s.label) { Fail "$name : slot without label"; continue }
      Test-ActionValue $s "$name wheel slot '$($s.label)'"
    }
  }
  if ($json.voice -and $json.voice.dictionary -and $json.voice.dictionary -isnot [array]) {
    Fail "$name : voice.dictionary must be an array"
  }
  Write-Host "OK: $name"
}

Test-File (Join-Path $configDir 'vibeos.jsonc')
$appsDir = Join-Path $configDir 'apps'
if (Test-Path $appsDir) {
  foreach ($f in Get-ChildItem $appsDir -Filter '*.jsonc' | Sort-Object Name) { Test-File $f.FullName }
}

if ($errors -gt 0) { Write-Host "$errors error(s)" -ForegroundColor Red; exit 1 }
Write-Host 'All configs valid.'
