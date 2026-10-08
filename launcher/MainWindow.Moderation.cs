using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace PlutoniumLauncher;

public partial class MainWindow
{
    private readonly ModerationService _moderation = new();
    private readonly Border _maintenancePanel = new() { Background = new SolidColorBrush(Color.FromRgb(12,12,14)), Visibility = Visibility.Collapsed };
    private readonly TextBlock _maintenanceMessage = new() { TextWrapping = TextWrapping.Wrap, FontSize = 15, Foreground = Brushes.LightGray, Margin = new Thickness(0,20,0,20) };
    private readonly DispatcherTimer _moderationTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly CancellationTokenSource _moderationLifetime = new();
    private bool _moderationPolling;
    private bool _serviceConnected;
    private bool _maintenance;
    private bool _policyAvailable;
    private HashSet<string> _disabledFeatures = [];
    private TextBlock? _liveStats;

    private void InitializeModeration()
    {
        var content = new StackPanel { MaxWidth = 600, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(36) };
        content.Children.Add(Label("PLUTONIUM",12,true)); content.Children.Add(Label("We'll be back soon",32)); content.Children.Add(_maintenanceMessage);
        content.Children.Add(ActionButton("CHECK AGAIN", RefreshPolicyAsync));
        content.Children.Add(ActionButton("OWNER SIGN-IN", async () =>
        {
            await ConnectModerationAsync(true);
            if (!_moderation.OwnerCandidate) throw new InvalidOperationException("Only the configured owner can manage maintenance.");
            _maintenancePanel.Visibility = Visibility.Collapsed; Navigate("moderation"); await ShowModerationAsync();
        }));
        _maintenancePanel.Child = content; Grid.SetColumnSpan(_maintenancePanel,2); Panel.SetZIndex(_maintenancePanel,20); LauncherBody.Children.Add(_maintenancePanel);
        _moderationTimer.Tick += async (_,_) =>
        {
            if (_moderationPolling) return;
            _moderationPolling = true;
            try
            {
                if (_serviceConnected && !_moderation.HasSession && _accounts is not null && _config is not null && !string.IsNullOrEmpty(_config.SelectedAccountId))
                {
                    _session=await _accounts.AuthenticateAsync(_config.SelectedAccountId,_moderationLifetime.Token);
                    await _moderation.ConnectAsync(_session,_moderationLifetime.Token);
                }
                await RefreshPolicyAsync();
                if (_moderation.HasSession && _config is not null)
                    await _moderation.RequestAsync("heartbeat",HttpMethod.Post,new { kind="launcher", version=CurrentLauncherVersion, installationId=_config.InstallationId },_moderationLifetime.Token);
                if (_liveStats is { IsVisible:true } && _moderation.Role == "owner") await RefreshModerationStatsAsync();
            }
            catch (OperationCanceledException) { }
            catch { /* RefreshPolicy displays service failures. Keep polling without dumping credentials into logs. */ }
            finally { _moderationPolling = false; }
        };
        Closed += (_,_) => { _moderationTimer.Stop(); _moderationLifetime.Cancel(); _moderation.Dispose(); };
    }
    private async Task RefreshPolicyAsync()
    {
        if (!ModerationService.Configured) return;
        try
        {
            var policy = await _moderation.RequestAsync("config",HttpMethod.Get,cancellationToken:_moderationLifetime.Token);
            _maintenance=policy.GetProperty("maintenance").GetBoolean(); _policyAvailable=true;
            _disabledFeatures=policy.GetProperty("disabledFeatures").EnumerateArray().Select(v=>v.GetString()!).ToHashSet();
            _maintenanceMessage.Text=policy.GetProperty("message").GetString();
            if (_moderation.HasSession) await _moderation.RefreshIdentityAsync(_moderationLifetime.Token);
            else _moderation.Reset();
            _maintenancePanel.Visibility=_maintenance && _moderation.Role!="owner" ? Visibility.Visible:Visibility.Collapsed;
        }
        catch (OperationCanceledException) when (_moderationLifetime.IsCancellationRequested) { }
        catch
        {
            _policyAvailable=false;
            _maintenanceMessage.Text="We couldn't verify the launcher status. Check your connection and try again. Accounts and saved files remain on this PC.";
            _maintenancePanel.Visibility=Visibility.Visible;
        }
    }
    private async Task ConnectModerationAsync(bool chooseAccount = false)
    {
        if (_accounts is null || _config is null) throw new InvalidOperationException("Wait for launcher setup to finish.");
        if (chooseAccount || string.IsNullOrEmpty(_config.SelectedAccountId))
        {
            var result=await _accounts.AddAsync(_moderationLifetime.Token); _session=result.Session; _config.SelectedAccountId=result.Account.Id; await _config.SaveAsync();
            _moderation.Reset(); RefreshAccounts();
        }
        else _session=await _accounts.AuthenticateAsync(_config.SelectedAccountId,_moderationLifetime.Token);
        await _moderation.ConnectAsync(_session,_moderationLifetime.Token);
        _serviceConnected=true;
        await _moderation.RequestAsync("heartbeat",HttpMethod.Post,new {kind="launcher",version=CurrentLauncherVersion,installationId=_config.InstallationId},_moderationLifetime.Token);
    }
    private async Task RequireClientAccessAsync()
    {
        if (!ModerationService.Configured) return;
        await RefreshPolicyAsync();
        if (!_policyAvailable) throw new InvalidOperationException("Unable to verify Plutonium status. Try again when the service is available.");
        if (_session is null) throw new InvalidOperationException("Sign in to Minecraft first.");
        await _moderation.ConnectAsync(_session,OperationToken);
        _serviceConnected=true;
        var access=await _moderation.RequestAsync("heartbeat",HttpMethod.Post,new {kind="launcher",version=CurrentLauncherVersion,installationId=_config!.InstallationId},OperationToken);
        if (!access.GetProperty("allowed").GetBoolean()) throw new InvalidOperationException("Your Plutonium client access is disabled. "+access.GetProperty("reason").GetString()+" Appeal at https://plutoniumclient.vercel.app/appeal. Your launcher account and files remain accessible.");
        if (_maintenance) throw new InvalidOperationException("The launcher is currently under maintenance. The owner can disable maintenance in Settings → Moderation.");
    }
    private async Task RequireFeatureAsync(string feature)
    {
        if (!ModerationService.Configured) return;
        await RefreshPolicyAsync();
        if (!_policyAvailable || _maintenance || _disabledFeatures.Contains(feature)) throw new InvalidOperationException("This feature is currently unavailable under the launcher service policy.");
    }
    private async Task ShowModerationAsync()
    {
        _libraryPage.Tag="moderation"; _libraryPage.Children.Clear(); _liveStats=null;
        var content=new StackPanel(); _libraryPage.Children.Add(Scroll(content));
        content.Children.Add(Label("Moderation",28));
        if (!ModerationService.Configured)
        {
            content.Children.Add(Label("Server setup required",18));
            content.Children.Add(Label("Cloud moderation is not active in this build. The owner must deploy the API, configure Microsoft identity and bundle its HTTPS address before publishing. No local email check grants administrator access.",12,true));
            return;
        }
        content.Children.Add(Label("Only verified server permissions unlock these tools. Owner privileges expire after 15 minutes.",12,true));
        if (!_moderation.HasSession)
        {
            content.Children.Add(ActionButton("CONNECT CURRENT ACCOUNT",async()=>{await ConnectModerationAsync(); await ShowModerationAsync();})); return;
        }
        var me=await _moderation.RefreshIdentityAsync(_moderationLifetime.Token);
        content.Children.Add(Label($"{me.GetProperty("username").GetString()} · {_moderation.Role}",16));
        content.Children.Add(ActionButton("DISCONNECT MODERATION",async()=>{_serviceConnected=false;await _moderation.SignOutAsync(); await RefreshPolicyAsync(); await ShowModerationAsync();}));
        if (_moderation.OwnerCandidate && _moderation.Role!="owner")
        {
            var prompt=Label("Owner: authenticate justquirk.business@gmail.com with Microsoft to unlock global controls.",12,true); content.Children.Add(prompt);
            content.Children.Add(ActionButton("VERIFY OWNER WITH MICROSOFT",async()=>
            {
                var flow=await _moderation.RequestAsync("owner/start",HttpMethod.Post,new {},_moderationLifetime.Token);
                var interval=flow.GetProperty("interval").GetInt32();
                prompt.Text="Open microsoft.com/devicelogin and enter "+flow.GetProperty("userCode").GetString()+". Use your owner account. Waiting for confirmation…";
                OpenLocation("https://microsoft.com/devicelogin");
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(_moderationLifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(Math.Min(900,flow.GetProperty("expiresIn").GetInt32())));
                while(true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(interval),timeout.Token);
                    var result=await _moderation.RequestAsync("owner/poll",HttpMethod.Post,new {},timeout.Token);
                    if (!result.GetProperty("pending").GetBoolean()) break;
                    if(result.TryGetProperty("interval",out var next)) interval=next.GetInt32();
                }
                await _moderation.RefreshIdentityAsync(); await RefreshPolicyAsync(); await ShowModerationAsync();
            }));
        }
        if (_moderation.Role is not ("owner" or "admin")) { content.Children.Add(Label("This account has no moderation permissions.",12,true)); return; }
        if (_moderation.Role=="owner") { _liveStats=Label("Loading live statistics…",13); content.Children.Add(Card(_liveStats)); await RefreshModerationStatsAsync(); }
        var tabs=new WrapPanel();
        tabs.Children.Add(ActionButton("REFRESH",ShowModerationAsync));
        tabs.Children.Add(ActionButton("USERS",()=>ShowModerationUsersAsync(content)));
        tabs.Children.Add(ActionButton("APPEALS",()=>ShowAppealsAsync(content)));
        if(_moderation.Role=="owner") { tabs.Children.Add(ActionButton("GLOBAL SETTINGS",()=>ShowRemoteConfigAsync(content))); tabs.Children.Add(ActionButton("AUDIT LOG",()=>ShowAuditAsync(content))); }
        content.Children.Add(tabs);
    }
    private async Task RefreshModerationStatsAsync()
    {
        var data=await _moderation.RequestAsync("admin/stats",HttpMethod.Get,cancellationToken:_moderationLifetime.Token);
        if (_liveStats is null) return;
        var a=data.GetProperty("accounts"); var i=data.GetProperty("installs");var w=data.GetProperty("weekly");
        static string Growth(JsonElement value) { var before=value.GetProperty("last_week").GetInt32();var current=value.GetProperty("this_week").GetInt32(); return before==0 ? $"{current} this week · no previous baseline" : $"{current} this week · {(current-before)*100.0/before:+0.0;-0.0;0}% vs previous week"; }
        _liveStats.Text=$"Active accounts: {a.GetProperty("active")}    Running clients: {a.GetProperty("playing")}    Accounts seen: {a.GetProperty("total")}\nReported installations: {i.GetProperty("total")} · {Growth(i)}\nWeekly active accounts: {Growth(w)}\nDisabled accounts: {a.GetProperty("disabled")}    Pending appeals: {data.GetProperty("pendingAppeals")}\n\n{data.GetProperty("definition").GetString()}\nRefreshes every 30 seconds.";
    }
    private void ClearModerationDetail(StackPanel content)
    {
        foreach(var item in content.Children.OfType<FrameworkElement>().Where(v=>Equals(v.Tag,"moderation-detail")).ToArray()) content.Children.Remove(item);
    }
    private StackPanel ModerationDetail(StackPanel content)
    {
        ClearModerationDetail(content);var detail=new StackPanel {Tag="moderation-detail"}; content.Children.Add(detail);return detail;
    }
    private async Task ShowModerationUsersAsync(StackPanel content,string after="",string query="")
    {
        var data=await _moderation.RequestAsync("admin/users?after="+Uri.EscapeDataString(after)+"&search="+Uri.EscapeDataString(query),HttpMethod.Get);var detail=ModerationDetail(content);
        detail.Children.Add(Label("Registered accounts · 100 per page. Search covers all accounts. No emails or account tokens.",12,true));
        var search=new TextBox { Text=query, MaxLength=16, Margin=new Thickness(0,10,0,10), ToolTip="Search all accounts by username" };detail.Children.Add(search);
        detail.Children.Add(ActionButton("SEARCH ALL ACCOUNTS",()=>ShowModerationUsersAsync(content,"",search.Text.Trim())));
        var rows=new StackPanel();detail.Children.Add(rows);
        foreach(var user in data.GetProperty("users").EnumerateArray())
        {
            var uuid=user.GetProperty("uuid").GetString();var name=user.GetProperty("username").GetString()!;var disabled=user.GetProperty("disabled").GetBoolean();var role=user.GetProperty("role").GetString();
            var row=new StackPanel();row.Children.Add(Label(name+" · "+role+(disabled?" · DISABLED":""),16));
            var reason=new TextBox {Text=user.GetProperty("reason").GetString(),ToolTip="Restriction reason",MaxLength=500,Margin=new Thickness(0,8,0,8)};row.Children.Add(reason);
            var actions=new WrapPanel();actions.Children.Add(ActionButton(disabled?"RESTORE CLIENT ACCESS":"DISABLE CLIENT",async()=>
            {
                if(MessageBox.Show(this,$"{(disabled?"Restore":"Disable")} Plutonium client access for {name}?","Confirm account action",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
                await _moderation.RequestAsync("admin/restriction",HttpMethod.Post,new {uuid,disabled=!disabled,reason=reason.Text});await ShowModerationUsersAsync(content);
            }));
            if(_moderation.Role=="owner")actions.Children.Add(ActionButton(role=="admin"?"REMOVE ADMIN":"GRANT ADMIN",async()=>
            {
                if(MessageBox.Show(this,$"Change moderation permissions for {name}?","Confirm admin permissions",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
                await _moderation.RequestAsync("admin/role",HttpMethod.Post,new {uuid,role=role=="admin"?"user":"admin"});await ShowModerationUsersAsync(content);
            }));
            row.Children.Add(actions);var card=Card(row);rows.Children.Add(card);
            search.TextChanged+=(_,_)=>card.Visibility=name.Contains(search.Text,StringComparison.OrdinalIgnoreCase)?Visibility.Visible:Visibility.Collapsed;
        }
        if(data.TryGetProperty("nextCursor",out var cursor)&&cursor.ValueKind==JsonValueKind.String)detail.Children.Add(ActionButton("NEXT 100 ACCOUNTS",()=>ShowModerationUsersAsync(content,cursor.GetString()!,query)));
        if(after.Length>0)detail.Children.Add(ActionButton("FIRST PAGE",()=>ShowModerationUsersAsync(content,"",query)));
    }
    private async Task ShowAppealsAsync(StackPanel content)
    {
        var data=await _moderation.RequestAsync("admin/appeals",HttpMethod.Get);var detail=ModerationDetail(content);
        foreach(var appeal in data.GetProperty("appeals").EnumerateArray())
        {
            var row=new StackPanel();row.Children.Add(Label(appeal.GetProperty("username").GetString()+" · "+appeal.GetProperty("status").GetString(),16));row.Children.Add(Label(appeal.GetProperty("explanation").GetString()!,12,true));
            if(appeal.GetProperty("status").GetString()=="pending")
            {
                var id=appeal.GetProperty("id").ToString();var actions=new WrapPanel();
                foreach(var decision in new[]{"accepted","rejected"}) actions.Children.Add(ActionButton(decision=="accepted"?"ACCEPT & RESTORE":"REJECT",async()=>
                {
                    if(MessageBox.Show(this,$"Mark this appeal as {decision}?"+(decision=="accepted"?" This restores client access.":""),"Review appeal",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
                    await _moderation.RequestAsync("admin/review",HttpMethod.Post,new {id,decision});await ShowAppealsAsync(content);
                }));row.Children.Add(actions);
            }
            detail.Children.Add(Card(row));
        }
        if(data.GetProperty("appeals").GetArrayLength()==0)detail.Children.Add(Label("No appeals yet.",14,true));
    }
    private async Task ShowRemoteConfigAsync(StackPanel content)
    {
        var config=await _moderation.RequestAsync("admin/config",HttpMethod.Get);var detail=ModerationDetail(content);
        var maintenance=new CheckBox {Content="Maintenance mode",IsChecked=config.GetProperty("maintenance").GetBoolean(),Margin=new Thickness(0,15,0,10)};
        var message=new TextBox {Text=config.GetProperty("message").GetString(),MaxLength=240};detail.Children.Add(maintenance);detail.Children.Add(message);
        detail.Children.Add(Label("Globally disabled features",16));var checks=new List<CheckBox>();
        var disabled=config.GetProperty("disabledFeatures").EnumerateArray().Select(v=>v.GetString()).ToHashSet();
        foreach(var feature in config.GetProperty("availableFeatures").EnumerateArray()) {var id=feature.GetString()!;var box=new CheckBox {Content=id,Tag=id,IsChecked=disabled.Contains(id),Margin=new Thickness(0,4,0,4)};checks.Add(box);detail.Children.Add(box);}
        detail.Children.Add(ActionButton("SAVE GLOBAL SETTINGS",async()=>
        {
            await _moderation.RequestAsync("admin/config",HttpMethod.Put,new {maintenance=maintenance.IsChecked==true,message=message.Text,disabledFeatures=checks.Where(c=>c.IsChecked==true).Select(c=>(string)c.Tag).ToArray(),revision=config.GetProperty("revision").GetInt32()});
            await RefreshPolicyAsync();await ShowRemoteConfigAsync(content);
        }));
        detail.Children.Add(ActionButton("DISABLE MAINTENANCE",async()=>
        {
            var latest=await _moderation.RequestAsync("admin/config",HttpMethod.Get);
            await _moderation.RequestAsync("admin/config",HttpMethod.Put,new {maintenance=false,message=latest.GetProperty("message").GetString(),disabledFeatures=latest.GetProperty("disabledFeatures").EnumerateArray().Select(v=>v.GetString()).ToArray(),revision=latest.GetProperty("revision").GetInt32()});
            await RefreshPolicyAsync();await ShowRemoteConfigAsync(content);
        }));
    }
    private async Task ShowAuditAsync(StackPanel content)
    {
        var data=await _moderation.RequestAsync("admin/audit",HttpMethod.Get);var detail=ModerationDetail(content);
        foreach(var entry in data.GetProperty("events").EnumerateArray())detail.Children.Add(Card(Label($"{entry.GetProperty("created_at")} · {entry.GetProperty("action")}\nActor: {entry.GetProperty("actor")}\nTarget: {entry.GetProperty("target")}",12,true)));
    }
}
