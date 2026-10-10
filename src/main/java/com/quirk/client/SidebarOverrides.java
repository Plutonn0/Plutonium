package com.quirk.client;

import com.google.gson.*;
import java.util.*;
import java.util.regex.Pattern;

/** Local display overrides only. Never writes to the Minecraft scoreboard or sends commands. */
public final class SidebarOverrides {
    public record Fields(String label,String value) {}
    private final LinkedHashMap<String,LinkedHashMap<String,Fields>> profiles=new LinkedHashMap<>();
    private Runnable changed=()->{};
    private static final Pattern END_VALUE=Pattern.compile("^(.*?)([-+]?[$€£]?\\d[\\d,.]*[kKmMbBtT%]?)\\s*$");
    public static Fields split(String text,String score){
        String plain=text.replaceAll("§[0-9A-FK-ORa-fk-or]","");
        var match=END_VALUE.matcher(plain);
        if(match.matches()&&!match.group(1).isBlank()&&!Character.isLetterOrDigit(match.group(1).charAt(match.group(1).length()-1)))return new Fields(match.group(1).stripTrailing(),match.group(2));
        return new Fields(plain,score);
    }
    public void onChange(Runnable callback){changed=callback;}
    public Fields get(String scope,String id){return profiles.getOrDefault(scope,new LinkedHashMap<>()).getOrDefault(id,new Fields(null,null));}
    public Fields apply(String scope,String id,Fields live){var override=get(scope,id);return new Fields(override.label==null?live.label:override.label,override.value==null?live.value:override.value);}
    public void set(String scope,String id,String label,String value){
        if(scope.length()>512||id.length()>512)return;
        var profile=profiles.computeIfAbsent(scope,k->new LinkedHashMap<>());
        if(label==null&&value==null)profile.remove(id);else profile.put(id,new Fields(clean(label),clean(value)));
        if(profile.size()>64)profile.pollFirstEntry();if(profiles.size()>32)profiles.pollFirstEntry();changed.run();
    }
    private static String clean(String text){if(text==null)return null;String clean=text.replaceAll("[\\p{Cntrl}]","");return clean.substring(0,clean.offsetByCodePoints(0,Math.min(160,clean.codePointCount(0,clean.length()))));}
    public void reset(String scope){if(profiles.remove(scope)!=null)changed.run();}
    public JsonObject save(){var root=new JsonObject();for(var p:profiles.entrySet()){var rows=new JsonObject();for(var row:p.getValue().entrySet()){var f=new JsonObject();if(row.getValue().label!=null)f.addProperty("label",row.getValue().label);if(row.getValue().value!=null)f.addProperty("value",row.getValue().value);rows.add(row.getKey(),f);}root.add(p.getKey(),rows);}return root;}
    public void load(JsonObject root){for(var p:root.entrySet().stream().limit(32).toList())try{for(var row:p.getValue().getAsJsonObject().entrySet().stream().limit(64).toList())try{var f=row.getValue().getAsJsonObject();set(p.getKey(),row.getKey(),f.has("label")?f.get("label").getAsString():null,f.has("value")?f.get("value").getAsString():null);}catch(RuntimeException ignored){}}catch(RuntimeException ignored){}}
}
