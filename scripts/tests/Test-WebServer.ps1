param(
    [string]$Configuration = 'Debug',
    [switch]$WithoutPassword,
    [switch]$EmptyCatalog,
    [switch]$BrowserInputs,
    [string]$ExecutablePath = ''
)

# Isolated HTTP contract smoke test; BrowserInputs optionally adds headless UI checks. Never executes a flow.
$ErrorActionPreference = 'Stop'
if ($BrowserInputs -and (-not $WithoutPassword -or $EmptyCatalog)) { throw 'BrowserInputs requires WithoutPassword and a fixture catalog.' }
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
    ProjectGuid = '2c312829-4c61-423c-aa6b-ef85f20e3f1c'; ProjectName = 'HTTP contract fixture'; ProjectDescription = 'Isolated HTTP test; never executed.'; Notice = 'Setup: https://example.com/'
    FlowBlocks = @(); ManagedObjects = @(); ProjectDependendDataObjects = @(); InputFiles = @()
    UserFields = @(
        @{ Name = 'Suggested'; UserField = $true; UserFieldType = 'Input'; Index = 0; StringValue = 'initial default'; FieldType = @{ FieldType = 'Text' }; ListOfValues = @(@{ DisplayName = 'Demo suggestion'; Value = 'demo-suggestion' }) }
        @{ Name = 'Allowed'; UserField = $true; UserFieldType = 'Input'; Index = 1; FieldType = @{ FieldType = 'Text' }; LoVType = 'PossibleValues'; ListOfValues = @(@{ DisplayName = 'Allowed demo'; Value = 'allowed-demo' }) }
        @{ Name = 'LocalPassword'; IsRequiredAtRuntimeStartup = $true; UserField = $true; UserFieldType = 'Input'; Index = 2; FieldType = @{ FieldType = 'Text' }; IsPassword = $true; StoreValueLocally = $true; StringValue = 'must-not-render-local-secret' }
        @{ Name = 'FalseDefault'; UserField = $true; UserFieldType = 'Input'; Index = 3; IsRequiredAtRuntimeStartup = $true; DefaultValue = 'false'; FieldType = @{ FieldType = 'Boolean' } }
        @{ Name = 'ZeroDefault'; UserField = $true; UserFieldType = 'Input'; Index = 4; IsRequiredAtRuntimeStartup = $true; DefaultValue = '0'; FieldType = @{ FieldType = 'Integer' } }
    )
}
if ($BrowserInputs) {
    $fixture.UserFields += @(
        @{ Name = 'Multiline'; UserField = $true; UserFieldType = 'Input'; DefaultValue = "first`nsecond"; FieldType = @{ FieldType = 'Text' } }
        @{ Name = 'ListFixture'; UserField = $true; UserFieldType = 'Input'; DefaultValue = 'alpha; beta;;'; FieldType = @{ FieldType = 'Text' } }
    )
}
if (-not $EmptyCatalog) {
    [IO.File]::WriteAllText((Join-Path $projectsDirectory 'fixture.fbprj'), ($fixture | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
    if ($BrowserInputs) {
        $second = $fixture.Clone()
        $second.ProjectGuid = '8eb64b97-a31b-4ab9-933e-edf9ce2bc58c'
        $second.ProjectName = 'Second input fixture'
        [IO.File]::WriteAllText((Join-Path $projectsDirectory 'second.fbprj'), ($second | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
    }
    $localData = @{ LocalUserFieldValues = @{ Suggested = 'local developer value'; LocalPassword = 'must-not-render-local-secret' } }
    [IO.File]::WriteAllText((Join-Path $projectsDirectory 'fixture.fblocaldata'), ($localData | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
} else {
    $demoProject = Join-Path $repoDirectory 'FlowBlox.Build/data/example_project/CsvToJson.fbprj'
    if (-not (Test-Path -LiteralPath $demoProject)) { throw 'Bundled example fixture is missing.' }
    $recent = @(@{ ProjectName = 'Bundled desktop example fixture'; ProjectFilePath = $demoProject })
    [IO.File]::WriteAllText((Join-Path $testDirectory 'recentProjects.json'), (ConvertTo-Json -InputObject $recent), [Text.UTF8Encoding]::new($false))
}
$abandonedId = [Guid]::NewGuid()
$missingProcessId = [Guid]::NewGuid()
$history = @(
    @{ Id = $abandonedId; Project = @{ Name = 'Abandoned launch fixture' }; StartedUtc = [DateTime]::UtcNow.AddMinutes(-5); RequestFile = ''; ResponseFile = '' }
    @{ Id = $missingProcessId; Project = @{ Name = 'Missing runner fixture' }; StartedUtc = [DateTime]::UtcNow.AddMinutes(-5); ProcessId = [int]::MaxValue; ProcessStartedUtc = [DateTime]::UtcNow.AddMinutes(-5); RequestFile = ''; ResponseFile = '' }
)
[IO.File]::WriteAllText((Join-Path $testDirectory 'fbexecutions.json'), ($history | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
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
        if ($BrowserInputs) {
            & node (Join-Path $PSScriptRoot 'web-input-editors.mjs') $base (Join-Path $testDirectory 'browser') (Join-Path $repoDirectory 'FlowBlox.Avalonia.IntegrationTests/TestResults/web-input-editors')
            if ($LASTEXITCODE -ne 0) { throw 'Interactive input editor checks failed.' }
        }
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
    if ($EmptyCatalog) {
        if ($html -match '/projects/[A-F0-9]+' -or $html -match 'Bundled desktop example fixture') { throw 'Bundled desktop examples leaked into the web catalog.' }
        if ($html -notmatch '<section class="notice" role="status">' -or $html -notmatch 'Project Space') { throw 'Empty catalog setup guidance is missing.' }
        Write-Host 'Passed: empty catalog setup guidance and exclusion of bundled desktop examples.'
        return
    }
    $projectPath = [regex]::Match($html, '/projects/[A-F0-9]+').Value
    if (-not $projectPath) { throw 'The fixture project is missing.' }
    $projectResponse = $client.GetAsync($base + $projectPath).GetAwaiter().GetResult()
    $projectHtml = $projectResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    if ([int]$projectResponse.StatusCode -ne 200 -or $projectHtml -notmatch 'Demo suggestion' -or $projectHtml -notmatch 'allowed-demo') {
        Write-Host ('Project response: ' + [int]$projectResponse.StatusCode)
        throw 'Project input form did not render suggestions and allowed values.'
    }
    if ($projectHtml -match 'must-not-render-local-secret') { throw 'Locally stored field value was rendered.' }
    if ($projectHtml -notmatch '<option value="00000000-0000-0000-0000-000000000000"') { throw 'Local developer configuration is missing.' }
    if ($projectHtml -match 'local developer value') { throw 'Local profile values were applied without selection.' }
    if ($projectHtml -notmatch '<input[^>]*value="initial default"') { throw 'Project default did not populate the text field.' }
    if ([regex]::Matches($projectHtml, 'field-value-status filled').Count -ne $(if ($BrowserInputs) { 5 } else { 3 })) { throw 'Defaults, false or zero were not marked as supplied.' }
    if ([regex]::Matches($projectHtml, 'field-value-status missing').Count -ne 1) { throw 'The empty required password was not marked as missing.' }
    if ([regex]::Matches($projectHtml, 'field-value-status empty').Count -ne 1) { throw 'The empty optional input was not neutral.' }
    if ($projectHtml -notmatch '<input[^>]*aria-required="true"[^>]*aria-describedby="field-[^"]+-status"[^>]*type="password"') { throw 'Password completion is not accessible.' }
    foreach ($icon in @('circle', 'star', 'lock-outline')) {
        if ([int]$client.GetAsync("$base/icons/$icon.svg").GetAwaiter().GetResult().StatusCode -ne 200) { throw "Field icon is not served: $icon" }
    }
    Write-Host 'Passed: input completion, required password accessibility and false/zero defaults.'
    $artifactResponse = $client.GetAsync($base + $projectPath + '/artifacts').GetAwaiter().GetResult()
    if ([int]$artifactResponse.StatusCode -ne 200) { throw 'Artifact browser failed.' }
    $recovered = $false
    for ($attempt = 0; $attempt -lt 15; $attempt++) {
        $executionsResponse = $client.GetAsync("$base/executions").GetAwaiter().GetResult()
        $executionsHtml = $executionsResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if ([int]$executionsResponse.StatusCode -ne 200) { throw 'Execution history failed to render.' }
        if ($executionsHtml -notmatch 'Abandoned launch fixture' -and $executionsHtml -match 'Missing runner fixture' -and $executionsHtml -notmatch 'class="loader"') { $recovered = $true; break }
        Start-Sleep -Seconds 1
    }
    if (-not $recovered) { throw 'Abandoned or missing-runner execution remained loading.' }
    if ($executionsHtml -notmatch 'icons/delete.svg') { throw 'Execution list removal action is missing.' }
    $executionResponse = $client.GetAsync("$base/executions/$missingProcessId").GetAwaiter().GetResult()
    $executionHtml = $executionResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    if ([int]$executionResponse.StatusCode -ne 200 -or $executionHtml -notmatch 'icons/delete.svg') { throw 'Execution detail removal action is missing.' }
    $removedResponse = $client.GetAsync("$base/executions/$abandonedId").GetAwaiter().GetResult()
    if ([int]$removedResponse.StatusCode -ne 200) { throw 'Automatically removed execution detail failed to render.' }
    $removedLogResponse = $client.GetAsync("$base/logs/$abandonedId").GetAwaiter().GetResult()
    if ([int]$removedLogResponse.StatusCode -ne 404) { throw 'Removed execution log did not return not found.' }
    $badProjectResponse = $client.GetAsync("$base/projects/missing-project").GetAwaiter().GetResult()
    $badProjectHtml = $badProjectResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    if ([int]$badProjectResponse.StatusCode -ne 200 -or $badProjectHtml -match 'class="loading-line"' -or $badProjectHtml -notmatch 'Retry') { throw 'Failed project loading did not offer retry without a spinner.' }
    Write-Host 'Passed: orphan recovery, execution removal actions and failed-project retry rendering.'
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
