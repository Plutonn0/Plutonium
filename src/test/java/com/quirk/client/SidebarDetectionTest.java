package com.quirk.client;
import org.junit.jupiter.api.Test;
import net.minecraft.world.scores.*;
import net.minecraft.world.scores.criteria.ObjectiveCriteria;
import net.minecraft.network.chat.Component;
import net.minecraft.network.chat.numbers.BlankFormat;
import net.minecraft.ChatFormatting;
import static org.junit.jupiter.api.Assertions.*;

class SidebarDetectionTest {
    @Test void detectsTeamSidebarPrefixesBlankScoresAndPreservesServerData(){
        net.minecraft.SharedConstants.tryDetectVersion();
        net.minecraft.server.Bootstrap.bootStrap();
        var board=new Scoreboard();
        var general=board.addObjective("general",ObjectiveCriteria.DUMMY,Component.literal("General"),ObjectiveCriteria.RenderType.INTEGER,false,null);
        board.setDisplayObjective(DisplaySlot.SIDEBAR,general);
        var special=board.addObjective("special",ObjectiveCriteria.DUMMY,Component.literal("Live server"),ObjectiveCriteria.RenderType.INTEGER,false,BlankFormat.INSTANCE);
        board.setDisplayObjective(DisplaySlot.TEAM_RED,special);
        var team=board.addPlayerTeam("red");team.setColor(ChatFormatting.RED);board.addPlayerToTeam("Tester",team);
        var rowTeam=board.addPlayerTeam("balance");rowTeam.setPlayerPrefix(Component.literal("Balance: "));rowTeam.setPlayerSuffix(Component.literal("$1,200"));board.addPlayerToTeam("",rowTeam);
        var score=board.getOrCreatePlayerScore(ScoreHolder.forNameOnly(""),special);score.set(15);
        board.getOrCreatePlayerScore(ScoreHolder.forNameOnly("#hidden"),special).set(20);
        var first=FakeStats.read(board,"Tester","example.org");assertEquals("Live server",first.title());assertEquals(1,first.rows().size());assertEquals("$1,200",first.rows().getFirst().value());
        String id=first.rows().getFirst().id();rowTeam.setPlayerSuffix(Component.literal("$1,300"));
        var second=FakeStats.read(board,"Tester","example.org");assertEquals(id,second.rows().getFirst().id());assertEquals("$1,300",second.rows().getFirst().value());
        var edits=new SidebarOverrides();edits.set(second.scope(),id,null,"$9,999");assertEquals(15,score.get());assertEquals("$1,300",rowTeam.getPlayerSuffix().getString());
        board.setDisplayObjective(DisplaySlot.TEAM_RED,null);assertEquals("General",FakeStats.read(board,"Tester","example.org").title());
        board.setDisplayObjective(DisplaySlot.SIDEBAR,null);assertNull(FakeStats.read(board,"Tester","example.org"));
    }
}
