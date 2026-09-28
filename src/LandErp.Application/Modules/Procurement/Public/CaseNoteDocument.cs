using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Encodings.Web;

namespace LandErp.Application.Modules.Procurement.Contracts;

public sealed record ValidatedCaseNote(string Json, string Html, IReadOnlyCollection<Guid> AttachmentIds)
{
    public IReadOnlyCollection<Guid> ImageIds { get; init; } = [];
}

/// <summary>A bounded document schema, not an HTML sanitizer/editor. Never render client HTML or attributes.</summary>
public static class CaseNoteDocument
{
    public const int MaxBytes = 128 * 1024;
    public const string Empty = "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\"}]}";
    // JSON is stored as data, never embedded into HTML. Html is independently encoded below.
    private static readonly JsonSerializerOptions StorageOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static ValidatedCaseNote Validate(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaxBytes)
            throw new ArgumentException("Документ пуст или превышает 128 КБ.");
        try
        {
            using JsonDocument source = JsonDocument.Parse(json, new() { MaxDepth = 48 });
            HashSet<Guid> images = [];
            HashSet<Guid> imageOnly = [];
            int count = 0;
            (JsonObject node, string html) = Visit(source.RootElement, "", 0);
            string canonical = node.ToJsonString(StorageOptions);
            if (Encoding.UTF8.GetByteCount(canonical) > MaxBytes) throw Invalid();
            return new(canonical, html, images) { ImageIds = imageOnly };

            (JsonObject Node, string Html) Visit(JsonElement input, string parent, int depth)
            {
                if (depth > 16 || ++count > 4000 || input.ValueKind != JsonValueKind.Object) throw Invalid();
                string type = input.GetProperty("type").GetString() ?? "";
                bool block = type is "paragraph" or "heading" or "bulletList" or "orderedList" or "table" or "image" or "attachment";
                bool allowed = parent switch
                {
                    "" => type == "doc",
                    "doc" or "listItem" or "tableCell" or "tableHeader" => block,
                    "paragraph" or "heading" => type is "text" or "hardBreak",
                    "bulletList" or "orderedList" => type == "listItem",
                    "table" => type == "tableRow",
                    "tableRow" => type is "tableCell" or "tableHeader",
                    _ => false
                };
                if (!allowed) throw Invalid();
                JsonObject output = new() { ["type"] = type };
                if (type == "text")
                {
                    string text = input.GetProperty("text").GetString() ?? "";
                    if (text.Length == 0) throw Invalid();
                    output["text"] = text;
                    string encoded = WebUtility.HtmlEncode(text);
                    if (input.TryGetProperty("marks", out var marks))
                    {
                        if (marks.ValueKind != JsonValueKind.Array || marks.GetArrayLength() > 3) throw Invalid();
                        JsonArray clean = [];
                        HashSet<string> seen = [];
                        foreach (JsonElement mark in marks.EnumerateArray())
                        {
                            string kind = mark.GetProperty("type").GetString() ?? "";
                            if (!seen.Add(kind)) throw Invalid();
                            JsonObject safeMark = new() { ["type"] = kind };
                            if (kind == "link")
                            {
                                string href = mark.GetProperty("attrs").GetProperty("href").GetString() ?? "";
                                if (href.Length > 2048 || href.Any(char.IsControl) || !Uri.TryCreate(href, UriKind.Absolute, out var uri)
                                    || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length != 0 || uri.Host.Length == 0) throw Invalid();
                                safeMark["attrs"] = new JsonObject { ["href"] = uri.AbsoluteUri };
                                encoded = $"<a href=\"{WebUtility.HtmlEncode(uri.AbsoluteUri)}\" target=\"_blank\" rel=\"noopener noreferrer\">{encoded}</a>";
                            }
                            else if (kind is "bold" or "italic")
                            {
                                string tag = kind == "bold" ? "strong" : "em";
                                encoded = $"<{tag}>{encoded}</{tag}>";
                            }
                            else throw Invalid();
                            clean.Add(safeMark);
                        }
                        output["marks"] = clean;
                    }
                    return (output, encoded);
                }
                if (type == "hardBreak") return (output, "<br>");
                if (type is "image" or "attachment")
                {
                    // Only a CaseAttachment ID is stored. URL, style, handlers and base64 are discarded.
                    string idText = input.GetProperty("attrs").GetProperty("attachmentId").GetString() ?? "";
                    if (!Guid.TryParse(idText, out Guid id) || id == Guid.Empty || images.Count >= 100) throw Invalid();
                    images.Add(id);
                    output["attrs"] = new JsonObject { ["attachmentId"] = id.ToString() };
                    if (type == "attachment")
                    {
                        string name = input.GetProperty("attrs").GetProperty("name").GetString() ?? "Файл";
                        if (name.Length is < 1 or > 512) throw Invalid();
                        output["attrs"]!["name"] = name;
                        return (output, $"<p><a href=\"/api/procurement/attachments/{id}\" target=\"_blank\" rel=\"noopener noreferrer\">{WebUtility.HtmlEncode(name)}</a></p>");
                    }
                    imageOnly.Add(id);
                    return (output, $"<img src=\"/api/procurement/attachments/{id}/image\" alt=\"Изображение заметки\" loading=\"lazy\">");
                }
                string tagName = type switch
                {
                    "doc" => "div", "paragraph" => "p", "heading" => "h2", "bulletList" => "ul",
                    "orderedList" => "ol", "listItem" => "li", "table" => "table", "tableRow" => "tr",
                    "tableCell" => "td", "tableHeader" => "th", _ => throw Invalid()
                };
                if (type is "tableCell" or "tableHeader" && input.TryGetProperty("attrs", out var cellAttrs))
                {
                    foreach (string span in new[] { "colspan", "rowspan" })
                        if (cellAttrs.TryGetProperty(span, out var value) && value.GetInt32() != 1)
                            throw new ArgumentException("Используйте простую таблицу без объединённых ячеек.");
                }
                if (type == "heading")
                {
                    int level = input.GetProperty("attrs").GetProperty("level").GetInt32();
                    if (level is < 1 or > 3) throw Invalid();
                    output["attrs"] = new JsonObject { ["level"] = level };
                    tagName = "h" + level;
                }
                JsonArray children = [];
                StringBuilder body = new();
                if (input.TryGetProperty("content", out var content))
                {
                    if (content.ValueKind != JsonValueKind.Array) throw Invalid();
                    foreach (var child in content.EnumerateArray())
                    {
                        var next = Visit(child, type, depth + 1);
                        children.Add(next.Node); body.Append(next.Html);
                    }
                }
                if (children.Count == 0 && type is not ("paragraph" or "heading")) throw Invalid();
                if (children.Count > 0) output["content"] = children;
                // Cell spanning is intentionally unsupported; a simple rectangular table is predictable.
                if (type == "table")
                {
                    int width = children[0]!["content"]!.AsArray().Count;
                    if (width > 20 || children.Count > 100 || children.Any(row => row!["content"]!.AsArray().Count != width)) throw Invalid();
                    body.Insert(0, "<tbody>").Append("</tbody>");
                }
                return (output, $"<{tagName}>{body}</{tagName}>");
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { throw new ArgumentException("Неподдерживаемый формат документа.", exception); }
    }

    public static bool IsImageType(string contentType) => contentType is "image/png" or "image/jpeg" or "image/webp" or "image/gif";

    public static bool IsPlain(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("content").EnumerateArray().All(node =>
            node.GetProperty("type").GetString() is "image" or "attachment" ||
            node.GetProperty("type").GetString() == "paragraph" &&
            (!node.TryGetProperty("content", out var children) || children.EnumerateArray().All(child =>
                child.GetProperty("type").GetString() == "hardBreak" || child.GetProperty("type").GetString() == "text" &&
                (!child.TryGetProperty("marks", out var marks) || marks.GetArrayLength() == 0))));
    }

    // Legacy values are text, including strings that look like HTML; never parse them as markup.
    public static string FromPlainText(string text)
    {
        JsonObject paragraph = new() { ["type"] = "paragraph" };
        if (text.Length > 0) paragraph["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text });
        return new JsonObject { ["type"] = "doc", ["content"] = new JsonArray(paragraph) }.ToJsonString(StorageOptions);
    }

    public static string PlainText(string validatedJson, bool includeAttachmentLabels = true)
    {
        using JsonDocument document = JsonDocument.Parse(validatedJson);
        StringBuilder result = new();
        Append(document.RootElement);
        return result.ToString().Trim();
        void Append(JsonElement node)
        {
            string? type = node.GetProperty("type").GetString();
            if (type == "text") result.Append(node.GetProperty("text").GetString());
            if (type == "image") result.Append("[Изображение]");
            if (type == "attachment" && includeAttachmentLabels) result.Append("[Файл: ").Append(node.GetProperty("attrs").GetProperty("name").GetString()).Append(']');
            if (type == "hardBreak") result.AppendLine();
            if (node.TryGetProperty("content", out var content)) foreach (var child in content.EnumerateArray()) Append(child);
            if (type is "paragraph" or "heading" or "tableRow" or "image") result.AppendLine();
        }
    }

    private static ArgumentException Invalid() => new("Документ содержит неподдерживаемые элементы или небезопасные ссылки.");
}
