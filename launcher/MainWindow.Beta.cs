using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PlutoniumLauncher;

public partial class MainWindow
{
    internal void ShowBetaPrompt()
    {
        var dialog=CreateBetaPrompt();dialog.Owner=this;dialog.ShowDialog();
    }
    internal Window CreateBetaPrompt()
    {
        var dialog = new Window { Title="Plutonium · Closed beta", Width=520,
            SizeToContent=SizeToContent.Height, ResizeMode=ResizeMode.NoResize, WindowStyle=WindowStyle.None,
            WindowStartupLocation=WindowStartupLocation.CenterOwner, Background=new SolidColorBrush(Color.FromRgb(17,17,19)),
            Foreground=Brushes.WhiteSmoke, ShowInTaskbar=false };
        var body=new StackPanel { Margin=new Thickness(30) }; dialog.Content=body;
        body.Children.Add(Label("EARLY ACCESS",11,true)); body.Children.Add(Label("Closed beta",28));
        body.Children.Add(Label("Use the invitation shared by Plutonium’s owner. Access stays with your Minecraft account until revoked.",14,true));
        var key=ModerationSettingsEditor.Field("",80); key.ToolTip="Daily beta invitation key";
        System.Windows.Automation.AutomationProperties.SetName(key,"Beta invitation key");body.Children.Add(key);
        var status=Label("Sign in with the Minecraft account you want to enroll.",12,true); body.Children.Add(status);
        var redeem=new Button { Content="UNLOCK BETA", Style=(Style)FindResource("QuietButton"), Padding=new Thickness(18,12,18,12), IsDefault=true };
        body.Children.Add(redeem);
        var redeeming=false;
        dialog.Closing+=(_,args)=>{ if(redeeming) {args.Cancel=true;status.Text="Please wait for verification to finish.";} };
        redeem.Click+=async(_,_)=>{
            if(string.IsNullOrWhiteSpace(key.Text)) {status.Text="Enter the invitation key first.";key.Focus();return;}
            redeeming=true;redeem.IsEnabled=false;key.IsEnabled=false;status.Text="Verifying your account and invitation…";
            try {
                await ConnectModerationAsync();
                await _moderation.RequestAsync("beta/redeem",HttpMethod.Post,new {key=key.Text.Trim()},_moderationLifetime.Token);
                key.Clear();await _moderation.RefreshIdentityAsync();
                status.Text="Beta access unlocked for this account. Available beta features will now open automatically. No private build downloads are configured yet.";
                redeem.Content="BETA UNLOCKED";
            } catch(Exception error) {status.Text=ErrorReport.Redact(error.Message);redeem.IsEnabled=true;key.IsEnabled=true;}
            finally {redeeming=false;}
        };
        var close=new Button { Content="CLOSE", Style=(Style)FindResource("QuietButton"), Margin=new Thickness(0,10,0,0), Padding=new Thickness(18,10,18,10), IsCancel=true };
        close.Click+=(_,_)=>dialog.Close();body.Children.Add(close);dialog.Loaded+=(_,_)=>key.Focus();return dialog;
    }

    private async Task ShowBetaAdminAsync(StackPanel content)
    {
        var data=await _moderation.RequestAsync("admin/beta",HttpMethod.Get);
        var detail=ModerationDetail(content);
        detail.Children.Add(Label("Closed beta",26));
        detail.Children.Add(Label("Invitation changes at midnight UTC. Membership lasts until revoked. Only the owner can view keys and manage beta access.",13,true));
        var key=ModerationSettingsEditor.Field(data.GetProperty("key").GetString()!,80);key.IsReadOnly=true;key.FontSize=18;detail.Children.Add(key);
        var expiry=Label("",12,true);detail.Children.Add(expiry);
        void SetExpiry(System.Text.Json.JsonElement value)=>expiry.Text="Next key: "+value.GetProperty("expiresAt").GetDateTimeOffset().ToLocalTime().ToString("g")+" (your time)";
        SetExpiry(data);
        _refreshBetaKey=async()=>{
            if(!key.IsVisible)return;
            var latest=await _moderation.RequestAsync("admin/beta",HttpMethod.Get);key.Text=latest.GetProperty("key").GetString();SetExpiry(latest);
        };
        detail.Children.Add(ActionButton("COPY TODAY’S KEY",async()=>{await _refreshBetaKey!();Clipboard.SetText(key.Text);}));
        detail.Children.Add(Label("Beta-only features",18));
        detail.Children.Add(Label("Select features to limit them to beta members. Leave everything off to keep the current public release unchanged. Globally disabled features and account restrictions still apply.",12,true));
        var selected=data.GetProperty("features").EnumerateArray().Select(v=>v.GetString()).ToHashSet();
        var list=new System.Windows.Controls.Primitives.UniformGrid { Columns=3 };
        var choices=new Dictionary<string,CheckBox>();
        foreach(var feature in data.GetProperty("availableFeatures").EnumerateArray()){
            var id=feature.GetString()!;
            var toggle=new CheckBox { Content=ModerationSettingsEditor.FeatureName(id), IsChecked=selected.Contains(id),
                Style=(Style)FindResource("AdminSwitch"), Margin=new Thickness(0,8,14,8) };
            choices[id]=toggle;list.Children.Add(toggle);
        }
        detail.Children.Add(ActionButton("SAVE BETA FEATURES",async()=>{
            await _moderation.RequestAsync("admin/beta/features",HttpMethod.Put,new { features=choices.Where(v=>v.Value.IsChecked==true).Select(v=>v.Key).ToArray(), revision=data.GetProperty("revision").GetInt32() });
            await RefreshPolicyAsync();await ShowBetaAdminAsync(content);
        }));
        detail.Children.Add(new ScrollViewer { Content=list,MaxHeight=240,VerticalScrollBarVisibility=ScrollBarVisibility.Auto });
        detail.Children.Add(Label("Members · latest 500",18));
        if(data.GetProperty("members").GetArrayLength()==0)detail.Children.Add(Label("No beta members yet. Share today’s key to invite testers.",13,true));
        foreach(var member in data.GetProperty("members").EnumerateArray()){
            var uuid=member.GetProperty("uuid").GetString();var name=member.GetProperty("username").GetString();var revoked=member.GetProperty("revoked").GetBoolean();
            var row=new StackPanel();row.Children.Add(Label(name+(revoked?" · Revoked":" · Beta member"),15));
            row.Children.Add(ActionButton(revoked?"RESTORE BETA ACCESS":"REVOKE BETA ACCESS",async()=>{
                if(MessageBox.Show(this,(revoked?"Restore":"Revoke")+" beta access for "+name+"?","Beta membership",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
                await _moderation.RequestAsync("admin/beta/member",HttpMethod.Post,new {uuid,revoked=!revoked});await ShowBetaAdminAsync(content);
            }));detail.Children.Add(Card(row));
        }
    }
}
