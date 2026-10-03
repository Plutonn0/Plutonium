package com.quirk.client;
import org.junit.jupiter.api.Test;
import java.net.URI;
import static org.junit.jupiter.api.Assertions.*;
class EntertainmentTest {
    @Test void youtubePlaybackOnlyOpensOfficialHttpsPages(){
        assertEquals("www.youtube.com",Entertainment.youtubeUri("https://www.youtube.com/playlist?list=test").getHost());
        assertThrows(IllegalArgumentException.class,()->Entertainment.youtubeUri("https://youtube.com.attacker.example/"));
        assertThrows(IllegalArgumentException.class,()->Entertainment.youtubeUri("http://youtube.com/"));
    }
    @Test void acceptsDirectHttpOggStreamUris() {
        assertEquals(URI.create("https://radio.example/live.ogg"),Entertainment.radioStreamUri(" https://radio.example/live.ogg "));
        assertEquals(URI.create("http://radio.example:8000/stream"),Entertainment.radioStreamUri("http://radio.example:8000/stream"));
    }
    @Test void rejectsUnsupportedRadioUriSchemes() {
        assertThrows(IllegalArgumentException.class,()->Entertainment.radioStreamUri("file:///music.ogg"));
        assertThrows(IllegalArgumentException.class,()->Entertainment.radioStreamUri("not a URL"));
    }
}
