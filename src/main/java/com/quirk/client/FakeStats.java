package com.quirk.client;

import net.minecraft.client.Minecraft;
import net.minecraft.world.scores.*;
import net.minecraft.network.chat.numbers.StyledFormat;
import java.util.*;

public final class FakeStats {
    public record Row(String id,String label,String value) {}
    public record Snapshot(String scope,String title,List<Row> rows) {}
    private static Snapshot snapshot;
    public static Snapshot snapshot(){return snapshot;}
    public static Snapshot read(Scoreboard board,String player,String server){
        var team=board.getPlayersTeam(player);Objective objective=null;
        if(team!=null){var slot=DisplaySlot.teamColorToSlot(team.getColor());if(slot!=null)objective=board.getDisplayObjective(slot);}
        if(objective==null)objective=board.getDisplayObjective(DisplaySlot.SIDEBAR);
        if(objective==null)return null;
        var format=objective.numberFormatOrDefault(StyledFormat.SIDEBAR_DEFAULT);
        var entries=board.listPlayerScores(objective).stream().filter(e->!e.isHidden())
            .sorted(Comparator.comparingInt(PlayerScoreEntry::value).reversed().thenComparing(PlayerScoreEntry::owner,String.CASE_INSENSITIVE_ORDER)).limit(15).toList();
        List<Row> rows=new ArrayList<>();Map<String,Integer> duplicates=new HashMap<>();
        for(var entry:entries){
            var rowTeam=board.getPlayersTeam(entry.owner());
            String text=PlayerTeam.formatNameForTeam(rowTeam,entry.ownerName()).getString();
            var parts=SidebarOverrides.split(text,entry.formatValue(format).getString());
            String identity=rowTeam!=null?"team:"+rowTeam.getName()+":row:"+parts.label():"row:"+parts.label();
            int index=duplicates.merge(identity,1,Integer::sum);
            rows.add(new Row(identity+"#"+index,parts.label(),parts.value()));
        }
        return new Snapshot(server+"/"+objective.getName(),objective.getDisplayName().getString(),List.copyOf(rows));
    }
    public static void tick(){
        var mc=Minecraft.getInstance();
        if(mc.level==null||mc.player==null){snapshot=null;return;}
        String server=mc.getCurrentServer()!=null?mc.getCurrentServer().ip:mc.getSingleplayerServer()!=null?"local:"+mc.getSingleplayerServer().getWorldData().getLevelName():"local";
        snapshot=read(mc.level.getScoreboard(),mc.player.getScoreboardName(),server);
    }
    public static boolean replacesSidebar(){return Quirk.settings().module("fakestats").on()&&(!Quirk.settings().module("fakestats").flag("live")||snapshot!=null);}
    public static Snapshot display(boolean preview){
        var module=Quirk.settings().module("fakestats");
        if(!module.flag("live")){
            List<Row> rows=new ArrayList<>();for(String line:module.get("lines").choice().split("\\|",-1)){if(rows.size()==15)break;rows.add(new Row("manual:"+rows.size(),line,""));}
            return new Snapshot("manual",module.get("title").choice(),rows);
        }
        if(snapshot==null)return preview?new Snapshot("preview","Scoreboard preview",List.of(new Row("a","Balance","$1,250"),new Row("b","Kills","24"),new Row("c","Deaths","3"))):null;
        var overrides=Quirk.settings().sidebar;
        List<Row> rows=new ArrayList<>();for(var row:snapshot.rows){var fields=overrides.apply(snapshot.scope,row.id,new SidebarOverrides.Fields(row.label,row.value));rows.add(new Row(row.id,fields.label(),fields.value()));}
        String title=overrides.apply(snapshot.scope,"$title",new SidebarOverrides.Fields(snapshot.title,"")).label();
        return new Snapshot(snapshot.scope,title,rows);
    }
}
