using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace PlutoniumLauncher;

internal sealed class ModerationSettingsEditor : Grid
{
    internal TextBox Reason { get; }
    internal TextBox Search { get; }
    internal CheckBox Maintenance { get; }
    internal Button Save { get; }
    internal Dictionary<string,CheckBox> Features { get; } = [];
    private readonly TextBlock _status;
    private readonly UniformGrid _items = new() { Columns=2 };
    private readonly Dictionary<string,Border> _cards = [];
    private string _category="All";
    internal bool Dirty { get; private set; }
    private static TextBlock Text(string value,double size=13,bool muted=false)=>new(){Text=value,FontSize=size,Foreground=muted?Brushes.DarkGray:Brushes.WhiteSmoke,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};
    internal static TextBox Field(string value,int limit,bool multiline=false)=>new(){Text=value,MaxLength=limit,Style=(Style)Application.Current.FindResource("AdminTextBox"),AcceptsReturn=multiline,TextWrapping=multiline?TextWrapping.Wrap:TextWrapping.NoWrap,MinHeight=multiline?90:46,VerticalScrollBarVisibility=multiline?ScrollBarVisibility.Auto:ScrollBarVisibility.Hidden,Margin=new Thickness(0,0,0,12)};
    private static string Category(string id)=>id switch {
        "mods" or "discord" or "radio"=>"Launcher",
        "players" or "storage" or "spawners" or "tracers" or "xray" or "fullbright" or "suschunk" or "mobs" or "nametags" or "freelook" or "trajectory"=>"Render",
        "freecam" or "fly" or "elytraglide" or "inventorymove" or "autoclutch" or "sprint" or "fastplace"=>"Movement",
        "aimassist" or "automace" or "autototem" or "doubleanchor" or "nohitdelay" or "autoclicker"=>"Combat",
        "weather" or "notifications" or "coordinates" or "active"=>"HUD",
        _=>"Misc"
    };
    private static string FeatureName(string id)=>id switch {
        "players"=>"Player ESP", "storage"=>"Storage ESP", "spawners"=>"Spawner ESP", "suschunk"=>"Suspicious chunks", "mobs"=>"Mob ESP", "nametags"=>"Name tags", "trajectory"=>"Pearl trajectory", "xray"=>"X-ray",
        "elytraglide"=>"Elytra glide", "inventorymove"=>"Inventory move", "autoclutch"=>"Auto clutch", "fastplace"=>"Fast place", "aimassist"=>"Aim assist", "automace"=>"Auto mace", "autototem"=>"Auto totem", "doubleanchor"=>"Double anchor", "nohitdelay"=>"No hit delay", "autoclicker"=>"Auto clicker", "autoeat"=>"Auto eat", "autofirework"=>"Auto firework", "quickexp"=>"Quick XP", "invtotem"=>"Inventory totem", "fakepay"=>"Fake pay", "netherite"=>"Netherite finder", "fakestats"=>"Fake stats", "weather"=>"Weather notifier", "active"=>"Active modules HUD", "discord"=>"Discord presence", "mods"=>"Mod browser",
        _=>char.ToUpperInvariant(id[0])+id[1..]
    };
    internal ModerationSettingsEditor(JsonElement config,Func<object,Task> save,Func<Task> back)
    {
        RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});RowDefinitions.Add(new RowDefinition());RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var header=new StackPanel();Children.Add(header);
        header.Children.Add(Text("GLOBAL SETTINGS",11,true));header.Children.Add(Text("Availability & features",28));
        var availability=new Grid();availability.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(200)});availability.ColumnDefinitions.Add(new ColumnDefinition());
        Maintenance=new CheckBox{Content="Maintenance mode",IsChecked=config.GetProperty("maintenance").GetBoolean(),Style=(Style)Application.Current.FindResource("AdminSwitch"),VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,12,12,0)};availability.Children.Add(Maintenance);
        var reasonBox=new StackPanel();reasonBox.Children.Add(Text("Message shown to everyone",12,true));Reason=Field(config.GetProperty("message").GetString()??"",240,true);reasonBox.Children.Add(Reason);Grid.SetColumn(reasonBox,1);availability.Children.Add(reasonBox);header.Children.Add(availability);
        header.Children.Add(Text("Feature access",18));header.Children.Add(Text("Enabled means available to users. Changes apply when you save.",12,true));
        Search=Field("",60);Search.ToolTip="Search features";System.Windows.Automation.AutomationProperties.SetName(Search,"Search features");header.Children.Add(Search);
        var tabs=new WrapPanel();header.Children.Add(tabs);
        foreach(var category in new[]{"All","Render","Movement","Combat","HUD","Misc","Launcher"}){
            var tab=new Button{Content=category,Style=(Style)Application.Current.FindResource("QuietButton"),Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,6,10)};
            tab.Click+=(_,_)=>{_category=category;Filter();foreach(Button button in tabs.Children)button.BorderBrush=Equals(button.Content,category)?Brushes.White:Brushes.DimGray;};tabs.Children.Add(tab);
        }
        var scroll=new ScrollViewer{Content=_items,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,8,0)};Grid.SetRow(scroll,1);Children.Add(scroll);
        _items.SizeChanged+=(_,_)=>{var cols=_items.ActualWidth<570?1:2;if(_items.Columns!=cols)_items.Columns=cols;};
        var disabled=config.GetProperty("disabledFeatures").EnumerateArray().Select(v=>v.GetString()).ToHashSet();
        foreach(var feature in config.GetProperty("availableFeatures").EnumerateArray()){
            var id=feature.GetString()!;var body=new DockPanel();var toggle=new CheckBox{IsChecked=!disabled.Contains(id),Style=(Style)Application.Current.FindResource("AdminSwitch"),Content=disabled.Contains(id)?"Off":"On",VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(toggle,Dock.Right);body.Children.Add(toggle);
            var labels=new StackPanel{Margin=new Thickness(0,0,12,0)};labels.Children.Add(Text(FeatureName(id),15));labels.Children.Add(Text(Category(id),11,true));body.Children.Add(labels);
            var card=new Border{Child=body,Padding=new Thickness(16,12,12,8),Margin=new Thickness(0,0,10,10),Background=new SolidColorBrush(Color.FromRgb(22,23,28)),BorderBrush=new SolidColorBrush(Color.FromRgb(47,49,57)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8)};
            Features[id]=toggle;_cards[id]=card;_items.Children.Add(card);System.Windows.Automation.AutomationProperties.SetName(toggle,FeatureName(id));
            toggle.Checked+=(_,_)=>{toggle.Content="On";Changed();};toggle.Unchecked+=(_,_)=>{toggle.Content="Off";Changed();};
        }
        var footer=new DockPanel{Margin=new Thickness(0,14,0,0),LastChildFill=true};Grid.SetRow(footer,2);Children.Add(footer);
        Save=new Button{Content="Save changes",Style=(Style)Application.Current.FindResource("QuietButton"),Padding=new Thickness(22,14,22,14),IsEnabled=false,MinWidth=160};DockPanel.SetDock(Save,Dock.Right);footer.Children.Add(Save);
        var backButton=new Button{Content="Back to overview",Style=(Style)Application.Current.FindResource("QuietButton"),Padding=new Thickness(16,14,16,14),Margin=new Thickness(12,0,12,0)};DockPanel.SetDock(backButton,Dock.Right);footer.Children.Add(backButton);
        _status=Text("All changes saved",12,true);_status.VerticalAlignment=VerticalAlignment.Center;footer.Children.Add(_status);
        backButton.Click+=async(_,_)=>{if(Dirty&&MessageBox.Show("Discard unsaved changes?","Global settings",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;await back();};
        Save.Click+=async(_,_)=>{
            Save.IsEnabled=false;Save.Content="Saving…";IsHitTestVisible=false;
            try{await save(new {maintenance=Maintenance.IsChecked==true,message=Reason.Text,disabledFeatures=Features.Where(k=>k.Value.IsChecked!=true).Select(k=>k.Key).ToArray(),revision=config.GetProperty("revision").GetInt32()});Dirty=false;_status.Text="Saved successfully";}
            catch(Exception error){_status.Text=error.Message+" Your edits are still here.";Save.IsEnabled=true;}
            finally{IsHitTestVisible=true;Save.Content="Save changes";}
        };
        Search.TextChanged+=(_,_)=>Filter();Maintenance.Checked+=(_,_)=>Changed();Maintenance.Unchecked+=(_,_)=>Changed();Reason.TextChanged+=(_,_)=>Changed();
    }
    private void Changed(){Dirty=true;Save.IsEnabled=true;_status.Text="Unsaved changes";}
    private void Filter(){foreach(var pair in _cards)pair.Value.Visibility=(_category=="All"||Category(pair.Key)==_category)&&(FeatureName(pair.Key).Contains(Search.Text.Trim(),StringComparison.OrdinalIgnoreCase)||pair.Key.Contains(Search.Text.Trim(),StringComparison.OrdinalIgnoreCase))?Visibility.Visible:Visibility.Collapsed;}
}
