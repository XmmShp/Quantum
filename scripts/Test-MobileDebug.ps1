param([ValidateRange(1024,65535)][int]$Port=9224)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$invoke=Join-Path $PSScriptRoot 'Invoke-MobileDebug.ps1'
$reservation=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$Port)
try{$reservation.Start()}finally{$reservation.Stop()}
$token=[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$names=@('QUANTUM_MOBILE_DEBUG_PORT','QUANTUM_MOBILE_DEBUG_TOKEN','QUANTUM_MOBILE_DEBUG_PREVIEW')
$previous=@{}
foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name)}
$process=$null
try{
    $env:QUANTUM_MOBILE_DEBUG_PORT=[string]$port
    $env:QUANTUM_MOBILE_DEBUG_TOKEN=$token
    $env:QUANTUM_MOBILE_DEBUG_PREVIEW='1'
    $directory=Join-Path $repo 'mobile/Quantum.Mobile/bin/Debug/net10.0-windows10.0.19041.0/win-x64'
    if(!(Test-Path (Join-Path $directory 'Quantum.Mobile.exe'))){throw 'Build the Windows Debug target first.'}
    $process=Start-Process -FilePath (Join-Path $directory 'Quantum.Mobile.exe') -WorkingDirectory $directory -WindowStyle Hidden -PassThru
    function Call([string]$path,[hashtable]$body){
        if($null -eq $body){return & $invoke -Port $Port -Path $path}
        return & $invoke -Port $Port -Method POST -Path $path -Json ($body|ConvertTo-Json -Compress)
    }
    function Reject([string]$path,[hashtable]$body,[int]$status){
        try{$null=Call $path $body;throw 'Expected HTTP rejection.'}
        catch [Microsoft.PowerShell.Commands.HttpResponseException]{if([int]$_.Exception.Response.StatusCode -ne $status){throw}}
    }
    $state=$null
    for($attempt=0;$attempt -lt 20;$attempt++){
        try{$state=Call '/state' $null;if($state.value.preview){break}}catch{}
        Start-Sleep -Milliseconds 300
    }
    if(!$state.value.preview -or $state.value.authenticated){throw 'Expected isolated, unauthenticated Mock startup.'}
    if($state.value.page.tree.cornerRadius -ne 0){throw 'Layout root clips its content to rounded card corners.'}
    $view=$state.value.page.viewId
    $null=Call '/input' @{viewId=$view;nodeId='content';value='HTTP debug test 中文'}
    $null=Call '/input' @{viewId=$view;nodeId='pinned';value=$true}
    $attachment=@{id=[guid]::NewGuid().ToString('N');name='sample.txt';contentType='text/plain';size=3;sha256=('a'*64)}
    $null=Call '/input' @{viewId=$view;nodeId='attachments';value=@($attachment)}
    $live=Call '/preview/update' @{}
    if($live.value.persistentHeader -or $live.value.syncPanelVisible){throw 'Plugin viewport contains persistent app chrome.'}
    $content=$live.value.page.tree.children | Where-Object id -eq 'content'
    if($live.value.page.schemaVersion -ne 2 -or @(($live.value.page.tree.children | Where-Object id -eq attachments).value).Count -ne 1){throw 'Native attachment input was not preserved by a live frame.'}
    $pinned=$live.value.page.tree.children | Where-Object id -eq 'pinned'
    $title=$live.value.page.tree.children | Where-Object id -eq 'title'
    if($content.value -ne 'HTTP debug test 中文' -or !$pinned.value -or !$live.value.page.dirty -or $title.text -ne 'Live update received'){throw 'A live snapshot replaced draft inputs or failed to update native content.'}
    $view=$live.value.page.viewId
    $refreshed=Call '/page/pull-refresh' @{}
    $content=$refreshed.value.page.tree.children | Where-Object id -eq 'content'
    if($refreshed.value.refresh.active -or !$refreshed.value.refresh.enabled -or $refreshed.value.refresh.count -ne ($live.value.refresh.count+1) -or $refreshed.value.syncPanelVisible){throw 'Pull refresh did not complete and retract without a persistent status panel.'}
    if($content.value -ne 'HTTP debug test 中文' -or !$refreshed.value.page.dirty -or $refreshed.value.page.viewId -eq $view){throw 'Pull refresh lost the draft or did not render a fresh snapshot.'}
    $view=$refreshed.value.page.viewId
    if($refreshed.value.navigation.iconButtons.Count -ne 2 -or ![string]::IsNullOrEmpty($refreshed.value.navigation.iconButtons[0]) -or !$refreshed.value.navigation.shortcuts[0].hasImage -or $refreshed.value.navigation.iconButtons[1] -ne '✦'){throw 'Bottom bar is not using icon-only plugin navigation.'}
    $reordered=Call '/navigation/reorder' @{source='mock.status/mock-status';target='mock/preview'}
    if($reordered.value.page.viewId -ne $view -or !$reordered.value.page.dirty -or $reordered.value.navigation.iconButtons[0] -ne '✦'){throw 'Reordering changed the current draft or failed to update icon order.'}
    if(!$live.value.titleBar.visible -or $live.value.titleBar.height -gt 44 -or $live.value.titleBar.avatar -ne 'mobile-account-avatar'){throw 'Thin avatar title bar is missing.'}
    $menu=Call '/menu/open' @{}
    if(!$menu.value.menu.expanded -or !$menu.value.menu.visible -or !$menu.value.menu.settingsPinnedToBottom){throw 'Left account menu or pinned Settings is missing.'}
    Reject '/page/pull-refresh' @{} 409
    Reject '/press' @{viewId=$view;nodeId='save'} 409
    $settings=Call '/settings/open' @{}
    if(!$settings.value.settingsVisible -or $settings.value.menu.expanded){throw 'Settings did not replace the account menu with a separate page.'}
    $returned=Call '/settings/close' @{}
    if($returned.value.settingsVisible -or $returned.value.page.viewId -ne $view -or !$returned.value.page.dirty){throw 'Returning from settings replaced the plugin draft.'}
    $after=Call '/press' @{viewId=$view;nodeId='save'}
    if($after.value.status -notmatch 'HTTP debug test' -or $after.value.status -notmatch 'true'){throw 'Native button callback did not see the actual bound inputs.'}
    $content=$after.value.page.tree.children | Where-Object id -eq 'content'
    if(@(($after.value.page.tree.children | Where-Object id -eq attachments).value).Count -ne 0){throw 'Submitted attachment references were not cleared immediately.'}
    if($content.value -ne '' -or !$after.value.pending -or !$after.value.actionFeedback.visible){throw 'Submission did not clear inputs and show immediate queued feedback.'}
    Reject '/press' @{viewId=$view;nodeId='save'} 409
    $null=Call '/input' @{viewId=$view;nodeId='content';value='Next draft'}
    $success=Call '/preview/outcome' @{status='succeeded'}
    if(($success.value.page.tree.children | Where-Object id -eq 'content').value -ne 'Next draft' -or $success.value.pending){throw 'Delayed success overwrote the next draft.'}
    $null=Call '/press' @{viewId=$view;nodeId='save'}
    $failed=Call '/preview/outcome' @{status='failed'}
    if(($failed.value.page.tree.children | Where-Object id -eq 'content').value -ne 'Next draft' -or $failed.value.pending){throw 'Known failure did not restore the submitted draft.'}

    Reject '/input' @{viewId='stale';nodeId='content';value='must not apply'} 409
    Reject '/input' @{viewId=$view;nodeId='content';value=$false} 400
    Reject '/input' @{viewId=$view;nodeId='content';value=('x'*501)} 400
    Reject '/press' @{viewId=$view;nodeId='not-present'} 404
    Reject '/preview' @{} 409
    $opened=Call '/navigation/open' @{}
    if(!$opened.value.navigation.expanded){throw 'Bottom navigation sheet did not open.'}
    if($opened.value.navigation.gridColumns -ne 4 -or $opened.value.navigation.tiles.Count -ne 2 -or $opened.value.navigation.tiles[1].column -ne 1 -or $opened.value.navigation.tiles[1].row -ne 0 -or !$opened.value.navigation.tiles[1].hasImage){throw 'Expanded plugin navigation is not a four-column icon matrix.'}
    Reject '/navigation/select' @{pluginId='mock.status';pageId='mock-status'} 409
    $switched=Call '/navigation/select' @{pluginId='mock.status';pageId='mock-status';discardDraft=$true}
    if($switched.value.navigation.expanded -or $switched.value.page.pageId -ne 'mock-status' -or $switched.value.page.viewId -eq $view){throw 'Navigation did not switch to a fresh page and close its sheet.'}
    Reject '/press' @{viewId=$view;nodeId='save'} 409
    $preview=Call '/navigation/select' @{pluginId='mock';pageId='preview';discardDraft=$true}
    $view=$preview.value.page.viewId
    $null=Call '/input' @{viewId=$view;nodeId='content';value='Uncertain outcome'}
    $null=Call '/press' @{viewId=$view;nodeId='save'}
    $unknown=Call '/preview/outcome' @{status='unknown'}
    if(!$unknown.value.pending -or !$unknown.value.uncertain){throw 'Unknown outcome was cleared or made eligible for resubmission.'}
    Reject '/press' @{viewId=$view;nodeId='save'} 409
    $json=(Call '/state' $null)|ConvertTo-Json -Depth 24
    if($json.Contains($token) -or $json -match 'accessToken|refreshToken|deviceSecret|password'){throw 'Credential material appeared in the debug state.'}
    [ordered]@{passed=$true;mode='isolated native Mock';pullRefreshCompletesAndRetracts=$true;pullRefreshPreservesDraft=$true;thinAvatarTitleBar=$true;leftAccountMenu=$true;settingsPinnedAtBottom=$true;iconOnlyNavigation=$true;staticIconWithTextFallback=$true;reorderPreservesDraft=$true;readPageTree=$true;nativeTextAndToggleBinding=$true;liveUpdatePreservesDraft=$true;unobstructedViewport=$true;separateSettingsRetainsDraft=$true;nativeButtonCallback=$true;immediateInputReset=$true;pendingFeedbackVisible=$true;nextDraftPreserved=$true;failedDraftRestored=$true;unknownOutcomeBlocksReplay=$true;attachmentBindingAndReset=$true;staleViewRejected=$true;wrongTypeAndLengthRejected=$true;unknownButtonRejected=$true;dirtyDraftProtected=$true;bottomNavigationSheet=$true;pluginSwitchCreatesFreshView=$true;credentialsExposed=$false;keyboardOrMouseUsed=$false}|ConvertTo-Json
}finally{
    if($process -and !$process.HasExited){Stop-Process -Id $process.Id}
    foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name])}
}
