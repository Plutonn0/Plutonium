package com.quirk.client;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;
class CommandInterceptionTest {
    @Test void paymentMatchesWholeCommandTokenIncludingNamespaces() {
        for(String value:new String[]{"pay Steve 10"," /pay Steve 10 ","PAY Steve 10","essentials:pay Steve 10","minecraft:pay"})assertTrue(Quirk.isPayCommand(value),value);
        for(String value:new String[]{"say pay Steve 10","payment Steve 10","repay Steve 10","","   ","msg Steve /pay"})assertFalse(Quirk.isPayCommand(value),value);
    }
}
