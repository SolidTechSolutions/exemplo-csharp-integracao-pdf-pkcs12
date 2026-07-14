/**
 * [EN]    PAdES (PDF) signing example — PKCS#12 pre-imported certificate.
 *         Run: dotnet run
 *         Batch: POST http://localhost:5088/api/pdf/sign-pkcs12
 *         Form:  POST http://localhost:5088/api/pdf/sign/form
 *
 * [PT-BR] Exemplo de assinatura PAdES (PDF) — certificado PKCS#12 pré-importado.
 *         Executar: dotnet run
 */
using SolidSign.Examples;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient<PdfPkcs12Service>();
var app = builder.Build();

// ── Batch endpoint ──────────────────────────────────────────────────────────
app.MapPost("/api/pdf/sign-pkcs12", async (PdfPkcs12Service svc, IConfiguration cfg) =>
{
    var inputPath  = cfg["SolidSign:Batch:InputPath"] ?? "";
    var outputPath = cfg["SolidSign:Batch:OutputPath"] ?? "";
    var certId     = cfg["SolidSign:Cert:Id"] ?? "";

    if (!Directory.Exists(inputPath)) return Results.BadRequest(new { error = $"Invalid input path: {inputPath}" });

    var pdfFiles = Directory.GetFiles(inputPath, "*.pdf");
    if (pdfFiles.Length == 0) return Results.Ok(new { message = $"No PDF files found in {inputPath}" });

    Console.WriteLine($"Found {pdfFiles.Length} files for local processing.");
    var result = await svc.SignPkcs12Async(pdfFiles, certId, outputPath);

    return result is not null
        ? Results.Ok(new { message = $"Processing completed! ZIP generated at: {result}" })
        : Results.Problem("Processing failed. Check logs.");
});

// ── Form endpoint ───────────────────────────────────────────────────────────
app.MapPost("/api/pdf/sign/form", async (HttpRequest req, PdfPkcs12Service svc) =>
{
    var form = await req.ReadFormAsync();
    var documents       = form.Files.GetFiles("document");
    var signatureImages = form.Files.GetFiles("signatureImage");
    string? G(string k) => form.TryGetValue(k, out var v) ? v.ToString() : null;

    var zip = await svc.SignPkcs12FormAsync(
        authorization: G("authorization")!, baseUrl: G("baseUrl")!, pfxCode: G("pfxCode")!,
        documents: documents, signatureImages: signatureImages,
        profile: G("profile"), hashAlgorithm: G("hashAlgorithm"), policyVersion: G("policyVersion"),
        sigFieldMeasurementUnit: G("sigFieldMeasurementUnit"), signatureFieldConfig: G("signatureFieldConfig"),
        reason: G("reason"), location: G("location"), contact: G("contact"),
        signatureFieldName: G("signatureFieldName"), signatureTextConfig: G("signatureTextConfig"),
        mdpPermissionLevel: G("mdpPermissionLevel"), passwordsForDecryption: G("passwordsForDecryption"),
        documentInfoMetadata: G("documentInfoMetadata"), signatureQrCodeConfig: G("signatureQrCodeConfig"));

    return zip is not null
        ? Results.File(zip, "application/zip", "signed_pdf.zip")
        : Results.Problem("Processing failed. Check logs.");
});

app.Run("http://0.0.0.0:5088");
