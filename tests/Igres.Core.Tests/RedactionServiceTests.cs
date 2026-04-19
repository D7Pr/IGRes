using Igres.Core.Logging;

namespace Igres.Core.Tests;

public class RedactionServiceTests
{
    private readonly RedactionService _redactor = new();

    [Fact]
    public void Redacts_authorization_header()
    {
        var input = "GET /api/things\nAuthorization: mysecrettoken123abc\nAccept: */*";
        var redacted = _redactor.Redact(input);
        redacted.Should().NotContain("mysecrettoken123abc");
        redacted.Should().Contain("Authorization:");
    }

    [Fact]
    public void Redacts_jwt_shaped_token()
    {
        var input = "token is eyJabcdefghij.payloadabcdef123.signaturexyz12345 end";
        var redacted = _redactor.Redact(input);
        redacted.Should().NotContain("eyJabcdefghij.payloadabcdef123.signaturexyz12345");
    }

    [Fact]
    public void Redacts_cookie_header()
    {
        var input = "Cookie: sessionid=abc123; csrftoken=xyz789";
        var redacted = _redactor.Redact(input);
        redacted.Should().NotContain("abc123");
        redacted.Should().NotContain("xyz789");
    }

    [Fact]
    public void Redacts_json_token_field()
    {
        var input = "{\"access_token\":\"eyJabc.payload.sig\",\"other\":\"ok\"}";
        var redacted = _redactor.Redact(input);
        redacted.Should().NotContain("eyJabc.payload.sig");
        redacted.Should().Contain("\"other\":\"ok\"");
    }

    [Fact]
    public void RedactException_redacts_message_and_stack()
    {
        try
        {
            throw new InvalidOperationException("Authorization: topsecretvalue123 failed");
        }
        catch (Exception ex)
        {
            var text = _redactor.RedactException(ex);
            text.Should().NotContain("topsecretvalue123");
        }
    }
}
