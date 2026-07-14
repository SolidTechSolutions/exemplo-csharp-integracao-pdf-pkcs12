using System.IO.Compression;
using System.Text.Json;

namespace SolidSign.Examples;

/// <summary>
/// [EN]    PAdES (PDF) signing service — PKCS#12 certificate pre-imported into SolidSign cache.
///         Import once via POST /solidsign/dsig/certificates/pkcs12/import, then set SolidSign:Cert:Id.
/// [PT-BR] Serviço de assinatura PAdES (PDF) — certificado PKCS#12 pré-importado na cache do SolidSign.
///         Importe uma vez via POST /solidsign/dsig/certificates/pkcs12/import, depois configure SolidSign:Cert:Id.
/// </summary>
public class PdfPkcs12Service(IConfiguration cfg, HttpClient http)
{
    private string BaseUrl    => cfg["SolidSign:Api:BaseUrl"]!.TrimEnd('/');
    private string Auth       => cfg["SolidSign:Api:Authorization"]!;
    private string Profile    => cfg["SolidSign:Sig:Profile"] ?? "ADRB";
    private string HashAlg    => cfg["SolidSign:Sig:HashAlgorithm"] ?? "SHA256";
    private string PolicyVer  => cfg["SolidSign:Sig:PolicyVersion"] ?? "";
    private string SigFieldMU => cfg["SolidSign:Sig:SigFieldMeasurementUnit"] ?? "PIXELS";
    private string SigFieldCfg => cfg["SolidSign:Sig:SignatureFieldConfig"] ?? "";
    private string Reason     => cfg["SolidSign:Sig:Reason"] ?? "";
    private string Location   => cfg["SolidSign:Sig:Location"] ?? "";
    private string Contact    => cfg["SolidSign:Sig:Contact"] ?? "";
    private IEnumerable<string> SigImagePaths =>
        (cfg["SolidSign:Sig:SignatureImagePaths"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // ── Batch endpoint ────────────────────────────────────────────────────────

    public async Task<string?> SignPkcs12Async(IEnumerable<string> pdfPaths, string certId, string outputDir)
    {
        using var form = BuildForm(pdfPaths, SigImagePaths);
        form.Add(new StringContent(certId),      "pfxCode");
        form.Add(new StringContent(Profile),     "profile");
        form.Add(new StringContent(HashAlg),     "hashAlgorithm");
        form.Add(new StringContent(SigFieldMU),  "sigFieldMeasurementUnit");
        AddIndexedJson(form, "signatureFieldConfig", SigFieldCfg);
        form.Add(new StringContent(Reason),      "reason");
        form.Add(new StringContent(Location),    "location");
        form.Add(new StringContent(Contact),     "contact");
        if (!string.IsNullOrWhiteSpace(PolicyVer)) form.Add(new StringContent(PolicyVer), "policyVersion");
        // Optional — uncomment to use:
        // form.Add(new StringContent("SignatureField1"), "signatureFieldName");
        // AddIndexedJson(form, "signatureTextConfig", "[...]");
        // form.Add(new StringContent("1"),               "mdpPermissionLevel");
        // form.Add(new StringContent("[\"pwd\"]"),       "passwordsForDecryption");
        // form.Add(new StringContent("{\"title\":\"\"}"), "documentInfoMetadata");
        // AddIndexedJson(form, "signatureQrCodeConfig", "[...]");

        var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/solidsign/dsig/pdf/sign-pkcs12") { Content = form };
        req.Headers.TryAddWithoutValidation("Authorization", Auth);
        var resp = await http.SendAsync(req);
        if (!resp.IsSuccessStatusCode) { Console.Error.WriteLine($"SolidSign error {(int)resp.StatusCode}: {await resp.Content.ReadAsStringAsync()}"); return null; }

        var signResp = JsonSerializer.Deserialize<SignResponse>(await resp.Content.ReadAsStringAsync(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var zipBytes = await DownloadAndZipAsync(signResp!, pdfPaths.Select(Path.GetFileName!).ToList(), Auth);

        Directory.CreateDirectory(outputDir);
        var outPath = Path.Combine(outputDir, $"signed_pdf_pkcs12_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.zip");
        await File.WriteAllBytesAsync(outPath, zipBytes);
        Console.WriteLine($"PAdES PKCS12 signing complete. Output: {outPath}");
        return outPath;
    }

    // ── Form endpoint ─────────────────────────────────────────────────────────

    public async Task<byte[]?> SignPkcs12FormAsync(
        string authorization, string baseUrl, string pfxCode,
        IEnumerable<IFormFile> documents, IEnumerable<IFormFile>? signatureImages,
        string? profile, string? hashAlgorithm, string? policyVersion,
        string? sigFieldMeasurementUnit, string? signatureFieldConfig,
        string? reason, string? location, string? contact,
        string? signatureFieldName = null, string? signatureTextConfig = null,
        string? mdpPermissionLevel = null, string? passwordsForDecryption = null,
        string? documentInfoMetadata = null, string? signatureQrCodeConfig = null)
    {
        using var form = new MultipartFormDataContent();
        int i = 0;
        foreach (var d in documents)
        {
            var ms = new MemoryStream(); await d.CopyToAsync(ms); ms.Position = 0;
            form.Add(new StreamContent(ms), $"document[{i++}]", d.FileName);
        }
        i = 0;
        foreach (var img in signatureImages ?? [])
        {
            var ms = new MemoryStream(); await img.CopyToAsync(ms); ms.Position = 0;
            form.Add(new StreamContent(ms), $"signatureImage[{i++}]", img.FileName);
        }
        form.Add(new StringContent(pfxCode), "pfxCode");
        void Add(string? v, string k) { if (!string.IsNullOrWhiteSpace(v)) form.Add(new StringContent(v), k); }
        Add(profile, "profile"); Add(hashAlgorithm, "hashAlgorithm"); Add(policyVersion, "policyVersion");
        Add(sigFieldMeasurementUnit, "sigFieldMeasurementUnit"); AddIndexedJson(form, "signatureFieldConfig", signatureFieldConfig);
        Add(reason, "reason"); Add(location, "location"); Add(contact, "contact");
        Add(signatureFieldName, "signatureFieldName"); AddIndexedJson(form, "signatureTextConfig", signatureTextConfig);
        Add(mdpPermissionLevel, "mdpPermissionLevel"); Add(passwordsForDecryption, "passwordsForDecryption");
        Add(documentInfoMetadata, "documentInfoMetadata"); AddIndexedJson(form, "signatureQrCodeConfig", signatureQrCodeConfig);

        var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/solidsign/dsig/pdf/sign-pkcs12") { Content = form };
        req.Headers.TryAddWithoutValidation("Authorization", authorization);
        var resp = await http.SendAsync(req);
        if (!resp.IsSuccessStatusCode) { Console.Error.WriteLine($"SolidSign error {(int)resp.StatusCode}"); return null; }

        var signResp = JsonSerializer.Deserialize<SignResponse>(await resp.Content.ReadAsStringAsync(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var origNames = documents.Select(d => d.FileName).ToList();
        return await DownloadAndZipAsync(signResp!, origNames, authorization);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static MultipartFormDataContent BuildForm(IEnumerable<string> filePaths, IEnumerable<string> imagePaths)
    {
        var form = new MultipartFormDataContent();
        int i = 0;
        foreach (var fp in filePaths)
            form.Add(new StreamContent(File.OpenRead(fp)), $"document[{i++}]", Path.GetFileName(fp));
        i = 0;
        foreach (var ip in imagePaths)
            if (File.Exists(ip)) form.Add(new StreamContent(File.OpenRead(ip)), $"signatureImage[{i++}]", Path.GetFileName(ip));
        return form;
    }


    // [EN]    Adds a visual-signature config as INDEXED fields (key[0], key[1], ...).
    //         The API expects signatureFieldConfig[0]={...} per document, NOT a single
    //         signatureFieldConfig=[{...}] — otherwise the field is ignored and the stamp never appears.
    // [PT-BR] Adiciona a config de assinatura visual como campos INDEXADOS (key[0], key[1], ...).
    //         A API espera signatureFieldConfig[0]={...} por documento, e NÃO um único
    //         signatureFieldConfig=[{...}] — senão o campo é ignorado e o carimbo não aparece.
    private static void AddIndexedJson(MultipartFormDataContent form, string key, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                int i = 0;
                foreach (var item in doc.RootElement.EnumerateArray())
                    form.Add(new StringContent(item.GetRawText()), $"{key}[{i++}]");
            }
            else
            {
                form.Add(new StringContent(doc.RootElement.GetRawText()), $"{key}[0]");
            }
        }
        catch (JsonException)
        {
            form.Add(new StringContent(raw), $"{key}[0]");
        }
    }

    private async Task<byte[]> DownloadAndZipAsync(SignResponse signResp, List<string?> originalNames, string auth)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (int i = 0; i < signResp.Documents.Count; i++)
            {
                var href = signResp.Documents[i].HalLinks?.Self?.Href
                        ?? signResp.Documents[i].Links?.FirstOrDefault(l => l.Rel == "self")?.Href;
                if (href is null) continue;
                var dlReq = new HttpRequestMessage(HttpMethod.Get, href);
                dlReq.Headers.TryAddWithoutValidation("Authorization", auth);
                var dlResp = await http.SendAsync(dlReq);
                if (!dlResp.IsSuccessStatusCode) continue;
                var entry = archive.CreateEntry($"signed_{originalNames[i]}");
                await using var entryStream = entry.Open();
                await (await dlResp.Content.ReadAsStreamAsync()).CopyToAsync(entryStream);
            }
        }
        return ms.ToArray();
    }
}
