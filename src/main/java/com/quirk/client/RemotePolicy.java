package com.quirk.client;

import com.google.gson.*;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;
import com.quirk.client.ui.Paint;
import java.net.URI;
import java.net.http.*;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.Set;
import java.util.HashSet;
import java.util.concurrent.CompletableFuture;

/** Server authority for official builds. No administrator key or email allow-list ships in the game. */
public final class RemotePolicy {
    private static final String endpoint=loadEndpoint();
    private static final HttpClient http=HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(8)).followRedirects(HttpClient.Redirect.NEVER).build();
    private static volatile Set<String> disabled=Set.of();
    private static volatile boolean allowed;
    private static volatile boolean maintenance;
    private static volatile String maintenanceReason="";
    private static volatile String role="user";
    private static volatile boolean ownerCandidate;
    private static volatile String reason="Verifying your Plutonium client access…";
    private static volatile long validUntil;
    private static CompletableFuture<Void> pending;
    private static long nextPoll;
    private static String session;
    private static long sessionUntil;
    private static String loadEndpoint(){
        try(var stream=RemotePolicy.class.getResourceAsStream("/plutonium-service.json")){
            if(stream==null)throw new IllegalStateException("Missing bundled service configuration");
            String value=JsonParser.parseString(new String(stream.readAllBytes(),StandardCharsets.UTF_8)).getAsJsonObject().get("baseUrl").getAsString();
            if(value.isEmpty())return "";
            URI uri=URI.create(value);
            if(!"https".equals(uri.getScheme())||uri.getHost()==null||uri.getUserInfo()!=null||uri.getQuery()!=null||uri.getFragment()!=null)throw new IllegalStateException("Invalid service endpoint");
            return value.replaceAll("/+$","")+"/";
        }catch(Exception error){throw new IllegalStateException("Unable to read service configuration",error);}
    }
    public static boolean permits(String module){return endpoint.isEmpty()||(allowed&&!maintenance&&System.nanoTime()<validUntil&&!disabled.contains(module));}
    public static void tick(){
        if(endpoint.isEmpty())return;
        Minecraft mc=Minecraft.getInstance();long now=System.nanoTime();
        if(now>=nextPoll&&(pending==null||pending.isDone())){
            nextPoll=now+3_000_000_000L;
            String minecraftToken=mc.getUser().getAccessToken();
            pending=CompletableFuture.runAsync(()->refresh(minecraftToken));
        }
        boolean blocked=maintenance||!allowed||now>=validUntil;
        if(blocked&&!(mc.screen instanceof AccessScreen))mc.setScreen(new AccessScreen(mc.screen));
        else if(!blocked&&mc.screen instanceof AccessScreen screen)mc.setScreen(screen.previous);
        else if(mc.screen instanceof AccessScreen screen && !screen.state.equals(screenState()))mc.setScreen(new AccessScreen(screen.previous));
    }
    private static JsonObject post(String path,JsonObject body,boolean authenticated)throws Exception{
        var request=HttpRequest.newBuilder(URI.create(endpoint+path)).timeout(Duration.ofSeconds(15)).header("Content-Type","application/json");
        if(authenticated)request.header("Authorization","Bearer "+session);
        var response=http.send(request.POST(HttpRequest.BodyPublishers.ofString(body.toString())).build(),HttpResponse.BodyHandlers.ofString());
        if(response.statusCode()==401)session=null;
        if(response.statusCode()!=200)throw new IllegalStateException("Policy request failed");
        return JsonParser.parseString(response.body()).getAsJsonObject();
    }
    private static void refresh(String minecraftToken){
        try{
            if(session==null||System.nanoTime()>=sessionUntil){
                var body=new JsonObject();body.addProperty("minecraftToken",minecraftToken);var auth=post("session",body,false);
                session=auth.get("token").getAsString();sessionUntil=System.nanoTime()+1_600_000_000_000L;
            }
            var body=new JsonObject();body.addProperty("kind","client");body.addProperty("version","2.0.61");var policy=post("heartbeat",body,true);
            var blocked=new HashSet<String>();for(var value:policy.getAsJsonArray("disabledFeatures"))blocked.add(value.getAsString());
            disabled=Set.copyOf(blocked);allowed=policy.get("allowed").getAsBoolean();
            maintenance=policy.get("maintenance").getAsBoolean();maintenanceReason=policy.get("message").getAsString();
            role=policy.has("role")?policy.get("role").getAsString():"user";
            ownerCandidate=policy.has("ownerCandidate")&&policy.get("ownerCandidate").getAsBoolean();
            reason=allowed?"":policy.get("reason").getAsString();
            validUntil=System.nanoTime()+Math.min(120,Math.max(1,policy.get("leaseSeconds").getAsInt()))*1_000_000_000L;
        }catch(Exception error){if(System.nanoTime()>=validUntil){allowed=false;reason="Unable to verify client access. Check your connection; retrying automatically.";}}
    }
    private static String screenState(){return maintenance+":"+role+":"+ownerCandidate;}
    private static final class AccessScreen extends Screen {
        private final String state=screenState();
        private boolean adminMenu;
        private String actionStatus="";
        private final Screen previous;
        AccessScreen(Screen previous){super(Component.literal("Plutonium client access"));this.previous=previous;}
        @Override public boolean shouldCloseOnEsc(){return false;}
        @Override public boolean isPauseScreen(){return false;}
        @Override protected void init(){
            if(maintenance && (role.equals("admin")||role.equals("owner")||ownerCandidate)) {
                addRenderableWidget(Button.builder(Component.literal("Open admin menu"),b->{adminMenu=true;b.active=false;}).bounds(width/2-105,height/2+48,210,20).build());
                if(role.equals("admin")||role.equals("owner")) {
                    var stop=addRenderableWidget(Button.builder(Component.literal("Stop maintenance"),b->{
                        b.active=false;actionStatus="Stopping maintenance…";
                        CompletableFuture.runAsync(()->{
                            try {post("admin/maintenance/stop",new JsonObject(),true);Minecraft.getInstance().execute(()->{actionStatus="Updating status…";nextPoll=0;});}
                            catch(Exception error){Minecraft.getInstance().execute(()->{actionStatus="Unable to stop maintenance. Check your permissions or try the launcher.";b.active=true;});}
                        });
                    }).bounds(width/2-105,height/2+72,210,20).build());
                    stop.visible=false;
                }
            } else if(!maintenance) addRenderableWidget(Button.builder(Component.literal("Appeal"),b->net.minecraft.util.Util.getPlatform().openUri("https://plutoniumclient.vercel.app/appeal")).bounds(width/2-105,height/2+48,210,20).build());
            addRenderableWidget(Button.builder(Component.literal("Quit game"),b->Minecraft.getInstance().stop()).bounds(width/2-105,height/2+100,210,20).build());
        }
        @Override public void render(GuiGraphics g,int x,int y,float delta){
            g.fill(0,0,width,height,0xf5111111);
            Paint.hudText(g,"PLUTONIUM",width/2f-Paint.width("PLUTONIUM")/2f,height/2f-70,20,Paint.TEXT);
            String heading=maintenance?"MAINTENANCE MODE":"Client access unavailable";
            for(var child:children())if(child instanceof Button button && button.getMessage().getString().equals("Stop maintenance"))button.visible=adminMenu;
            Paint.hudText(g,heading,width/2f-Paint.width(heading)/2f,height/2f-40,20,Paint.TEXT);
            var lines=Minecraft.getInstance().font.split(Paint.label(adminMenu && ownerCandidate && !role.equals("admin") && !role.equals("owner") ? "Open your launcher admin menu and verify the owner account to stop maintenance." : !actionStatus.isEmpty()?actionStatus:maintenance?maintenanceReason:reason),Math.max(200,(width-80)*2));int top=height/2-12;
            for(var line:lines){g.pose().pushMatrix();g.pose().translate(width/2f-Minecraft.getInstance().font.width(line)/4f,top);g.pose().scale(.5f,.5f);g.drawString(Minecraft.getInstance().font,line,0,0,Paint.MUTED,false);g.pose().popMatrix();top+=12;}
            super.render(g,x,y,delta);
        }
    }
}
