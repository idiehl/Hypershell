namespace HyperLib.Core;

public static class SeedLibrary
{
    public static LibraryState Create()
    {
        var state = new LibraryState
        {
            Nodes =
            [
                Node("natural-world", "collection", "hyperlib", "Natural World", 0, "#65a88e"),
                Node("animal-atlas", "work", "natural-world", "Animal Atlas", 0, "#376954", "#f5f3ed"),
                Node("woodland-habitat", "page", "animal-atlas", "A Woodland Habitat", 0),
                Node("science-lab", "collection", "hyperlib", "Science Lab", 1, "#7fa7ee"),
                Node("foundations", "series", "science-lab", "Foundations", 0),
                Node("wave-lab", "work", "foundations", "Wave Lab", 0, "#7fa7ee", "#101a21"),
                Node("fundamentals", "volume", "wave-lab", "Fundamentals", 0),
                Node("waves", "part", "fundamentals", "Waves", 0),
                Node("sound", "chapter", "waves", "Sound", 0),
                Node("exploring-wave", "page", "sound", "Exploring a Wave", 0)
            ],
            Pages =
            {
                ["woodland-habitat"] = new()
                {
                    Id = "woodland-habitat", WorkId = "animal-atlas", Title = "A Woodland Habitat",
                    Blocks =
                    [
                        Block("woodland-intro", "text", ("text", "A woodland is a living neighborhood. Trees form a canopy, shrubs provide shelter, and the forest floor supports fungi, insects, and new growth. Use this page as a field notebook: add observations, photographs, and questions as you explore.")),
                        Block("woodland-image", "image", ("src", "/media/woodland.svg"), ("alt", "An illustrated woodland with layered trees and a winding path"), ("caption", "Look for the layers: canopy, understory, and forest floor.")),
                        Block("woodland-note", "callout", ("title", "Try a five-minute survey"), ("text", "Choose one small patch. Record three textures, two sounds, and one sign of animal life. Return later and notice what has changed.")),
                        Block("woodland-counter", "counter", ("label", "Species observed"), ("start", 0)),
                        Block("woodland-richtext", "richtext", ("text", "## A closer look\n\nRecord the **canopy**, the *understory*, and the forest floor as separate habitats.\n\n- Sketch one leaf shape.\n- Look for tracks without disturbing wildlife.\n- Add a question for your next visit.")),
                        Block("woodland-gallery", "gallery", ("src1", "/media/woodland.svg"), ("src2", "/media/forest-detail.svg"), ("caption", "Different views of the same living landscape."))
                    ]
                },
                ["exploring-wave"] = new()
                {
                    Id = "exploring-wave", WorkId = "wave-lab", Title = "Exploring a Wave",
                    Blocks =
                    [
                        Block("wave-intro", "text", ("text", "A wave describes a repeating disturbance. Amplitude controls the size of the variation; frequency describes how often it repeats. This simplified experiment lets you change both and inspect the shape. It is a visual model, not an audio synthesizer.")),
                        Block("wave-experiment", "wave", ("frequency", 2.0), ("amplitude", 0.7)),
                        Block("wave-note", "callout", ("title", "Change one thing at a time"), ("text", "Keep amplitude fixed and increase frequency. Then restore frequency and change amplitude. Describe the difference in your own words.")),
                        Block("wave-audio", "audio", ("src", "/media/sample-tone.wav"), ("caption", "A short reference tone. Use the player to listen when you are ready.")),
                        Block("wave-video", "video", ("src", "/media/sample-motion.webm"), ("caption", "A short motion study: follow the repeating shape.")),
                        Block("wave-citation", "citation", ("url", "https://openstax.org/books/college-physics-2e/pages/16-9-waves"), ("title", "OpenStax: Waves"), ("text", "A starting reference for further reading; this seeded reference has not been fetched by the research assistant."))
                    ],
                    Sources =
                    [
                        new() { Id = "wave-reference", Url = "https://openstax.org/books/college-physics-2e/pages/16-9-waves", Title = "OpenStax: Waves", RetrievedAt = new DateTimeOffset(2026, 9, 26, 0, 0, 0, TimeSpan.Zero), Provider = "starter-reference", Verified = false, BlockIds = ["wave-intro"] }
                    ]
                }
            }
        };
        ContentRules.DerivePaths(state);
        ContentRules.Validate(state, _ => StarterTemplate.Create());
        return state;
    }

    private static LibraryNode Node(string id, string type, string parent, string title, double order, string? accent = null, string? background = null)
        => new() { Id = id, Type = type, ParentId = parent, Title = title, Order = order,
            Appearance = Appearance(accent, background) };
    private static Dictionary<string, System.Text.Json.JsonElement> Appearance(string? accent, string? background)
    {
        var values = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal);
        if (accent is not null) values["accent"] = ContentJson.Value(accent);
        if (background is not null) values["background"] = ContentJson.Value(background);
        return values;
    }
    private static PageBlock Block(string id, string type, params (string key, object value)[] values)
        => new() { Id = id, Type = type, Region = "main", Values = values.ToDictionary(pair => pair.key, pair => ContentJson.Value(pair.value), StringComparer.Ordinal) };
}
