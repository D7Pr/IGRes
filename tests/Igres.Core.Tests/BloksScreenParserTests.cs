using System.Text.Json;
using Igres.Core.Models;
using Igres.Infrastructure.Providers.Real;

namespace Igres.Core.Tests;

public class BloksScreenParserTests
{
    [Fact]
    public void ParseReposts_uses_structured_rows_instead_of_every_media_shaped_id()
    {
        var payload = """
            \"com.instagram.privacy.activity_center.media_repost_delete\" (ety, 17), (ety, 27), (ety, 37),
            items_for_action\":\"223867989958145703456_17707884175,3832585496547172257_6166207766\",
            media_repost_container_non_empty_state
            (dnt, \"702654346_30\"), (f4i, (dkc, \"3867989958145703456_17707884175\"), (dkc, (dqp, true))))
            https:\/\/instagram.fdmm3-1.fna.fbcdn.net\/v\/t51.82787-15\/658852951_18125717065588176_2734255715387250021_n.jpg
            https://i.instagram.com/static/images/bloks/icons/generated/carousel-prism__filled__32-4x.png
            \")\":\"Fred Bakery Oxford Circus\",\"-\":\"12sp\"
            (dnt, \"702654348_30\"), (f4i, (dkc, \"3832585496547172257_6166207766\"), (dkc, (dqp, true))))
            https:\/\/instagram.fdmm3-2.fna.fbcdn.net\/v\/t51.82787-15\/629760104_18358594600207767_780918517232729835_n.jpg
            https://i.instagram.com/static/images/bloks/icons/generated/reels__filled__32-4x.png
            """;
        var body = JsonSerializer.Serialize(new { payload });

        var result = BloksScreenParser.ParseReposts(body, DateTimeOffset.UtcNow);

        result.Items.Should().HaveCount(2);
        result.Items.Select(item => item.Id).Should().BeEquivalentTo(
            "3867989958145703456_17707884175",
            "3832585496547172257_6166207766");
        result.Items.Should().NotContain(item => item.Id == "223867989958145703456_17707884175");

        var carousel = result.Items.Single(item => item.Id == "3867989958145703456_17707884175");
        carousel.MediaKind.Should().Be(MediaKind.Carousel);
        carousel.TextSnippet.Should().Be("Fred Bakery Oxford Circus");
        carousel.PreviewUri.Should().StartWith("https://instagram.fdmm3-1.fna.fbcdn.net/");

        var reel = result.Items.Single(item => item.Id == "3832585496547172257_6166207766");
        reel.MediaKind.Should().Be(MediaKind.Video);
        reel.TextSnippet.Should().Be("Reposted reel");
        reel.PreviewUri.Should().StartWith("https://instagram.fdmm3-2.fna.fbcdn.net/");
    }
}
