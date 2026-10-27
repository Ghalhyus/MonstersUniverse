using System.Text.RegularExpressions;

namespace MonsutaPotaru.Tests;

/// <summary>En-têtes de sécurité : politique de contenu (CSP) stricte, et aucun script écrit dans les pages qu'elle bloquerait.</summary>
public sealed partial class SecurityHeadersTests
{
    [GeneratedRegex("unsafe-inline")]
    private static partial Regex UnsafeInline();

    /// <summary>Bloc &lt;script&gt; sans src, ou attribut d'événement HTML (en minuscules : pas les paramètres des composants, OnClick=...).</summary>
    [GeneratedRegex(@"(?i:<script(?![^>]*\bsrc=)[^>]*>)|(?<![@\w-])on[a-z]+\s*=\s*""")]
    private static partial Regex InlineScript();

    [Fact]
    public void ContentSecurityPolicy_OnlyTheSite_InProduction()
    {
        var policy = SecurityHeaders.ContentSecurityPolicy(development: false);
        Assert.Contains("script-src 'self';", policy);
        Assert.Contains("connect-src 'self';", policy);
        Assert.Contains("object-src 'none'", policy);
        Assert.Contains("frame-ancestors 'self'", policy);
        Assert.DoesNotContain("localhost", policy);
        Assert.DoesNotContain("unsafe-eval", policy);
        Assert.Single(UnsafeInline().Matches(policy)); // styles seulement
        Assert.Contains("style-src 'self' 'unsafe-inline'", policy);
    }

    [Fact]
    public void ContentSecurityPolicy_AllowsVisualStudioReload_InDevelopment_ButNoInlineScript()
    {
        var policy = SecurityHeaders.ContentSecurityPolicy(development: true);
        Assert.Contains("script-src 'self' http://localhost:* https://localhost:*;", policy);
        Assert.Contains("ws://localhost:*", policy);
        Assert.Single(UnsafeInline().Matches(policy));
    }

    /// <summary>
    /// La CSP n'exécute aucun script écrit dans la page : ni bloc &lt;script&gt; sans <c>src</c>, ni attribut d'événement
    /// HTML (<c>onerror="…"</c>, <c>onclick="…"</c> ; les <c>@onclick</c> de Blazor ne sont pas concernés). Une image de
    /// remplacement passe par <c>data-remplacement</c> (wwwroot/js/app.js).
    /// </summary>
    [Fact]
    public void Pages_HaveNoInlineScript()
    {
        var components = Path.Combine(RealSavesTests.WebRoot(), "..", "Components");
        var inline = InlineScript();
        var found = Directory.EnumerateFiles(components, "*.razor", SearchOption.AllDirectories)
            .SelectMany(file => File.ReadLines(file).Select((line, i) => (file, line, i)))
            .Where(l => inline.IsMatch(l.line) && !l.line.TrimStart().StartsWith("@*") && !l.line.TrimStart().StartsWith("//"))
            .Select(l => $"{Path.GetFileName(l.file)}:{l.i + 1} : {l.line.Trim()}")
            .ToList();
        Assert.True(found.Count == 0, "script écrit dans la page (bloqué par la CSP) :\n" + string.Join("\n", found));
    }
}
