using Igres.Core.Providers.Real;

namespace Igres.Core.Tests;

public class CapturedRequestParserTests
{
    private const string Sample = """
        request:
        GET /api/v1/feed/saved/all/ HTTP/2
        Host: i.instagram.com
        Authorization: Bearer IGT:2:redacted-token
        User-Agent: Instagram 423.1.0.30.69 (iPhone10,6; iOS 16_7_10)
        X-Ig-App-Id: 124024574287414
        X-Ig-Device-Id: ABCDEF-1111-2222-3333-444444444444
        X-Ig-Family-Device-Id: 99999999-0000-0000-0000-111111111111
        X-Mid: somemidvalue
        Ig-U-Ds-User-Id: 368499107100
        Ig-Intended-User-Id: 368499107100
        Ig-U-Rur: rur-blob
        X-Bloks-Version-Id: bloksversion
        Accept-Language: en-US
        X-Ig-App-Locale: en_US
        X-Ig-Device-Locale: en_US
        X-Ig-Mapped-Locale: en_US

        response:
        HTTP/2 200
        Content-Type: application/json
        """;

    [Fact]
    public void Parses_a_full_captured_block()
    {
        var parsed = CapturedRequestParser.Parse(Sample);
        parsed.Authorization.Should().StartWith("Bearer IGT:2:");
        parsed.XIgAppId.Should().Be("124024574287414");
        parsed.IgUDsUserId.Should().Be("368499107100");
        parsed.XIgFamilyDeviceId.Should().NotBeEmpty();
    }

    [Fact]
    public void Stops_at_response_boundary()
    {
        var parsed = CapturedRequestParser.Parse(Sample);
        // Response section had Content-Type but parser must not have pulled it into any field.
        parsed.UserAgent.Should().StartWith("Instagram 423.1.0.30.69");
    }

    [Fact]
    public void Display_summary_masks_user_id()
    {
        var parsed = CapturedRequestParser.Parse(Sample);
        parsed.DisplaySummary.Should().NotContain("368499107100");
        parsed.DisplaySummary.Should().Contain("368499");
    }

    [Fact]
    public void Missing_required_header_throws_with_name()
    {
        const string incomplete = "GET / HTTP/2\nUser-Agent: x\nX-Ig-App-Id: 1\nX-Ig-Device-Id: 2\nX-Mid: 3\nIg-U-Ds-User-Id: 4";
        var act = () => CapturedRequestParser.Parse(incomplete);
        act.Should().Throw<CapturedRequestParseException>().WithMessage("*Authorization*");
    }

    [Fact]
    public void Blank_input_throws()
    {
        var act = () => CapturedRequestParser.Parse("   \n  \n");
        act.Should().Throw<CapturedRequestParseException>();
    }
}
