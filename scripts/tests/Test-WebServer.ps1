param(
    [string]$Configuration = 'Debug',
    [switch]$WithoutPassword,
    [string]$ExecutablePath = ''
)

# HTTP contract smoke test. Uses an isolated profile, no browser, project execution or external API.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
Add-Type -AssemblyName System.Security
$repoDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$testDirectory = Join-Path $repoDirectory ('.tmp-webserver-' + [Guid]::NewGuid().ToString('N'))
$executable = Join-Path $repoDirectory "FlowBlox.WebServer\bin\$Configuration\net10.0-windows10.0.19041.0\FlowBlox.WebServer.exe"
if ($ExecutablePath) { $executable = [IO.Path]::GetFullPath($ExecutablePath) }
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$listener.Stop()
$password = [Guid]::NewGuid().ToString('N')
$configurationJson = @{ BindAddress = '127.0.0.1'; Port = $port; AppPassword = $(if ($WithoutPassword) { '' } else { $password }) } | ConvertTo-Json -Depth 5 -Compress
$protected = [Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes($configurationJson), [Text.Encoding]::UTF8.GetBytes('FlowBlox.ProtectedData.WebServer.Configuration'), [Security.Cryptography.DataProtectionScope]::CurrentUser)
$xml = [xml]'<configuration><option_elements /></configuration>'
New-Item -ItemType Directory -Path $testDirectory | Out-Null
$options = @{
    'WebServer.Configuration' = @{ Type = 'Password'; Value = [Convert]::ToBase64String($protected) }
    'Paths.ProjectDir' = @{ Type = 'Text'; Value = (Join-Path $testDirectory 'projects') }
    'Paths.OutputDir' = @{ Type = 'Text'; Value = (Join-Path $testDirectory 'output') }
    'Paths.InputDir' = @{ Type = 'Text'; Value = (Join-Path $testDirectory 'input') }
}
foreach ($option in $options.GetEnumerator()) {
    $node = $xml.CreateElement('option_element')
    foreach ($attribute in @{ name = $option.Key; displayName = $option.Key; value = $option.Value.Value; desc = ''; type = $option.Value.Type; system = 'False'; isPlaceholderEnabled = 'False' }.GetEnumerator()) {
        $node.SetAttribute($attribute.Key, $attribute.Value)
    }
    [void]$xml.SelectSingleNode('/configuration/option_elements').AppendChild($node)
}
$xml.Save((Join-Path $testDirectory 'global_configuration.xml'))
$projectsDirectory = Join-Path $testDirectory 'projects'
New-Item -ItemType Directory -Path $projectsDirectory | Out-Null
$fixture = @{
    ProjectName = 'HTTP contract fixture'; ProjectDescription = 'Isolated HTTP test; never executed.'; Notice = 'Setup: https://example.com/'
    FlowBlocks = @(); ManagedObjects = @(); ProjectDependendDataObjects = @(); InputFiles = @()
    UserFields = @(
        @{ Name = 'Suggested'; UserField = $true; UserFieldType = 'Input'; Index = 0; StringValue = 'initial default'; FieldType = @{ FieldType = 'Text' }; ListOfValues = @(@{ DisplayName = 'Demo suggestion'; Value = 'demo-suggestion' }) }
        @{ Name = 'Allowed'; UserField = $true; UserFieldType = 'Input'; Index = 1; FieldType = @{ FieldType = 'Text' }; LoVType = 'PossibleValues'; ListOfValues = @(@{ DisplayName = 'Allowed demo'; Value = 'allowed-demo' }) }
        @{ Name = 'LocalPassword'; UserField = $true; UserFieldType = 'Input'; Index = 2; FieldType = @{ FieldType = 'Text' }; IsPassword = $true; StoreValueLocally = $true; StringValue = 'must-not-render-local-secret' }
    )
}
[IO.File]::WriteAllText((Join-Path $projectsDirectory 'fixture.fbprj'), ($fixture | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
$process = $null
$handler = [Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(10)
$base = "http://127.0.0.1:$port"
try {
    $process = Start-Process -FilePath $executable -ArgumentList @('--data-directory', ('"' + $testDirectory + '"')) -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $testDirectory 'stderr.log') -RedirectStandardOutput (Join-Path $testDirectory 'stdout.log')
    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        if ($process.HasExited) { throw 'Test web server exited during startup.' }
        try {
            $readyUrl = if ($WithoutPassword) { "$base/" } else { "$base/login" }
            $response = $client.GetAsync($readyUrl).GetAwaiter().GetResult()
            if ([int]$response.StatusCode -eq 200) { $ready = $true; break }
        } catch { Start-Sleep -Milliseconds 300 }
    }
    if (-not $ready) {
        $log = Join-Path $testDirectory 'webserver\server.log'
        if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -TotalCount 35 | Write-Host }
        throw 'Test web server did not become ready.'
    }
    $login = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    if ($WithoutPassword) {
        if ($login -notmatch '/projects/[A-F0-9]+') { throw 'Password-free dashboard is missing the fixture project.' }
        Write-Host 'Passed: direct dashboard access without an app password.'
        return
    }
    $token = [regex]::Match($login, 'name="__RequestVerificationToken" value="([^"]+)"').Groups[1].Value
    if (-not $token) { throw 'Login form is missing its antiforgery token.' }
    $anonymous = $client.GetAsync("$base/").GetAwaiter().GetResult()
    if ([int]$anonymous.StatusCode -ne 302) { throw 'Anonymous dashboard access was not redirected.' }
    foreach ($asset in @('app.css', '_framework/blazor.web.js')) {
        $assetResponse = $client.GetAsync("$base/$asset").GetAwaiter().GetResult()
        if ([int]$assetResponse.StatusCode -ne 200) { throw "Static asset missing: $asset" }
    }
    $form = [Collections.Generic.Dictionary[string,string]]::new()
    $form['username'] = 'smoke'
    $form['password'] = $password
    $invalid = $client.PostAsync("$base/session/login", [Net.Http.FormUrlEncodedContent]::new($form)).GetAwaiter().GetResult()
    if ([int]$invalid.StatusCode -lt 400) { throw 'Login without antiforgery token was accepted.' }
    $form['__RequestVerificationToken'] = [Net.WebUtility]::HtmlDecode($token)
    $form['password'] = 'wrong-test-password'
    $wrongPassword = $client.PostAsync("$base/session/login", [Net.Http.FormUrlEncodedContent]::new($form)).GetAwaiter().GetResult()
    if ([int]$wrongPassword.StatusCode -ne 302 -or $wrongPassword.Headers.Location.ToString() -ne '/login?invalid=true') { throw 'Incorrect app password was accepted.' }
    $form['password'] = $password
    $signedIn = $client.PostAsync("$base/session/login", [Net.Http.FormUrlEncodedContent]::new($form)).GetAwaiter().GetResult()
    if ([int]$signedIn.StatusCode -ne 302 -or $signedIn.Headers.Location.ToString() -ne '/') { throw 'Valid login failed.' }
    $dashboard = $client.GetAsync("$base/").GetAwaiter().GetResult()
    if ([int]$dashboard.StatusCode -ne 200) { throw 'Authenticated dashboard failed.' }
    $html = $dashboard.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $projectPath = [regex]::Match($html, '/projects/[A-F0-9]+').Value
    if (-not $projectPath) { throw 'The fixture project is missing.' }
    $projectResponse = $client.GetAsync($base + $projectPath).GetAwaiter().GetResult()
    $projectHtml = $projectResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    if ([int]$projectResponse.StatusCode -ne 200 -or $projectHtml -notmatch 'Demo suggestion' -or $projectHtml -notmatch 'allowed-demo') {
        Write-Host ('Project response: ' + [int]$projectResponse.StatusCode)
        throw 'Project input form did not render suggestions and allowed values.'
    }
    if ($projectHtml -match 'must-not-render-local-secret') { throw 'Locally stored field value was rendered.' }
    if ($projectHtml -notmatch '(?s)<textarea[^>]*>initial default</textarea>') { throw 'Project default did not populate the text field.' }
    $artifactResponse = $client.GetAsync($base + $projectPath + '/artifacts').GetAwaiter().GetResult()
    if ([int]$artifactResponse.StatusCode -ne 200) { throw 'Artifact browser failed.' }
    Write-Host 'Passed: anonymous access, antiforgery, login, authenticated dashboard and static Blazor assets.'
} catch {
    $log = Join-Path $testDirectory 'webserver\server.log'
    if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Tail 45 | Write-Host }
    foreach ($errorLog in @('stderr.log', 'webserver\startup.log')) { $failureFile=Join-Path $testDirectory $errorLog; if (Test-Path -LiteralPath $failureFile) { Get-Content -LiteralPath $failureFile -Tail 25 | Write-Host } }
    throw
} finally {
    $client.Dispose()
    $handler.Dispose()
    if ($process -and -not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    $resolvedTestDirectory = [IO.Path]::GetFullPath($testDirectory)
    if (-not $resolvedTestDirectory.StartsWith($repoDirectory + '\.tmp-webserver-', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected test directory.' }
    Remove-Item -LiteralPath $resolvedTestDirectory -Recurse -Force
}
