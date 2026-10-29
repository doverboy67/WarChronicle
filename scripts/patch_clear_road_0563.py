from pathlib import Path
R=Path(__file__).resolve().parents[1]
for proj in ('WarChronicle.Web','WarChronicle.Pages'):
 p=R/proj/'Services/GameSession.cs'
 s=p.read_text()
 old='''        if (clearRoadActive)
        {
            _specialPrompt = "HighOverlook";
            FlowPrompt = new(
                FlowPromptKind.Choice,
                "High Overlook",
                "Clear Road can cancel this triggered Explore event before any part of it is resolved.",
                [
                    new("high-overlook:resolve", "Resolve the Explore event", "Keep Clear Road for a future Explore event"),
                    new("high-overlook:ignore", "Discard Clear Road and ignore this event")
                ]);
'''
 new='''        if (clearRoadActive)
        {
            // Show the actual printed effect BEFORE the once-use Clear Road decision.
            // This reads from the triggered card, not from a card-specific fallback;
            // importantly it does not execute the event or make any rolls.
            var exploreEffectPreview = System.Net.WebUtility.HtmlDecode(
                Regex.Replace(
                    Regex.Replace(card.Effect ?? string.Empty, @"(?i)<br\\s*/?>|</li>|</p>", ". "),
                    @"<[^>]*>", " "));
            exploreEffectPreview = Regex.Replace(exploreEffectPreview, @"\\s+", " ").Trim();
            _specialPrompt = "HighOverlook";
            FlowPrompt = new(
                FlowPromptKind.Choice,
                "High Overlook — " + card.Title,
                $"Before choosing, review {card.Title}. If resolved: {exploreEffectPreview} Clear Road can cancel this Explore event before any part of it is resolved.",
                [
                    new("high-overlook:resolve", "Resolve the Explore event", "Resolve the printed event and keep Clear Road for a future Explore event."),
                    new("high-overlook:ignore", "Discard Clear Road and ignore this event", "Spend Clear Road now: ignore the entire Explore event without rolling or applying its effects.")
                ]);
'''
 assert old in s,proj
 s=s.replace(old,new)
 p.write_text(s)
 print('Patched',p)
