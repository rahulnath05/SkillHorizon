using System.Text;
using System.Text.Encodings.Web;

namespace SkillHorizon.Web;

public static class WorkspaceText
{
    public static string Label(string code) => code switch
    {
        "employee" => "Employee",
        "people_manager" => "People manager",
        "competency_lead" => "Competency lead",
        "learning_and_development" => "L&D",
        "platform_admin" => "Platform admin",
        _ => code
    };

    public static string Detail(string code) => code switch
    {
        "employee" => "Your own skills, path, and evidence.",
        "people_manager" => "Coaching and readiness for the people who report to you.",
        "competency_lead" => "Practice packs and the baseline you seal.",
        "learning_and_development" => "Taxonomy, benchmarks, assessment, and activation.",
        "platform_admin" => "Access, retention, and integration health.",
        _ => "A workspace granted to this sign-in."
    };
}

public static class ShellHtml
{
    public static IResult Page(string title, string main, string? correlationId, string? signOutFields)
    {
        var encodedTitle = HtmlEncoder.Default.Encode(title);
        var reference = string.IsNullOrWhiteSpace(correlationId)
            ? ""
            : $"""<p class="reference">Reference <span class="mono">{HtmlEncoder.Default.Encode(correlationId)}</span></p>""";
        var account = string.IsNullOrWhiteSpace(signOutFields)
            ? ""
            : $"""
              <form method="post" action="/signout" class="signout">
                {signOutFields}
                <button type="submit" class="text-button">Sign out</button>
              </form>
              """;
        var html = $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <meta name="robots" content="noindex">
              <title>{{encodedTitle}} · SkillHorizon</title>
              <link rel="preconnect" href="https://fonts.googleapis.com">
              <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Plus+Jakarta+Sans:wght@400;500;600;700&amp;display=swap">
              <link rel="stylesheet" href="/shell.css">
            </head>
            <body>
              <a class="skip" href="#main">Skip to content</a>
              <header class="top">
                <p class="brand">SkillHorizon</p>
                {{account}}
              </header>
              <main id="main" class="wrap">
                <section class="card">
                  {{main}}
                </section>
                {{reference}}
              </main>
            </body>
            </html>
            """;
        return Results.Content(html, "text/html", Encoding.UTF8);
    }

    public static string Encode(string? value) => HtmlEncoder.Default.Encode(value ?? "");

    public static string Banner(string kind, string text) =>
        $"""<p class="banner banner-{kind}" role="alert">{Encode(text)}</p>""";
}
