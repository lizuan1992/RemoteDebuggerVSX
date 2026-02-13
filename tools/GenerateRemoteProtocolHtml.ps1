param(
  [Parameter(Mandatory=$false)][string]$InputPath = "docs/RemoteProtocol.md",
  [Parameter(Mandatory=$false)][string]$OutputPath = "docs/RemoteProtocol.html"
)

$ErrorActionPreference = "Stop"

if (!(Test-Path -LiteralPath $InputPath)) {
  throw "Input markdown not found: $InputPath"
}

$md = Get-Content -Raw -LiteralPath $InputPath

function Get-AnchorId([string]$text) {
  if ([string]::IsNullOrWhiteSpace($text)) { return "" }
  $s = $text.Trim().ToLowerInvariant()
  $s = $s -replace '`', ''
  # Keep ascii letters/digits/spaces/hyphen/underscore
  $s = $s -replace '[^a-z0-9 _-]', ''
  $s = $s -replace '\s+', '-'
  $s = $s -replace '-+', '-'
  return $s.Trim('-')
}

# Parse headings for TOC
$headings = New-Object System.Collections.Generic.List[object]
foreach ($line in ($md -split "`r?`n")) {
  if ($line -match '^(#{1,6})\s+(.+?)\s*$') {
    $level = $Matches[1].Length
    $text = $Matches[2]
    $id = Get-AnchorId $text
    if ($id) {
      $headings.Add([pscustomobject]@{ Level=$level; Text=$text; Id=$id })
    }
  }
}

function EscapeHtml([string]$s) {
  return [System.Net.WebUtility]::HtmlEncode($s)
}

function InlineMarkdownToHtml([string]$s) {
  if ($null -eq $s) { return "" }

  # escape first, then re-introduce supported inline markup
  $x = EscapeHtml $s

  # inline code: `code`
  $x = $x -replace '`([^`]+)`', '<code>$1</code>'

  # bold / italic (basic)
  $x = $x -replace '\*\*([^*]+)\*\*', '<strong>$1</strong>'
  $x = $x -replace '\*([^*]+)\*', '<em>$1</em>'

  # links: [text](url)
  $x = $x -replace '\[([^\]]+)\]\(([^)]+)\)', '<a href="$2">$1</a>'

  return $x
}

function ColorizeJsonContent([string]$content) {
  if ([string]::IsNullOrEmpty($content)) { return $content }

  # Avoid double-colorizing if called more than once.
  if ($content -match 'tok-key|tok-keystr|tok-str|tok-num|tok-lit|tok-punc') {
    return $content
  }

  $c = $content

  # Keys: "key": => mark quoted key text as tok-keystr
  $c = [System.Text.RegularExpressions.Regex]::Replace(
    $c,
    '(&quot;)([^&]+?)(&quot;)(\s*:)',    '<span class="tok-keystr">$1$2$3</span>$4')
  # Protect key strings so the value-string pass won't recolor them.
  $keySentinelOpen = "@@KEY_OPEN@@"
  $keySentinelClose = "@@KEY_CLOSE@@"
  $c = ([string]$c).Replace([string]'</span>', [string]$keySentinelClose)

  # Strings (values): "..."
  $c = [System.Text.RegularExpressions.Regex]::Replace(
    $c,
    '(&quot;)([^&]*?)(&quot;)',
    '<span class="tok-str">$1$2$3</span>')

  # Restore protected key spans.
  $c = ([string]$c).Replace([string]$keySentinelOpen, [string]'<span class="tok-keystr">')
  $c = ([string]$c).Replace([string]$keySentinelClose, [string]'</span>')

  # Numbers
  $c = [System.Text.RegularExpressions.Regex]::Replace(
    $c,
    '(?<![\w])(-?\d+(?:\.\d+)?)(?![\w])',
    '<span class="tok-num">$1</span>')

  # true/false/null
  $c = [System.Text.RegularExpressions.Regex]::Replace(
    $c,
    '(?i)(?<![\w])(true|false|null)(?![\w])',
    '<span class="tok-lit">$1</span>')

  # Punctuation
  $c = [System.Text.RegularExpressions.Regex]::Replace(
    $c,
    '([\{\}\[\],:])',
    '<span class="tok-punc">$1</span>')

  return $c
}

function ColorizeJsonInCodeBlocks([string]$html) {
  if ([string]::IsNullOrEmpty($html)) { return $html }

  # 1) Fenced code blocks: <pre><code ...>...</code></pre>
  $blockPattern = '(?s)<pre><code(?<attrs>[^>]*)>(?<content>.*?)</code></pre>'

  $html = [System.Text.RegularExpressions.Regex]::Replace($html, $blockPattern, {
    param($m)

    $attrs = $m.Groups['attrs'].Value
    $content = $m.Groups['content'].Value

    $trim = $content.TrimStart()
    $isJson = $trim.StartsWith('{') -or $trim.StartsWith('[')
    if (-not $isJson) {
      return $m.Value
    }

    $c = ColorizeJsonContent $content

    $open = '<pre><code'
    if ($attrs -notmatch '\bclass\s*=') {
      $open = $open + $attrs + ' class="json-colored"'
    }
    else {
      $open = $open + ($attrs -replace 'class="', 'class="json-colored ')
    }

    return $open + '>' + $c + '</code></pre>'
  })

  # 2) Inline JSON examples: <code>{ ... }</code> or <code>[ ... ]</code>
  # Only colorize when the code content starts with '{' or '[' (after trimming), to avoid affecting normal inline tokens.
  $inlinePattern = '(?s)<code>(?<content>[^<]*?)</code>'

  $html = [System.Text.RegularExpressions.Regex]::Replace($html, $inlinePattern, {
    param($m)

    $content = $m.Groups['content'].Value
    $trim = $content.TrimStart()

    $isJson = $trim.StartsWith('{') -or $trim.StartsWith('[')
    if (-not $isJson) {
      return $m.Value
    }

    $c = ColorizeJsonContent $content
    return '<code class="json-colored">' + $c + '</code>'
  })

  return $html
}

# Very small markdown renderer for this doc: headings, paragraphs, lists, code fences, inline code, tables.
$lines = $md -split "`r?`n"

$body = New-Object System.Text.StringBuilder
$inCode = $false
$codeLang = ""
$codeBuf = New-Object System.Text.StringBuilder

$inUl = $false
$inOl = $false

$inTable = $false
$tableHeader = $null
$tableAlign = $null
$tableRows = New-Object System.Collections.Generic.List[string[]]

function FlushList() {
  if ($script:inUl) {
    $body.AppendLine('</ul>') | Out-Null
    $script:inUl = $false
  }
  if ($script:inOl) {
    $body.AppendLine('</ol>') | Out-Null
    $script:inOl = $false
  }
}

function FlushTable() {
  if (-not $script:inTable) { return }

  $body.AppendLine('<table>') | Out-Null

  if ($script:tableHeader -ne $null) {
    $body.AppendLine('  <thead><tr>') | Out-Null
    foreach ($h in $script:tableHeader) {
      $body.AppendLine("    <th>$(InlineMarkdownToHtml $h)</th>") | Out-Null
    }
    $body.AppendLine('  </tr></thead>') | Out-Null
  }

  $body.AppendLine('  <tbody>') | Out-Null
  foreach ($r in $script:tableRows) {
    $body.AppendLine('  <tr>') | Out-Null
    foreach ($c in $r) {
      $body.AppendLine("    <td>$(InlineMarkdownToHtml $c)</td>") | Out-Null
    }
    $body.AppendLine('  </tr>') | Out-Null
  }
  $body.AppendLine('  </tbody>') | Out-Null
  $body.AppendLine('</table>') | Out-Null

  $script:inTable = $false
  $script:tableHeader = $null
  $script:tableAlign = $null
  $script:tableRows = New-Object System.Collections.Generic.List[string[]]
}

function SplitTableRow([string]$row) {
  $r = $row.Trim()
  if ($r.StartsWith('|')) { $r = $r.Substring(1) }
  if ($r.EndsWith('|')) { $r = $r.Substring(0, $r.Length - 1) }
  $parts = $r -split '\s*\|\s*'
  return ,$parts
}

function IsTableSeparator([string]$row) {
  $r = $row.Trim()
  if ($r -notmatch '^\|') { return $false }
  return $r -match '^\|\s*:?-+:?\s*(\|\s*:?-+:?\s*)+\|\s*$'
}

function IsTableRow([string]$row) {
  $r = $row.Trim()
  return ($r.StartsWith('|') -and $r.Contains('|'))
}

for ($i = 0; $i -lt $lines.Length; $i++) {
  $line = $lines[$i]

  # code fences
  if ($line -match '^```\s*([^\s`]*)\s*$') {
    if (-not $inCode) {
      FlushList
      FlushTable
      $inCode = $true
      $codeLang = $Matches[1]
      $codeBuf.Clear() | Out-Null
    } else {
      $inCode = $false
      $langClass = if ([string]::IsNullOrWhiteSpace($codeLang)) { '' } else { (' class="language-{0}"' -f $codeLang) }
      $codeHtml = EscapeHtml($codeBuf.ToString().TrimEnd("`r", "`n"))
      $body.AppendLine(('<pre><code{0}>{1}</code></pre>' -f $langClass, $codeHtml)) | Out-Null
      $codeLang = ''
    }
    continue
  }

  if ($inCode) {
    $codeBuf.AppendLine($line) | Out-Null
    continue
  }

  # horizontal rule
  if ($line.Trim() -eq '---') {
    FlushList
    FlushTable
    $body.AppendLine('<hr />') | Out-Null
    continue
  }

  # headings
  if ($line -match '^(#{1,6})\s+(.+?)\s*$') {
    FlushList
    FlushTable
    $level = $Matches[1].Length
    $text = $Matches[2]
    $id = Get-AnchorId $text
    if ([string]::IsNullOrWhiteSpace($id)) { $id = ('h-{0}' -f $i) }
    $body.AppendLine(('<h{0} id="{1}">{2}</h{0}>' -f $level, $id, (InlineMarkdownToHtml $text))) | Out-Null
    continue
  }

  # tables (github style)
  if ($inTable) {
    if (IsTableRow $line) {
      if ($tableAlign -ne $null -and (IsTableSeparator $line)) {
        continue
      }
      $tableRows.Add((SplitTableRow $line))
      continue
    }

    # end of table
    FlushTable
  }

  if (-not $inTable) {
    # detect table start: header row + separator row
    if (($i + 1) -lt $lines.Length -and (IsTableRow $line) -and (IsTableSeparator $lines[$i + 1])) {
      FlushList
      $inTable = $true
      $tableHeader = SplitTableRow $line
      $tableAlign = $lines[$i + 1]
      $tableRows = New-Object System.Collections.Generic.List[string[]]
      $i++
      continue
    }
  }

  # unordered list
  if ($line -match '^\s*[-*]\s+(.+)$') {
    FlushTable
    if (-not $inUl) {
      FlushList
      $inUl = $true
      $body.AppendLine('<ul>') | Out-Null
    }
    $body.AppendLine(('  <li>{0}</li>' -f (InlineMarkdownToHtml $Matches[1]))) | Out-Null
    continue
  }

  # ordered list
  if ($line -match '^\s*\d+\.\s+(.+)$') {
    FlushTable
    if (-not $inOl) {
      FlushList
      $inOl = $true
      $body.AppendLine('<ol>') | Out-Null
    }
    $body.AppendLine(('  <li>{0}</li>' -f (InlineMarkdownToHtml $Matches[1]))) | Out-Null
    continue
  }

  # blank line
  if ([string]::IsNullOrWhiteSpace($line)) {
    FlushList
    FlushTable
    continue
  }

  # paragraph
  FlushList
  FlushTable
  $body.AppendLine(('<p>{0}</p>' -f (InlineMarkdownToHtml $line.Trim()))) | Out-Null
}

FlushList
FlushTable

# Build a simple TOC from headings.
$tocSb = New-Object System.Text.StringBuilder
$tocSb.AppendLine('<ul>') | Out-Null
foreach ($h in $headings) {
  if ($h.Level -lt 2 -or $h.Level -gt 4) { continue }
  $indent = ''
  if ($h.Level -eq 3) { $indent = '  ' }
  elseif ($h.Level -eq 4) { $indent = '    ' }

  $tocLine = '{0}<li><a href="#{1}">{2}</a></li>' -f $indent, $h.Id, (EscapeHtml $h.Text)
  $tocSb.AppendLine($tocLine) | Out-Null
}
$tocSb.AppendLine('</ul>') | Out-Null

$html = @"
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Remote Debugger VSX ? Remote Runtime JSON Protocol</title>
  <style>
    :root {
      --bg: #ffffff;
      --fg: #1f2328;
      --muted: #57606a;
      --border: #d0d7de;
      --code-bg: #f6f8fa;
      --link: #0969da;

      /* VS-like JSON token palette (light) */
      --tok-key: #001080;   /* identifier-ish */
      --tok-keystr: #6f42c1;/* property name (distinct + non-red) */
      --tok-str: #a31515;   /* string */
      --tok-num: #098658;   /* number */
      --tok-lit: #0000ff;   /* keyword/literal */
      --tok-punc: #6a737d;  /* punctuation */

      --json-bg: #f6f8fa;
      --json-border: #d0d7de;
    }
    @media (prefers-color-scheme: dark) {
      :root {
        --bg: #0d1117;
        --fg: #c9d1d9;
        --muted: #8b949e;
        --border: #30363d;
        --code-bg: #161b22;
        --link: #58a6ff;

        /* VS-like JSON token palette (dark-ish) */
        --tok-key: #9cdcfe;
        --tok-keystr: #d2a8ff;
        --tok-str: #ce9178;
        --tok-num: #b5cea8;
        --tok-lit: #569cd6;
        --tok-punc: #8b949e;

        --json-bg: #0b1220;
        --json-border: #30363d;
      }
    }
    body { margin: 0; background: var(--bg); color: var(--fg); font-family: -apple-system,BlinkMacSystemFont,"Segoe UI",Helvetica,Arial,sans-serif; line-height: 1.55; }
    header { padding: 18px 20px; border-bottom: 1px solid var(--border); }
    header h1 { margin: 0 0 6px 0; font-size: 22px; font-weight: 650; }
    header .sub { color: var(--muted); font-size: 13px; }
    main { max-width: 1100px; margin: 0 auto; padding: 20px; }
    a { color: var(--link); text-decoration: none; }
    a:hover { text-decoration: underline; }
    nav.toc { border: 1px solid var(--border); border-radius: 10px; padding: 12px 14px; margin: 0 0 18px 0; }
    nav.toc h2 { margin: 0 0 8px 0; font-size: 16px; }
    nav.toc ul { margin: 0; padding-left: 18px; }
    nav.toc li { margin: 4px 0; }
    hr { border: none; border-top: 1px solid var(--border); margin: 18px 0; }
    code, pre { font-family: ui-monospace,SFMono-Regular,Menlo,Consolas,"Liberation Mono",monospace; font-size: 12.5px; }
    code { background: rgba(127,127,127,0.12); padding: 0.1em 0.35em; border-radius: 6px; }
    pre { background: var(--code-bg); border: 1px solid var(--border); border-radius: 10px; padding: 10px 12px; overflow: auto; }
    table { border-collapse: collapse; width: 100%; margin: 10px 0 16px 0; }
    th, td { border: 1px solid var(--border); padding: 8px 10px; vertical-align: top; }
    th { text-align: left; background: var(--code-bg); }
    .muted { color: var(--muted); }
    .badge { display: inline-block; padding: 2px 8px; border: 1px solid var(--border); border-radius: 999px; font-size: 12px; color: var(--muted); }
    .note { border-left: 3px solid var(--border); padding: 8px 12px; background: var(--code-bg); border-radius: 8px; margin: 10px 0 16px 0; }

    /* JSON highlighting */
    code.json-colored {
      background: var(--json-bg);
      border: 1px solid var(--json-border);
      padding: 0.12em 0.42em;
      border-radius: 6px;
    }
    pre code.json-colored {
      display: block;
      padding: 0;
      border: none;
      background: transparent;
    }

    code.json-colored .tok-keystr { color: var(--tok-keystr); font-weight: 650; }
    code.json-colored .tok-key { color: var(--tok-key); font-weight: 600; }
    code.json-colored .tok-str { color: var(--tok-str); }
    code.json-colored .tok-num { color: var(--tok-num); }
    code.json-colored .tok-lit { color: var(--tok-lit); font-weight: 600; }
    code.json-colored .tok-punc { color: var(--tok-punc); }
    code.json-colored { font-size: 13.5px; }
    pre code.json-colored { font-size: 13.5px; }
  </style>
</head>
<body>
  <header>
    <h1>Remote Debugger VSX ? Remote Runtime JSON Protocol</h1>
    <div class="sub"><span class="badge">Generated</span> from <code>$InputPath</code> by <code>tools/GenerateRemoteProtocolHtml.ps1</code></div>
  </header>
  <main>
    <nav class="toc" aria-label="Table of contents">
      <h2>Contents</h2>
      $($tocSb.ToString())
    </nav>
    $($body.ToString())
    <hr />
    <p class="muted">Generated on $(Get-Date -Format "yyyy-MM-dd HH:mm:ss") (local time). Source of truth: <code>$InputPath</code>.</p>
  </main>
</body>
</html>
"@

$html = ColorizeJsonInCodeBlocks $html

$outDir = [System.IO.Path]::GetDirectoryName((Resolve-Path -LiteralPath (Split-Path -Parent $OutputPath) -ErrorAction SilentlyContinue))
New-Item -ItemType Directory -Force -Path ([System.IO.Path]::GetDirectoryName($OutputPath)) | Out-Null
[System.IO.File]::WriteAllText($OutputPath, $html, [System.Text.Encoding]::UTF8)
Write-Host "Generated $OutputPath"