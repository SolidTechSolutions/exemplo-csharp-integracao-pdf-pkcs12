using System.Text.Json.Serialization;

namespace SolidSign.Examples;

public record SignLink(string Rel, string Href);
public record SelfHref(string Href);
public record HalLinks([property: JsonPropertyName("self")] SelfHref? Self);
public record SignDocument(
    List<SignLink>? Links,
    [property: JsonPropertyName("_links")] HalLinks? HalLinks);
public record SignResponse(List<SignDocument> Documents, string? Identifier);
