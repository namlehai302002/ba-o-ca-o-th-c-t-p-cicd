using Microsoft.AspNetCore.Mvc;

using Microsoft.AspNetCore.Authorization;

using Microsoft.EntityFrameworkCore;

using WMS.Data;

using WMS.Models;

using WMS.ViewModels;

using WMS.Authorization;

using WMS.Common;

using WMS.Services;

using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;

using System.Linq;

using ClosedXML.Excel;

using System.Globalization;

using System.Data;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.RateLimiting;
using System.Diagnostics;
using System.Net;

namespace WMS.Controllers;

public partial class VouchersController
{
    private const string DocumentIntakeManualFallbackGuidance =
        "Bạn vẫn có thể nhập bằng Excel, quét mã vạch hoặc chọn vật tư thủ công trên phiếu. Nếu cần đọc tự động, vui lòng kiểm tra trạng thái dịch vụ đọc chứng từ và cấu hình provider dự phòng.";

    private const string DefaultGroqVisionModel = "qwen/qwen3.6-27b";
    private const string DefaultGeminiVisionModel = "gemini-2.5-flash";
    private const int MaxDocumentBatchSize = 6;
    private const string VoucherImportWorksheetName = "ImportLines";
    private const string VoucherImportMetadataWorksheetName = "_WMS_META";
    private const string VoucherImportTemplateVersion = "WMS-VOUCHER-LINES-1.0";
    private const long MaxVoucherImportBytes = 5 * 1024 * 1024;
    private const int MaxVoucherImportRows = 1000;
    private const decimal MaxVoucherImportDecimal = 99999999999999.9999m;

    private static readonly string[] VoucherImportHeaders =
    {
        "ItemCode",
        "ItemName",
        "Quantity",
        "UnitPrice",
        "UnitName",
        "LocationCode",
        "ExpiryDate (yyyy-MM-dd)",
        "LotNumber",
        "DefectQty",
        "Notes"
    };

    private static readonly HashSet<string> LegacyDocumentReaderImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    private static readonly HashSet<string> LegacyDocumentReaderExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".pdf"
    };

    [HttpPost]
    [Authorize(Roles = "Admin,Manager,Staff,InboundStaff,OutboundStaff,InventoryStaff,TransportStaff")]
    [Authorize(Policy = WmsPermissions.VoucherCreate)]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("ocr")]
    public async Task<IActionResult> AnalyzeReceipt(IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest("Vui lòng chọn chứng từ cần đọc.");

        if (ShouldUseLegacyDocumentReaderFirst(file))
            return await AnalyzeReceiptWithLegacyReaderAsync(file);

        var allowLegacyFallback = _config.GetValue<bool>("MinerU:AllowLegacyFallback");
        try
        {
            return await AnalyzeReceiptWithDocumentIntakeAsync(file);
        }
        catch (BusinessRuleException ex) when (allowLegacyFallback && CanUseLegacyDocumentReader(file))
        {
            _logger.LogWarning(ex, "MinerU không xử lý được chứng từ, chuyển sang bộ đọc chứng từ dự phòng.");
            return await AnalyzeReceiptWithLegacyReaderAsync(file);
        }
        catch (BusinessRuleException ex)
        {
            return BadRequest(new
            {
                error = UserSafeError.From(ex),
                guidance = DocumentIntakeManualFallbackGuidance,
                code = ex.Code
            });
        }
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Manager,Staff,InboundStaff,OutboundStaff,InventoryStaff,TransportStaff")]
    [Authorize(Policy = WmsPermissions.VoucherCreate)]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("ocr")]
    public async Task<IActionResult> AnalyzeReceipts(List<IFormFile> files)
    {
        if (files == null || files.Count == 0)
            return BadRequest(new { error = "Vui lòng chọn ít nhất một chứng từ cần đọc." });

        if (files.Count > MaxDocumentBatchSize)
            return BadRequest(new { error = $"Mỗi lần chỉ được đọc tối đa {MaxDocumentBatchSize} chứng từ để kiểm soát tải và kết quả." });

        var batchWarnings = new List<string>();
        var batchErrors = new List<string>();
        var seenHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var documentGroups = new Dictionary<string, BatchDocumentGroup>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files.Where(f => f != null && f.Length > 0))
        {
            var safeFileName = Path.GetFileName(file.FileName);
            var contentHash = await ComputeSha256Async(file, HttpContext.RequestAborted);
            if (seenHashes.TryGetValue(contentHash, out var originalFileName))
            {
                batchWarnings.Add($"File {safeFileName} trùng nội dung với {originalFileName}; hệ thống đã bỏ qua để không nhân đôi số lượng.");
                continue;
            }
            seenHashes[contentHash] = safeFileName;

            var singleResult = await AnalyzeReceipt(file);
            if (singleResult is not OkObjectResult ok)
            {
                batchErrors.Add($"{safeFileName}: {ExtractErrorText(singleResult)}");
                continue;
            }

            var payload = ExtractReceiptPayload(ok.Value!, safeFileName);
            var documentKey = BuildDocumentBusinessKey(payload.Header, contentHash);
            var referenceNo = ReadHeaderString(payload.Header, "ReferenceNo", "referenceNo");
            if (documentGroups.TryGetValue(documentKey, out var existing))
            {
                existing.SourceFiles.Add(safeFileName);
                existing.DuplicateDocumentFiles.Add(safeFileName);
                existing.Warnings.Add($"Chứng từ {FormatDocumentLabel(existing.ReferenceNo)} đã được đọc từ file khác; bỏ qua {safeFileName} để không cộng dồn số lượng.");
                batchWarnings.Add($"{safeFileName}: chứng từ {FormatDocumentLabel(existing.ReferenceNo)} bị trùng, không tính vào dòng áp dụng.");
                continue;
            }

            var dedupe = DeduplicateDocumentLines(payload.Lines, referenceNo);
            var group = new BatchDocumentGroup
            {
                DocumentKey = documentKey,
                ReferenceNo = referenceNo,
                VoucherDate = NormalizeDocumentDate(ReadHeaderString(payload.Header, "VoucherDate", "voucherDate")),
                PartnerName = ReadHeaderString(payload.Header, "PartnerName", "partnerName", "PartnerCode", "partnerCode"),
                Header = payload.Header,
                Lines = dedupe.Lines,
                Provider = payload.Provider,
                ParseStatus = payload.ParseStatus,
                Confidence = payload.Confidence,
                SourceLogId = payload.LogId
            };
            group.SourceFiles.Add(safeFileName);
            group.Warnings.AddRange(payload.Warnings);
            group.Warnings.AddRange(dedupe.Warnings);
            documentGroups[documentKey] = group;
        }

        if (documentGroups.Count == 0)
        {
            var message = batchErrors.Count > 0
                ? string.Join(Environment.NewLine, batchErrors.Take(5))
                : "Không nhận diện được chứng từ hợp lệ.";
            return BadRequest(new
            {
                error = message,
                guidance = DocumentIntakeManualFallbackGuidance,
                warnings = batchWarnings
            });
        }

        var documents = documentGroups.Values.ToList();
        var readyLineCount = documents.Sum(d => d.Lines.Count(IsReadyMappedLine));
        return Ok(new
        {
            provider = "Batch",
            parseStatus = batchErrors.Count == 0 && documents.All(d => string.Equals(d.ParseStatus, "Success", StringComparison.OrdinalIgnoreCase))
                ? "Success"
                : "Partial",
            documents,
            warnings = batchWarnings.Concat(batchErrors).ToList(),
            duplicateFileCount = files.Count(f => f != null && f.Length > 0) - seenHashes.Count,
            duplicateDocumentCount = documents.Sum(d => d.DuplicateDocumentFiles.Count),
            requiresDocumentSelection = documents.Count > 1,
            canAutoApply = documents.Count == 1,
            readyLineCount
        });
    }

    private async Task<IActionResult> AnalyzeReceiptWithDocumentIntakeAsync(IFormFile file)
    {
        var intake = await _voucherDocumentIntakeService.AnalyzeAsync(
            file,
            User.Identity?.Name ?? "system",
            HttpContext.RequestAborted);

        await ApplyDocumentIntakeScopeAsync(intake, HttpContext.RequestAborted);

        return Ok(new
        {
            data = JsonSerializer.Serialize(intake.Lines),
            header = intake.Header,
            rawText = intake.RawText,
            provider = intake.Provider,
            logId = intake.LogId,
            warnings = intake.Warnings,
            confidence = intake.Confidence,
            parseStatus = intake.ParseStatus
        });
    }

    private async Task ApplyDocumentIntakeScopeAsync(
        VoucherDocumentIntakeResult intake,
        CancellationToken cancellationToken)
    {
        var scopeWarnings = new List<string>();
        await ApplyDocumentHeaderScopeAsync(intake.Header, scopeWarnings, cancellationToken);

        var allowedOwnerIds = GetOwnerScopeClaimIds();
        if (allowedOwnerIds.Count > 0)
        {
            var candidateItemIds = intake.Lines
                .SelectMany(line => new int?[] { line.ItemId, line.SuggestedItemId })
                .Where(itemId => itemId.HasValue)
                .Select(itemId => itemId!.Value)
                .Distinct()
                .ToList();

            var visibleItemIds = candidateItemIds.Count == 0
                ? new HashSet<int>()
                : (await _db.Items.AsNoTracking()
                    .Where(item => candidateItemIds.Contains(item.ItemId)
                        && item.IsActive
                        && (!item.OwnerPartnerId.HasValue || allowedOwnerIds.Contains(item.OwnerPartnerId.Value)))
                    .Select(item => item.ItemId)
                    .ToListAsync(cancellationToken))
                    .ToHashSet();

            foreach (var line in intake.Lines)
            {
                if (line.ItemId.HasValue && !visibleItemIds.Contains(line.ItemId.Value))
                {
                    line.ItemId = null;
                    line.ItemCode = null;
                    line.ItemName = null;
                    line.BaseUomId = null;
                    line.IsMatched = false;
                    line.RequiresReview = true;
                    line.MatchKind = "OutOfScope";
                    line.Warnings.Add($"Dòng {line.LineNumber}: vật tư nhận diện được nằm ngoài phạm vi chủ hàng của bạn; vui lòng chọn vật tư được phép.");
                }

                if (line.SuggestedItemId.HasValue && !visibleItemIds.Contains(line.SuggestedItemId.Value))
                {
                    line.SuggestedItemId = null;
                    line.SuggestedItemCode = null;
                    line.SuggestedItemName = null;
                    line.RequiresReview = true;
                    if (!string.Equals(line.MatchKind, "OutOfScope", StringComparison.Ordinal))
                        line.MatchKind = "Unmatched";
                    line.Warnings.Add($"Dòng {line.LineNumber}: gợi ý vật tư ngoài phạm vi đã được ẩn; vui lòng chọn thủ công.");
                }
            }
        }

        scopeWarnings.AddRange(intake.Lines.SelectMany(line => line.Warnings));
        intake.Warnings = intake.Warnings
            .Concat(scopeWarnings)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (scopeWarnings.Count > 0 && string.Equals(intake.ParseStatus, "Success", StringComparison.OrdinalIgnoreCase))
            intake.ParseStatus = intake.Lines.Any(line => line.IsMatched && !line.RequiresReview) ? "Partial" : "Failed";
    }

    private async Task ApplyDocumentHeaderScopeAsync(
        MappedDocumentHeader? header,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        if (header == null)
            return;

        var scopedWarehouseId = GetScopedWarehouseId();
        if (scopedWarehouseId.HasValue
            && header.WarehouseId.HasValue
            && header.WarehouseId.Value != scopedWarehouseId.Value)
        {
            const string warning = "Kho nhận diện từ chứng từ nằm ngoài phạm vi được phân quyền; vui lòng chọn kho được phép.";
            header.WarehouseId = null;
            header.WarehouseCode = null;
            header.WarehouseName = null;
            header.Warnings.Add(warning);
            warnings.Add(warning);
        }

        var allowedOwnerIds = GetOwnerScopeClaimIds();
        if (allowedOwnerIds.Count == 0)
            return;

        if (header.OwnerPartnerId.HasValue && !allowedOwnerIds.Contains(header.OwnerPartnerId.Value))
        {
            const string warning = "Chủ hàng nhận diện từ chứng từ nằm ngoài phạm vi được phân quyền; vui lòng chọn chủ hàng được phép.";
            header.OwnerPartnerId = null;
            header.OwnerPartnerCode = null;
            header.OwnerPartnerName = null;
            header.Warnings.Add(warning);
            warnings.Add(warning);
        }

        if (!header.PartnerId.HasValue || allowedOwnerIds.Contains(header.PartnerId.Value))
            return;

        var partnerIsRestrictedOwner = await _db.Partners.AsNoTracking()
            .Where(partner => partner.PartnerId == header.PartnerId.Value)
            .Select(partner => partner.IsThreePlClient)
            .FirstOrDefaultAsync(cancellationToken);
        if (!partnerIsRestrictedOwner)
            return;

        const string partnerWarning = "Đối tác chủ hàng nhận diện từ chứng từ nằm ngoài phạm vi được phân quyền; vui lòng chọn đối tác được phép.";
        header.PartnerId = null;
        header.PartnerCode = null;
        header.PartnerName = null;
        header.Warnings.Add(partnerWarning);
        warnings.Add(partnerWarning);
    }

    private bool ShouldUseLegacyDocumentReaderFirst(IFormFile file)
        => CanUseLegacyDocumentReader(file) && HasLegacyDocumentReaderProvider(file);

    private bool CanUseLegacyDocumentReader(IFormFile file)
    {
        var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant() ?? "";
        return LegacyDocumentReaderExtensions.Contains(ext);
    }

    private bool HasLegacyDocumentReaderProvider(IFormFile file)
    {
        var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant() ?? "";
        var hasGroq = !string.IsNullOrWhiteSpace(_config["GroqApiKey"]);
        var hasGemini = !string.IsNullOrWhiteSpace(_config["GeminiApiKey"]);

        if (LegacyDocumentReaderImageExtensions.Contains(ext))
            return hasGroq || hasGemini;

        return string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase) && hasGemini;
    }

    private async Task<IActionResult> AnalyzeReceiptWithLegacyReaderAsync(IFormFile file)
    {
        string? pendingDocumentPath = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // Basic upload hardening
            const long groqImageMaxBytes = 4 * 1024 * 1024; // Groq image URL payload limit.
            const long geminiDocumentMaxBytes = 10 * 1024 * 1024;
            if (file.Length > geminiDocumentMaxBytes)
                return BadRequest("Tệp chứng từ quá lớn cho chế độ dự phòng. Vui lòng chọn file ≤ 10MB.");

            var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant() ?? "";
            if (!LegacyDocumentReaderExtensions.Contains(ext))
                return BadRequest("Chế độ đọc chứng từ dự phòng chỉ hỗ trợ JPG, PNG, WEBP hoặc PDF.");

            if (!SecurityHelpers.FileUpload.IsContentTypeCompatible(file.FileName, file.ContentType))
            {
                return BadRequest(new
                {
                    error = "MIME của chứng từ không phù hợp với định dạng file.",
                    guidance = DocumentIntakeManualFallbackGuidance,
                    code = "DOCUMENT_FILE_MIME_INVALID"
                });
            }

            var isImage = LegacyDocumentReaderImageExtensions.Contains(ext);

            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream);
            var documentBytes = memoryStream.ToArray();
            using (var probeStream = new MemoryStream(documentBytes, writable: false))
            {
                if (!SecurityHelpers.FileUpload.HasExpectedFileSignature(file.FileName, probeStream))
                {
                    return BadRequest(new
                    {
                        error = "Nội dung chứng từ không khớp với định dạng file.",
                        guidance = DocumentIntakeManualFallbackGuidance,
                        code = "DOCUMENT_FILE_SIGNATURE_INVALID"
                    });
                }
            }
            var base64Document = Convert.ToBase64String(documentBytes);
            var mimeType = SecurityHelpers.FileUpload.GetCanonicalContentType(ext)!;

            // === PROMPT CHUNG CHO CẢ 2 PROVIDER ===
            var ocrPrompt = """
Extract the warehouse document as structured WMS data. Return ONLY one raw JSON object, no markdown, no explanation.
Required shape:
{
  "Header": {
    "ReferenceNo": "document/order/invoice/ASN/DO number or null",
    "VoucherDate": "yyyy-MM-dd or null",
    "PartnerCode": "supplier/customer code if visible or null",
    "PartnerName": "supplier/customer/source/destination name or null",
    "WarehouseCode": "warehouse/facility code if visible or null",
    "WarehouseName": "warehouse/facility name if visible or null",
    "InventoryOwnershipMode": "Internal or ThreePl or null",
    "OwnerPartnerCode": "owner/customer-warehouse code if visible or null",
    "OwnerPartnerName": "owner/customer-warehouse name if visible or null",
    "CarrierName": "carrier/transport company or null",
    "VehicleNumber": "truck/vehicle/license plate or null",
    "DriverName": "driver/deliverer/receiver person or null",
    "DriverPhone": "driver phone or null",
    "DockDoor": "dock/receiving/shipping door or null",
    "Description": "note/remark or null"
  },
  "Lines": [
    {
      "ItemCode": "IT-LAP-DELL-5420",
      "ItemName": "Laptop Dell Latitude 5420",
      "Quantity": 1.0,
      "UnitPrice": 1000,
      "UnitName": "Cái",
      "LotNumber": "lot/batch or null",
      "ManufacturingDate": "yyyy-MM-dd or null",
      "ExpiryDate": "yyyy-MM-dd or null"
    }
  ]
}
Keep all visible line items. UnitName examples: Cái, Bộ, Cuộn, Chai, m², kg, Hộp, Thùng, Pcs, Pair, Set. If UnitPrice is not visible, use 0. If a header field is not visible, use null.
""";

            // === GỜI AI: GROQ (primary) → GEMINI (fallback) ===
            string textResult;
            string providerUsed;
            string modelUsed;
            var warnings = new List<string>();
            using var client = _httpClientFactory.CreateClient("AiOcr");

            var groqKey = _config["GroqApiKey"];
            var geminiKey = _config["GeminiApiKey"];
            var groqVisionModel = _config["Groq:VisionModel"];
            if (string.IsNullOrWhiteSpace(groqVisionModel))
                groqVisionModel = _config["GroqModel"];
            if (string.IsNullOrWhiteSpace(groqVisionModel))
                groqVisionModel = DefaultGroqVisionModel;

            if (isImage && file.Length <= groqImageMaxBytes && !string.IsNullOrEmpty(groqKey))
            {
                // --- GROQ (OpenAI-compatible, 30 RPM free) ---
                var groqBody = new
                {
                    model = groqVisionModel,
                    messages = new object[]
                    {
                        new
                        {
                            role = "user",
                            content = new object[]
                            {
                                new { type = "text", text = ocrPrompt },
                                new { type = "image_url", image_url = new { url = $"data:{mimeType};base64,{base64Document}" } }
                            }
                        }
                    },
                    temperature = 0.1,
                    max_tokens = 4096
                };

                var groqBodyJson = JsonSerializer.Serialize(groqBody);
                var groqResponse = await SendOcrWithBoundedRetryAsync(
                    client,
                    () =>
                    {
                        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
                        request.Headers.Add("Authorization", $"Bearer {groqKey}");
                        request.Content = new StringContent(groqBodyJson, Encoding.UTF8, "application/json");
                        return request;
                    },
                    HttpContext.RequestAborted);

                if (groqResponse.IsSuccessStatusCode)
                {
                    if (TryExtractGroqDocumentText(groqResponse.Body, out var groqText)
                        && TryNormalizeLegacyDocumentJson(groqText, out textResult))
                    {
                        providerUsed = "Groq";
                        modelUsed = groqVisionModel;
                        _logger.LogInformation("[Đọc chứng từ dự phòng] Groq OK — model: {GroqVisionModel}", groqVisionModel);
                    }
                    else if (!string.IsNullOrEmpty(geminiKey))
                    {
                        _logger.LogWarning("[Đọc chứng từ dự phòng] Groq returned an invalid document payload; falling back to Gemini.");
                        (textResult, providerUsed, modelUsed) = await CallGeminiOcr(client, geminiKey, ocrPrompt, mimeType, base64Document, HttpContext.RequestAborted);
                        warnings.Add("Groq không trả về dữ liệu hợp lệ; hệ thống đã chuyển sang Gemini và vẫn có kết quả để bạn kiểm tra.");
                    }
                    else
                    {
                        _logger.LogWarning("[Đọc chứng từ dự phòng] Groq returned an invalid document payload and no fallback provider is configured.");
                        return BadRequest(new
                        {
                            error = "Dịch vụ đọc chứng từ trả về dữ liệu không hợp lệ. Vui lòng thử lại hoặc nhập chứng từ theo cách khác.",
                            guidance = DocumentIntakeManualFallbackGuidance,
                            code = "DOCUMENT_READER_INVALID_RESPONSE"
                        });
                    }
                }
                else if (!string.IsNullOrEmpty(geminiKey))
                {
                    // Groq failed → fallback to Gemini
                    _logger.LogWarning("[Đọc chứng từ dự phòng] Groq failed ({StatusCode}), falling back to Gemini...", (int)groqResponse.StatusCode);
                    (textResult, providerUsed, modelUsed) = await CallGeminiOcr(client, geminiKey, ocrPrompt, mimeType, base64Document, HttpContext.RequestAborted);
                    warnings.Add("Groq đang tạm thời không khả dụng; hệ thống đã chuyển sang Gemini và vẫn có kết quả để bạn kiểm tra.");
                }
                else
                {
                    var code = (int)groqResponse.StatusCode;
                    if (code == 429) return BadRequest("Dịch vụ đọc chứng từ đã hết lượt tạm thời. Vui lòng thử lại sau 1 phút.");
                    _logger.LogError("[Đọc chứng từ dự phòng] Groq Error ({StatusCode}).", code);
                    return BadRequest("Lỗi dịch vụ đọc chứng từ dự phòng. Vui lòng thử lại sau.");
                }
            }
            else if (!string.IsNullOrEmpty(geminiKey))
            {
                // No Groq key, non-image PDF, or file too large for Groq image payload → use Gemini directly.
                (textResult, providerUsed, modelUsed) = await CallGeminiOcr(client, geminiKey, ocrPrompt, mimeType, base64Document, HttpContext.RequestAborted);
            }
            else if (isImage && file.Length > groqImageMaxBytes)
            {
                return BadRequest("Ảnh chứng từ quá lớn cho Groq. Vui lòng chọn ảnh ≤ 4MB hoặc cấu hình Gemini để đọc file lớn/PDF.");
            }
            else
            {
                return BadRequest(new
                {
                    error = "Chưa có dịch vụ đọc chứng từ dự phòng khả dụng.",
                    guidance = DocumentIntakeManualFallbackGuidance,
                    code = "DOCUMENT_READER_PROVIDER_UNAVAILABLE"
                });
            }

            if (!TryNormalizeLegacyDocumentJson(textResult, out textResult))
            {
                return BadRequest(new
                {
                    error = "Dịch vụ đọc chứng từ trả về dữ liệu không hợp lệ. Vui lòng thử lại hoặc nhập chứng từ theo cách khác.",
                    guidance = DocumentIntakeManualFallbackGuidance,
                    code = "DOCUMENT_READER_INVALID_RESPONSE"
                });
            }

            var imageUrl = await StoreLegacyReceiptDocumentAsync(file.FileName, documentBytes, HttpContext.RequestAborted);
            pendingDocumentPath = imageUrl;

            // === TỰ ĐỘNG MAP VẬT TƯ ===
            var mappedItems = new List<object>();
            var traceLines = new List<object>();
            MappedDocumentHeader? mappedHeader = null;
            try
            {
                using var doc = JsonDocument.Parse(textResult);
                mappedHeader = await MapLegacyDocumentHeaderAsync(doc.RootElement, HttpContext.RequestAborted);
                await ApplyDocumentHeaderScopeAsync(mappedHeader, warnings, HttpContext.RequestAborted);
                var lineElements = EnumerateLegacyLineElements(doc.RootElement).ToList();
                if (lineElements.Count > 0)
                {
                    var allowedOwnerIds = GetOwnerScopeClaimIds();
                    var availableItemsQuery = _db.Items.AsNoTracking().Where(item => item.IsActive);
                    if (allowedOwnerIds.Count > 0)
                    {
                        availableItemsQuery = availableItemsQuery.Where(item =>
                            !item.OwnerPartnerId.HasValue || allowedOwnerIds.Contains(item.OwnerPartnerId.Value));
                    }
                    var availableItems = await availableItemsQuery.ToListAsync(HttpContext.RequestAborted);
                    var defaultUomId = await _db.UnitsOfMeasure
                        .Where(u => u.IsActive)
                        .OrderBy(u => u.UomId)
                        .Select(u => u.UomId)
                        .FirstOrDefaultAsync();
                    var allUoms = await _db.UnitsOfMeasure.Where(u => u.IsActive).ToListAsync();
                    for (var lineIndex = 0; lineIndex < lineElements.Count; lineIndex++)
                    {
                        var el = lineElements[lineIndex];
                        var sourceLine = lineIndex + 1;
                        var code = ReadLegacyString(el, "ItemCode", "itemCode", "Sku", "SKU", "Mã vật tư");
                        var name = ReadLegacyString(el, "ItemName", "itemName", "ProductName", "Tên vật tư");
                        var price = ReadLegacyDecimal(el, "UnitPrice", "unitPrice", "Đơn giá") ?? 0m;
                        var qty = ReadLegacyDecimal(el, "Quantity", "quantity", "Qty", "Số lượng") ?? 1m;
                        var unitName = ReadLegacyString(el, "UnitName", "unitName", "Uom", "UOM", "ĐVT");
                        var lotNumber = ReadLegacyString(el, "LotNumber", "lotNumber", "Batch", "Số lô");
                        var manufacturingDate = ReadLegacyDate(el, "ManufacturingDate", "manufacturingDate", "MfgDate", "NSX", "Ngày SX");
                        var expiryDate = ReadLegacyDate(el, "ExpiryDate", "expiryDate", "ExpirationDate", "HSD", "Hạn SD");

                        // Khớp đơn vị tính từ tên đơn vị trên chứng từ.
                        int matchedUomId = defaultUomId;
                        if (!string.IsNullOrEmpty(unitName))
                        {
                            var unitLower = unitName.ToLower().Trim();
                            var matchedUom = allUoms.FirstOrDefault(u =>
                                u.UomCode.ToLower() == unitLower ||
                                u.UomName.ToLower() == unitLower ||
                                u.UomName.ToLower().Contains(unitLower) ||
                                unitLower.Contains(u.UomName.ToLower()) ||
                                u.UomCode.ToLower().Contains(unitLower) ||
                                unitLower.Contains(u.UomCode.ToLower())
                            );
                            if (matchedUom != null) matchedUomId = matchedUom.UomId;
                        }

                        if (!string.IsNullOrEmpty(code) || !string.IsNullOrEmpty(name))
                        {
                            var searchName = name ?? code;
                            // 1. Exact match by code or name
                            var existingItem = availableItems.FirstOrDefault(x =>
                                (code != null && string.Equals(x.ItemCode, code, StringComparison.OrdinalIgnoreCase))
                                || (name != null && string.Equals(x.ItemName, name, StringComparison.OrdinalIgnoreCase)));
                            // 2. Fuzzy match: Contains on name (handles typos like "bột" vs "bọt")
                            if (existingItem == null && !string.IsNullOrEmpty(name))
                            {
                                var nameLower = name.ToLower();
                                existingItem = availableItems.FirstOrDefault(x =>
                                    x.ItemName.ToLower().Contains(nameLower) ||
                                    nameLower.Contains(x.ItemName.ToLower()) ||
                                    nameLower.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                        .Where(w => w.Length >= 3)
                                        .Count(w => x.ItemName.ToLower().Contains(w)) >= 2
                                );
                            }
                            // 3. Fuzzy match by code fragments
                            if (existingItem == null && !string.IsNullOrEmpty(code))
                            {
                                var codeLower = code.ToLower();
                                existingItem = availableItems.FirstOrDefault(x =>
                                    x.ItemCode.ToLower().Contains(codeLower) || codeLower.Contains(x.ItemCode.ToLower()));
                            }
                            // 4. Không tự tạo mới master data từ chứng từ đọc máy.
                            if (existingItem == null)
                            {
                                warnings.Add($"Chưa khớp vật tư: {searchName ?? code ?? "dòng không tên"}. Vui lòng chọn thủ công.");
                                continue;
                            }
                            mappedItems.Add(new
                            {
                                LineNumber = sourceLine,
                                ItemId = existingItem.ItemId,
                                ItemCode = existingItem.ItemCode,
                                ItemName = existingItem.ItemName,
                                Quantity = qty,
                                UnitPrice = price,
                                UnitName = unitName,
                                LotNumber = lotNumber,
                                ManufacturingDate = manufacturingDate,
                                ExpiryDate = expiryDate,
                                BaseUomId = existingItem.BaseUomId,
                                TransactionUomId = matchedUomId,
                                IsNew = false,
                                IsMatched = true,
                                RequiresReview = false
                            });
                            traceLines.Add(new
                            {
                                sourceLine,
                                itemId = existingItem.ItemId,
                                matchKind = "Exact",
                                requiresReview = false,
                                confidence = 0.75m
                            });
                        }
                    }
                }
            }
            catch (Exception mapEx)
            {
                _logger.LogWarning(mapEx, "[Đọc chứng từ dự phòng] Mapping error");
                warnings.Add("Không map được một phần dữ liệu từ chứng từ dự phòng.");
            }

            var parseStatus = mappedItems.Count == 0
                ? "Failed"
                : warnings.Count == 0 ? "Success" : "Partial";
            var confidence = mappedItems.Count == 0
                ? 0m
                : warnings.Count == 0 ? 0.75m : 0.5m;

            var processingTimeMs = stopwatch.ElapsedMilliseconds > int.MaxValue
                ? int.MaxValue
                : (int)stopwatch.ElapsedMilliseconds;
            var fileHash = Convert.ToHexString(SHA256.HashData(documentBytes));
            var ocrLog = new AiOcrLog
            {
                ImageUrl = imageUrl,
                FileName = Path.GetFileName(file.FileName),
                FileSize = file.Length,
                OcrProvider = providerUsed,
                ModelVersion = modelUsed,
                RawJsonResponse = JsonSerializer.Serialize(new
                {
                    sourceDocumentId = imageUrl,
                    fileHashSha256 = fileHash
                }),
                ParsedData = JsonSerializer.Serialize(new
                {
                    schemaVersion = "WMS_OCR_TRACE_1",
                    parseStatus,
                    providerTaskId = (string?)null,
                    lines = traceLines
                }),
                ConfidenceScore = confidence,
                DetectedItems = mappedItems.Count,
                ProcessingTimeMs = processingTimeMs,
                Status = parseStatus switch
                {
                    "Success" => 1,
                    "Partial" => 2,
                    _ => 3
                },
                CreatedBy = User.Identity?.Name ?? "system",
                CreatedAt = VietnamNow
            };
            _db.Set<AiOcrLog>().Add(ocrLog);
            await _unitOfWork.SaveChangesAsync(HttpContext.RequestAborted);
            pendingDocumentPath = null;

            return Ok(new
            {
                data = System.Text.Json.JsonSerializer.Serialize(mappedItems),
                header = mappedHeader,
                rawText = textResult,
                provider = providerUsed,
                logId = ocrLog.AiOcrLogId,
                warnings,
                confidence,
                parseStatus
            });
        }
        catch (Exception ex)
        {
            TryDeletePendingReceiptDocument(pendingDocumentPath);
            // P1-R2-1: log chi tiết server-side, không leak lỗi hệ thống ra client.
            _logger.LogError(ex, "Fallback document parser failed");
            return BadRequest("Lỗi đọc chứng từ dự phòng. Vui lòng kiểm tra file và thử lại.");
        }
    }

    private static async Task<string> ComputeSha256Async(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static ReceiptPayload ExtractReceiptPayload(object source, string fileName)
    {
        var json = JsonSerializer.Serialize(source);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var data = root.TryGetProperty("data", out var dataElement) ? dataElement.GetString() : null;
        var lines = new List<JsonElement>();
        if (!string.IsNullOrWhiteSpace(data))
        {
            using var linesDocument = JsonDocument.Parse(data);
            if (linesDocument.RootElement.ValueKind == JsonValueKind.Array)
                lines.AddRange(linesDocument.RootElement.EnumerateArray().Select(line => line.Clone()));
        }

        JsonElement? header = null;
        if (root.TryGetProperty("header", out var headerElement) && headerElement.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            header = headerElement.Clone();

        var warnings = root.TryGetProperty("warnings", out var warningsElement) && warningsElement.ValueKind == JsonValueKind.Array
            ? warningsElement.EnumerateArray().Select(w => w.GetString()).Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w!).ToList()
            : new List<string>();

        return new ReceiptPayload(
            fileName,
            header,
            lines,
            root.TryGetProperty("provider", out var providerElement) ? providerElement.GetString() ?? "Đọc chứng từ" : "Đọc chứng từ",
            root.TryGetProperty("parseStatus", out var statusElement) ? statusElement.GetString() ?? "Partial" : "Partial",
            root.TryGetProperty("confidence", out var confidenceElement) && confidenceElement.TryGetDecimal(out var confidence) ? confidence : 0m,
            root.TryGetProperty("logId", out var logElement) && logElement.TryGetInt64(out var logId) ? logId : 0L,
            warnings);
    }

    private static string ExtractErrorText(IActionResult result)
    {
        if (result is BadRequestObjectResult badRequest)
        {
            if (badRequest.Value is string text)
                return text;
            var json = JsonSerializer.Serialize(badRequest.Value);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("error", out var error))
                return error.GetString() ?? json;
            return json;
        }

        return result is ObjectResult objectResult
            ? JsonSerializer.Serialize(objectResult.Value)
            : "Không đọc được chứng từ.";
    }

    private static string BuildDocumentBusinessKey(JsonElement? header, string contentHash)
    {
        var referenceNo = NormalizeDocumentKey(ReadHeaderString(header, "ReferenceNo", "referenceNo"));
        if (string.IsNullOrWhiteSpace(referenceNo))
            return $"hash:{contentHash}";

        var date = NormalizeDocumentDate(ReadHeaderString(header, "VoucherDate", "voucherDate"));
        var partner = NormalizeDocumentKey(ReadHeaderString(header, "PartnerCode", "partnerCode", "PartnerName", "partnerName", "PartnerId", "partnerId"));
        return $"{referenceNo}|{date}|{partner}";
    }

    private static (List<JsonElement> Lines, List<string> Warnings) DeduplicateDocumentLines(List<JsonElement> lines, string? referenceNo)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<JsonElement>();
        var warnings = new List<string>();
        foreach (var line in lines)
        {
            var key = BuildDocumentLineKey(line, referenceNo);
            if (seen.Add(key))
            {
                result.Add(line);
            }
            else
            {
                var itemCode = ReadLineString(line, "ItemCode", "itemCode", "SuggestedItemCode", "suggestedItemCode") ?? "dòng vật tư";
                warnings.Add($"Bỏ qua dòng trùng trong chứng từ {FormatDocumentLabel(referenceNo)}: {itemCode}.");
            }
        }
        return (result, warnings);
    }

    private static string BuildDocumentLineKey(JsonElement line, string? referenceNo)
    {
        var item = ReadLineString(line, "ItemId", "itemId", "ItemCode", "itemCode", "SuggestedItemCode", "suggestedItemCode");
        var lot = ReadLineString(line, "LotNumber", "lotNumber");
        var uom = ReadLineString(line, "TransactionUomId", "transactionUomId", "UnitName", "unitName");
        var mfg = NormalizeDocumentDate(ReadLineString(line, "ManufacturingDate", "manufacturingDate"));
        var exp = NormalizeDocumentDate(ReadLineString(line, "ExpiryDate", "expiryDate"));
        var qty = NormalizeDocumentKey(ReadLineString(line, "Quantity", "quantity"));
        return $"{NormalizeDocumentKey(referenceNo)}|{NormalizeDocumentKey(item)}|{NormalizeDocumentKey(lot)}|{NormalizeDocumentKey(uom)}|{mfg}|{exp}|{qty}";
    }

    private static bool IsReadyMappedLine(JsonElement line)
    {
        var hasItemId = !string.IsNullOrWhiteSpace(ReadLineString(line, "ItemId", "itemId"));
        var isMatched = ReadBool(line, "IsMatched", "isMatched") ?? hasItemId;
        var requiresReview = ReadBool(line, "RequiresReview", "requiresReview") ?? false;
        return hasItemId && isMatched && !requiresReview;
    }

    private static bool? ReadBool(JsonElement line, params string[] names)
    {
        foreach (var name in names)
        {
            if (!line.TryGetProperty(name, out var value))
                continue;
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
            if (bool.TryParse(value.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static string? ReadHeaderString(JsonElement? header, params string[] names)
        => header.HasValue ? ReadElementString(header.Value, names) : null;

    private static string? ReadLineString(JsonElement line, params string[] names)
        => ReadElementString(line, names);

    private static string? ReadElementString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value))
            {
                if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    return null;
                return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
            }
        }
        return null;
    }

    private static string NormalizeDocumentKey(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? ""
            : Regex.Replace(value.Trim().ToUpperInvariant(), @"\s+", " ");

    private static string NormalizeDocumentDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            || DateTime.TryParse(value, CultureInfo.GetCultureInfo("vi-VN"), DateTimeStyles.AssumeLocal, out parsed))
            return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return value.Trim();
    }

    private static string FormatDocumentLabel(string? referenceNo)
        => string.IsNullOrWhiteSpace(referenceNo) ? "chưa có số chứng từ" : referenceNo.Trim();

    private sealed record ReceiptPayload(
        string FileName,
        JsonElement? Header,
        List<JsonElement> Lines,
        string Provider,
        string ParseStatus,
        decimal Confidence,
        long LogId,
        List<string> Warnings);

    private sealed class BatchDocumentGroup
    {
        public string DocumentKey { get; set; } = "";
        public string? ReferenceNo { get; set; }
        public string VoucherDate { get; set; } = "";
        public string? PartnerName { get; set; }
        public JsonElement? Header { get; set; }
        public List<JsonElement> Lines { get; set; } = new();
        public List<string> SourceFiles { get; set; } = new();
        public List<string> DuplicateDocumentFiles { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public string Provider { get; set; } = "";
        public string ParseStatus { get; set; } = "Partial";
        public decimal Confidence { get; set; }
        public long SourceLogId { get; set; }
    }

    private static string ResolveLegacyDocumentMimeType(string? contentType, string extension)
    {
        if (!string.IsNullOrWhiteSpace(contentType)
            && !string.Equals(contentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return contentType;
        }

        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream"
        };
    }

    private static bool TryExtractGroqDocumentText(string responsePayload, out string text)
    {
        text = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(responsePayload);
            if (!document.RootElement.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                return false;
            }

            var choice = choices[0];
            if (!choice.TryGetProperty("message", out var message)
                || message.ValueKind != JsonValueKind.Object
                || !message.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            text = content.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(text);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryExtractGeminiDocumentText(string responsePayload, out string text)
    {
        text = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(responsePayload);
            if (!document.RootElement.TryGetProperty("candidates", out var candidates)
                || candidates.ValueKind != JsonValueKind.Array
                || candidates.GetArrayLength() == 0)
            {
                return false;
            }

            var candidate = candidates[0];
            if (!candidate.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.Object
                || !content.TryGetProperty("parts", out var parts)
                || parts.ValueKind != JsonValueKind.Array
                || parts.GetArrayLength() == 0)
            {
                return false;
            }

            foreach (var part in parts.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.Object
                    && part.TryGetProperty("text", out var textElement)
                    && textElement.ValueKind == JsonValueKind.String)
                {
                    text = textElement.GetString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(text))
                        return true;
                }
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryNormalizeLegacyDocumentJson(string? providerText, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(providerText))
            return false;

        var candidate = providerText.Trim();
        if (candidate.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = candidate.IndexOf('\n');
            if (firstNewline < 0)
                return false;
            candidate = candidate[(firstNewline + 1)..].Trim();
            if (candidate.EndsWith("```", StringComparison.Ordinal))
                candidate = candidate[..^3].Trim();
        }

        try
        {
            using var document = JsonDocument.Parse(candidate);
            if (document.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                return false;
            if (!EnumerateLegacyLineElements(document.RootElement).Any(line => line.ValueKind == JsonValueKind.Object))
                return false;

            normalized = candidate;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IEnumerable<JsonElement> EnumerateLegacyLineElements(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in root.EnumerateArray())
                yield return element;
            yield break;
        }

        if (root.ValueKind != JsonValueKind.Object)
            yield break;

        foreach (var propertyName in new[] { "Lines", "lines", "Items", "items", "Details", "details" })
        {
            if (!root.TryGetProperty(propertyName, out var lines) || lines.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var element in lines.EnumerateArray())
                yield return element;
            yield break;
        }
    }

    private async Task<MappedDocumentHeader?> MapLegacyDocumentHeaderAsync(JsonElement root, CancellationToken cancellationToken)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        var headerElement = root;
        foreach (var propertyName in new[] { "Header", "header", "DocumentHeader", "documentHeader" })
        {
            if (root.TryGetProperty(propertyName, out var nestedHeader) && nestedHeader.ValueKind == JsonValueKind.Object)
            {
                headerElement = nestedHeader;
                break;
            }
        }

        var header = new MappedDocumentHeader
        {
            ReferenceNo = ReadLegacyString(headerElement, "ReferenceNo", "referenceNo", "DocumentNo", "DocumentNumber", "InvoiceNo", "AsnCode", "OrderNo"),
            VoucherDate = ReadLegacyDate(headerElement, "VoucherDate", "voucherDate", "DocumentDate", "InvoiceDate", "DeliveryDate"),
            PartnerCode = ReadLegacyString(headerElement, "PartnerCode", "SupplierCode", "CustomerCode", "VendorCode"),
            PartnerName = ReadLegacyString(headerElement, "PartnerName", "SupplierName", "CustomerName", "VendorName", "SourceName", "DestinationName"),
            WarehouseCode = ReadLegacyString(headerElement, "WarehouseCode", "FacilityCode", "ReceivingWarehouseCode", "ShippingWarehouseCode"),
            WarehouseName = ReadLegacyString(headerElement, "WarehouseName", "FacilityName", "ReceivingWarehouseName", "ShippingWarehouseName"),
            InventoryOwnershipMode = NormalizeLegacyOwnershipMode(ReadLegacyString(headerElement, "InventoryOwnershipMode", "OwnershipMode", "OwnershipType")),
            OwnerPartnerCode = ReadLegacyString(headerElement, "OwnerPartnerCode", "OwnerCode", "ClientCode"),
            OwnerPartnerName = ReadLegacyString(headerElement, "OwnerPartnerName", "OwnerName", "ClientName"),
            CarrierName = ReadLegacyString(headerElement, "CarrierName", "TransportCompany", "Carrier", "Transporter"),
            VehicleNumber = ReadLegacyString(headerElement, "VehicleNumber", "TruckNumber", "VehicleNo", "LicensePlate"),
            DriverName = ReadLegacyString(headerElement, "DriverName", "DelivererName", "ReceiverName"),
            DriverPhone = ReadLegacyString(headerElement, "DriverPhone", "Phone", "Mobile"),
            DockDoor = ReadLegacyString(headerElement, "DockDoor", "Door", "Gate"),
            Description = ReadLegacyString(headerElement, "Description", "Note", "Remark", "Comments")
        };

        if (!HasLegacyHeaderSignal(header))
            return null;

        var partners = await _db.Partners.AsNoTracking()
            .Where(partner => partner.IsActive)
            .Select(partner => new
            {
                partner.PartnerId,
                partner.PartnerCode,
                partner.PartnerName,
                partner.IsThreePlClient
            })
            .ToListAsync(cancellationToken);

        var partner = MatchLegacyByCodeOrName(partners, header.PartnerCode, header.PartnerName, x => x.PartnerCode, x => x.PartnerName);
        if (partner != null)
        {
            header.PartnerId = partner.PartnerId;
            header.PartnerCode = partner.PartnerCode;
            header.PartnerName = partner.PartnerName;
        }
        else if (!string.IsNullOrWhiteSpace(header.PartnerCode) || !string.IsNullOrWhiteSpace(header.PartnerName))
        {
            header.Warnings.Add("Chưa khớp được nhà cung cấp/khách hàng từ header chứng từ.");
        }

        var owner = MatchLegacyByCodeOrName(
            partners.Where(partner => partner.IsThreePlClient).ToList(),
            header.OwnerPartnerCode,
            header.OwnerPartnerName,
            x => x.PartnerCode,
            x => x.PartnerName);
        if (owner != null)
        {
            header.InventoryOwnershipMode = "ThreePl";
            header.OwnerPartnerId = owner.PartnerId;
            header.OwnerPartnerCode = owner.PartnerCode;
            header.OwnerPartnerName = owner.PartnerName;
        }
        else if (!string.IsNullOrWhiteSpace(header.OwnerPartnerCode) || !string.IsNullOrWhiteSpace(header.OwnerPartnerName))
        {
            header.Warnings.Add("Chưa khớp được chủ hàng từ header chứng từ.");
        }

        var warehouses = await _db.Warehouses.AsNoTracking()
            .Where(warehouse => warehouse.IsActive)
            .Select(warehouse => new
            {
                warehouse.WarehouseId,
                warehouse.WarehouseCode,
                warehouse.WarehouseName
            })
            .ToListAsync(cancellationToken);

        var warehouse = MatchLegacyByCodeOrName(warehouses, header.WarehouseCode, header.WarehouseName, x => x.WarehouseCode, x => x.WarehouseName);
        if (warehouse != null)
        {
            header.WarehouseId = warehouse.WarehouseId;
            header.WarehouseCode = warehouse.WarehouseCode;
            header.WarehouseName = warehouse.WarehouseName;
        }
        else if (!string.IsNullOrWhiteSpace(header.WarehouseCode) || !string.IsNullOrWhiteSpace(header.WarehouseName))
        {
            header.Warnings.Add("Chưa khớp được kho từ header chứng từ.");
        }

        var populated = new object?[]
        {
            header.ReferenceNo,
            header.VoucherDate,
            header.PartnerId ?? (object?)header.PartnerName,
            header.WarehouseId ?? (object?)header.WarehouseName,
            header.OwnerPartnerId ?? (object?)header.OwnerPartnerName,
            header.CarrierName,
            header.VehicleNumber,
            header.DriverName,
            header.DriverPhone,
            header.DockDoor,
            header.Description
        }.Count(value => value != null && !string.IsNullOrWhiteSpace(value.ToString()));
        header.Confidence = Math.Round(Math.Min(1m, populated / 8m), 4, MidpointRounding.AwayFromZero);
        return header;
    }

    private static bool HasLegacyHeaderSignal(MappedDocumentHeader header)
        => new[]
        {
            header.ReferenceNo,
            header.VoucherDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            header.PartnerCode,
            header.PartnerName,
            header.WarehouseCode,
            header.WarehouseName,
            header.InventoryOwnershipMode,
            header.OwnerPartnerCode,
            header.OwnerPartnerName,
            header.CarrierName,
            header.VehicleNumber,
            header.DriverName,
            header.DriverPhone,
            header.DockDoor,
            header.Description
        }.Any(value => !string.IsNullOrWhiteSpace(value));

    private static string? ReadLegacyString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                continue;

            return value.ValueKind == JsonValueKind.String
                ? NullIfWhiteSpace(value.GetString())
                : NullIfWhiteSpace(value.GetRawText().Trim('"'));
        }

        return null;
    }

    private static decimal? ReadLegacyDecimal(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                continue;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
                return number;

            var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var normalized = text.Trim().Replace(" ", "", StringComparison.Ordinal);
            if (decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.GetCultureInfo("vi-VN"), out var vi))
                return vi;
            if (decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var invariant))
                return invariant;
        }

        return null;
    }

    private static DateTime? ReadLegacyDate(JsonElement element, params string[] names)
    {
        var text = ReadLegacyString(element, names);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var formats = new[] { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "MM/dd/yyyy", "M/d/yyyy" };
        if (DateTime.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
            return exact.Date;
        if (DateTime.TryParse(text.Trim(), CultureInfo.GetCultureInfo("vi-VN"), DateTimeStyles.None, out var parsed))
            return parsed.Date;
        return null;
    }

    private static string? NormalizeLegacyOwnershipMode(string? value)
    {
        var normalized = NormalizeLegacyKey(value);
        if (normalized.Length == 0)
            return null;
        if (normalized.Contains("3pl", StringComparison.Ordinal) || normalized.Contains("khachhangthuekho", StringComparison.Ordinal) || normalized.Contains("thuekho", StringComparison.Ordinal))
            return "ThreePl";
        if (normalized.Contains("noibo", StringComparison.Ordinal) || normalized.Contains("internal", StringComparison.Ordinal))
            return "Internal";
        return value;
    }

    private static T? MatchLegacyByCodeOrName<T>(
        IReadOnlyCollection<T> source,
        string? code,
        string? name,
        Func<T, string?> codeSelector,
        Func<T, string?> nameSelector)
        where T : class
    {
        var normalizedCode = NormalizeLegacyKey(code);
        var normalizedName = NormalizeLegacyKey(name);

        if (normalizedCode.Length > 0)
        {
            var exactCode = source.FirstOrDefault(item => string.Equals(NormalizeLegacyKey(codeSelector(item)), normalizedCode, StringComparison.Ordinal));
            if (exactCode != null)
                return exactCode;
        }

        if (normalizedName.Length > 0)
        {
            var exactName = source.FirstOrDefault(item => string.Equals(NormalizeLegacyKey(nameSelector(item)), normalizedName, StringComparison.Ordinal));
            if (exactName != null)
                return exactName;

            return source.FirstOrDefault(item =>
            {
                var candidateCode = NormalizeLegacyKey(codeSelector(item));
                var candidateName = NormalizeLegacyKey(nameSelector(item));
                return (candidateCode.Length > 0 && normalizedName.Contains(candidateCode, StringComparison.Ordinal))
                    || (candidateName.Length > 0
                        && (candidateName.Contains(normalizedName, StringComparison.Ordinal)
                            || normalizedName.Contains(candidateName, StringComparison.Ordinal)));
            });
        }

        return null;
    }

    private static string NormalizeLegacyKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        }

        return Regex.Replace(builder.ToString().Normalize(NormalizationForm.FormC), "[^a-z0-9]+", "", RegexOptions.IgnoreCase);
    }

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();


    /// <summary>
    /// Helper: gọi Gemini API để đọc chứng từ dự phòng khi Groq không khả dụng.
    /// </summary>
    private async Task<(string textResult, string provider, string model)> CallGeminiOcr(
        HttpClient client,
        string apiKey,
        string prompt,
        string mimeType,
        string base64Image,
        CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = prompt },
                        new { inlineData = new { mimeType = mimeType, data = base64Image } }
                    }
                }
            }
        };

        var requestBodyJson = JsonSerializer.Serialize(requestBody);
        var response = await SendOcrWithBoundedRetryAsync(
            client,
            () =>
            {
                var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"https://generativelanguage.googleapis.com/v1beta/models/{DefaultGeminiVisionModel}:generateContent");
                request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
                request.Content = new StringContent(requestBodyJson, Encoding.UTF8, "application/json");
                return request;
            },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var code = (int)response.StatusCode;
            var msg = code == 429
                ? "Cả Groq và Gemini đều hết quota. Vui lòng thử lại sau 1 phút."
                : "Lỗi dịch vụ đọc chứng từ dự phòng. Vui lòng thử lại sau.";
            _logger.LogError("[Đọc chứng từ dự phòng] Gemini Error ({StatusCode}).", code);
            throw new BusinessRuleException(msg, code: "DOCUMENT_READ_ERROR", entityName: "AI");
        }

        if (!TryExtractGeminiDocumentText(response.Body, out var providerText)
            || !TryNormalizeLegacyDocumentJson(providerText, out var text))
        {
            throw new BusinessRuleException(
                "Dịch vụ đọc chứng từ trả về dữ liệu không hợp lệ. Vui lòng thử lại hoặc nhập chứng từ theo cách khác.",
                code: "DOCUMENT_READER_INVALID_RESPONSE",
                entityName: "AI");
        }

        _logger.LogInformation("[Đọc chứng từ dự phòng] Gemini OK (fallback)");
        return (text, "Gemini", DefaultGeminiVisionModel);
    }

    private async Task<OcrHttpResult> SendOcrWithBoundedRetryAsync(
        HttpClient client,
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 2;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var request = requestFactory();
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = new OcrHttpResult(response.StatusCode, body);

                if (result.IsSuccessStatusCode || !IsTransientOcrStatus(response.StatusCode) || attempt == maxAttempts)
                    return result;

                var retryAfter = response.Headers.RetryAfter?.Delta;
                if (!retryAfter.HasValue && response.Headers.RetryAfter?.Date is DateTimeOffset retryDate)
                    retryAfter = retryDate - VietnamTime.UtcNowOffset;

                var delay = BoundOcrRetryDelay(retryAfter, attempt);
                _logger.LogWarning(
                    "[Đọc chứng từ dự phòng] Provider trả HTTP {StatusCode}; thử lại lần {NextAttempt}/{MaxAttempts} sau {DelayMs} ms.",
                    (int)response.StatusCode,
                    attempt + 1,
                    maxAttempts,
                    (int)delay.TotalMilliseconds);
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt == maxAttempts)
                    return new OcrHttpResult(HttpStatusCode.ServiceUnavailable, "");

                var delay = BoundOcrRetryDelay(null, attempt);
                _logger.LogWarning(
                    "[Đọc chứng từ dự phòng] Provider hết thời gian phản hồi; thử lại lần {NextAttempt}/{MaxAttempts} sau {DelayMs} ms.",
                    attempt + 1,
                    maxAttempts,
                    (int)delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                if (attempt == maxAttempts)
                    return new OcrHttpResult(HttpStatusCode.ServiceUnavailable, "");

                var delay = BoundOcrRetryDelay(null, attempt);
                _logger.LogWarning(
                    "[Đọc chứng từ dự phòng] Provider gặp lỗi kết nối {ExceptionType}; thử lại lần {NextAttempt}/{MaxAttempts} sau {DelayMs} ms.",
                    ex.GetType().Name,
                    attempt + 1,
                    maxAttempts,
                    (int)delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
        }

        throw new InvalidOperationException("Vòng lặp retry OCR kết thúc ngoài dự kiến.");
    }

    private static TimeSpan BoundOcrRetryDelay(TimeSpan? retryAfter, int attempt)
    {
        var delay = retryAfter.GetValueOrDefault(TimeSpan.FromMilliseconds(250 * attempt));
        if (delay < TimeSpan.Zero)
            return TimeSpan.Zero;
        return delay > TimeSpan.FromSeconds(2) ? TimeSpan.FromSeconds(2) : delay;
    }

    private static bool IsTransientOcrStatus(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            || (int)statusCode >= 500;

    private sealed record OcrHttpResult(HttpStatusCode StatusCode, string Body)
    {
        public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and <= 299;
    }

    // Legacy storage contract: Path.Combine("App_Data", "uploads", "document-intake-legacy", fileName).
    private async Task<string> StoreLegacyReceiptDocumentAsync(string originalFileName, byte[] imageBytes, CancellationToken cancellationToken)
        => await _voucherImportQueryService.StoreLegacyReceiptDocumentAsync(originalFileName, imageBytes, cancellationToken);

    private void TryDeletePendingReceiptDocument(string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
            return;

        try
        {
            var physicalPath = ResolvePrivateReceiptPath(storedPath);
            if (System.IO.File.Exists(physicalPath))
                System.IO.File.Delete(physicalPath);
        }
        catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogWarning(cleanupException, "Không thể dọn file chứng từ dự phòng chưa được ghi nhận: {FileName}", Path.GetFileName(storedPath));
        }
    }

    private static string NormalizePrivateFileStem(string? value)
    {
        var source = string.IsNullOrWhiteSpace(value) ? "document" : value.Trim();
        var chars = source
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-')
            .ToArray();
        var normalized = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "document" : normalized[..Math.Min(normalized.Length, 80)];
    }

    private string ResolvePrivateReceiptPath(string storedPath)
        => _voucherImportQueryService.ResolvePrivateReceiptPath(storedPath);

    private string ResolveContentType(string physicalPath, string? storedContentType = null)
        => _voucherImportQueryService.ResolveContentType(physicalPath, storedContentType);

    [HttpGet]
    [Authorize(Roles = "Admin,Manager,Staff,InboundStaff,OutboundStaff,InventoryStaff,TransportStaff")]
    public async Task<IActionResult> DownloadReceiptDocument(long logId)
    {
        var log = await _db.Set<AiOcrLog>()
            .Include(x => x.Voucher)
            .FirstOrDefaultAsync(x => x.AiOcrLogId == logId);
        if (log == null)
            return NotFound();

        var scopedWarehouseId = GetScopedWarehouseId();
        if (log.Voucher != null && scopedWarehouseId.HasValue && log.Voucher.WarehouseId != scopedWarehouseId.Value)
            return Forbid();

        var currentUser = User.Identity?.Name ?? "";
        var allowedOwnerIds = User.IsInRole("Admin") ? new List<int>() : GetOwnerScopeClaimIds();
        if (log.Voucher != null
            && allowedOwnerIds.Count > 0
            && (!log.Voucher.OwnerPartnerId.HasValue || !allowedOwnerIds.Contains(log.Voucher.OwnerPartnerId.Value)))
        {
            return Forbid();
        }

        if (log.Voucher == null
            && !(User.IsInRole("Admin") || (User.IsInRole("Manager") && allowedOwnerIds.Count == 0))
            && !string.Equals(log.CreatedBy, currentUser, StringComparison.OrdinalIgnoreCase))
        {
            return Forbid();
        }

        string physicalPath;
        try
        {
            physicalPath = ResolvePrivateReceiptPath(log.ImageUrl);
        }
        catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogWarning(ex, "Đường dẫn chứng từ không hợp lệ cho log {LogId}.", logId);
            return NotFound();
        }

        if (!System.IO.File.Exists(physicalPath))
            return NotFound();

        var downloadName = string.IsNullOrWhiteSpace(log.FileName)
            ? Path.GetFileName(physicalPath)
            : Path.GetFileName(log.FileName);
        return PhysicalFile(physicalPath, ResolveContentType(physicalPath), downloadName);
    }


    [HttpGet]
    [Authorize(Roles = "Admin,Manager,Staff,InboundStaff,OutboundStaff,InventoryStaff,TransportStaff")]
    public async Task<IActionResult> DownloadSampleImport100()
    {
        // Generate a 100-row sample file using current master data where available.
        var allowedOwnerIds = GetOwnerScopeClaimIds();
        var itemQuery = _db.Items
            .AsNoTracking()
            .Include(i => i.BaseUom)
            .Where(i => i.IsActive);
        if (allowedOwnerIds.Count > 0)
        {
            itemQuery = itemQuery.Where(item =>
                !item.OwnerPartnerId.HasValue || allowedOwnerIds.Contains(item.OwnerPartnerId.Value));
        }

        var items = await itemQuery
            .OrderBy(i => i.ItemCode)
            .Take(300)
            .ToListAsync();

        var scopedWarehouseId = GetScopedWarehouseId();
        var locationQuery = _db.Locations
            .AsNoTracking()
            .Where(l => l.IsActive)
            .Where(l => !scopedWarehouseId.HasValue || l.Zone.WarehouseId == scopedWarehouseId.Value);
        var locations = await locationQuery
            .OrderBy(l => l.LocationCode)
            .Take(300)
            .Select(l => l.LocationCode)
            .ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("ImportLines");

        // Header
        ws.Cell(1, 1).Value = "ItemCode";
        ws.Cell(1, 2).Value = "ItemName";
        ws.Cell(1, 3).Value = "Quantity";
        ws.Cell(1, 4).Value = "UnitPrice";
        ws.Cell(1, 5).Value = "UnitName";
        ws.Cell(1, 6).Value = "LocationCode";
        ws.Cell(1, 7).Value = "ExpiryDate (yyyy-MM-dd)";
        ws.Cell(1, 8).Value = "LotNumber";
        ws.Cell(1, 9).Value = "DefectQty";
        ws.Cell(1, 10).Value = "Notes";

        ws.Range(1, 1, 1, 10).Style.Font.Bold = true;
        ws.Range(1, 1, 1, 10).Style.Fill.BackgroundColor = XLColor.FromHtml("#111827");
        ws.Range(1, 1, 1, 10).Style.Font.FontColor = XLColor.White;

        var rng = new Random();
        for (int i = 0; i < 100; i++)
        {
            var row = i + 2;
            var it = items.Count > 0 ? items[rng.Next(items.Count)] : null;
            var loc = locations.Count > 0 ? locations[rng.Next(locations.Count)] : "";

            var qty = rng.Next(1, 30);
            var defect = rng.NextDouble() < 0.10 ? rng.Next(1, Math.Min(3, qty + 1)) : 0; // ~10% rows have small defect
            var unitName = it?.BaseUom?.UomCode ?? "Pcs";
            var price = it != null ? it.UnitCost : 0;

            ws.Cell(row, 1).Value = it?.ItemCode ?? $"MAU-{(i + 1):D3}";
            ws.Cell(row, 2).Value = it?.ItemName ?? $"Vật tư mẫu {(i + 1):D3}";
            ws.Cell(row, 3).Value = qty;
            ws.Cell(row, 4).Value = price;
            ws.Cell(row, 5).Value = unitName;
            ws.Cell(row, 6).Value = loc;

            // Add expiry dates to some rows to show FEFO-compatible data.
            if (rng.NextDouble() < 0.35)
            {
                var days = rng.Next(5, 180);
                ws.Cell(row, 7).Value = VietnamNow.Date.AddDays(days).ToString("yyyy-MM-dd");
            }
            else
            {
                ws.Cell(row, 7).Value = "";
            }

            // Always include a LotNumber so sample import covers batch tracking.
            ws.Cell(row, 8).Value = $"LOT-{VietnamNow:yyMMdd}-{rng.Next(1000, 9999)}";
            ws.Cell(row, 9).Value = defect;
            ws.Cell(row, 10).Value = defect > 0 ? "Cần kiểm tra số lượng lỗi/thiếu" : "";
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "WMS_DanhSachVatTu_Mau_100dong.xlsx");
    }


    [HttpPost]
    [Authorize(Roles = "Admin,Manager,Staff,InboundStaff,OutboundStaff,InventoryStaff,TransportStaff")]
    [Authorize(Policy = WmsPermissions.VoucherCreate)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportLinesExcel(
        IFormFile file,
        int? warehouseId = null,
        int? ownerPartnerId = null,
        VoucherTypeEnum? voucherType = null)
    {
        if (file == null || file.Length == 0)
            return BadRequest("Vui lòng chọn file Excel.");
        if (file.Length > MaxVoucherImportBytes)
            return BadRequest("File quá lớn. Vui lòng chọn file không vượt quá 5 MB.");

        var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant() ?? "";
        if (extension != ".xlsx")
            return BadRequest("Định dạng không hợp lệ. Chỉ hỗ trợ file .xlsx.");
        if (!SecurityHelpers.FileUpload.IsContentTypeCompatible(file.FileName, file.ContentType))
            return BadRequest("MIME của file Excel không phù hợp với định dạng .xlsx.");

        try
        {
            await using (var probeStream = file.OpenReadStream())
            {
                if (!SecurityHelpers.FileUpload.HasExpectedFileSignature(file.FileName, probeStream))
                    return BadRequest("Nội dung file không phải là gói Excel OpenXML hợp lệ.");
            }

            using var memory = new MemoryStream();
            await file.CopyToAsync(memory, HttpContext.RequestAborted);
            memory.Position = 0;
            var fileHash = Convert.ToHexString(SHA256.HashData(memory.ToArray()));

            using var workbook = new XLWorkbook(memory);
            var errors = new List<ExcelImportError>();
            var warnings = new List<string>();

            var worksheet = workbook.Worksheets.FirstOrDefault(sheet =>
                string.Equals(sheet.Name, VoucherImportWorksheetName, StringComparison.OrdinalIgnoreCase));
            if (worksheet == null)
            {
                errors.Add(new ExcelImportError(
                    0,
                    "Worksheet",
                    "WORKSHEET_MISSING",
                    $"Không tìm thấy worksheet '{VoucherImportWorksheetName}'.",
                    "Tải file mẫu mới từ hệ thống và giữ nguyên tên worksheet."));
                return ExcelImportValidationFailure(errors, warnings);
            }

            var metadataSheet = workbook.Worksheets.FirstOrDefault(sheet =>
                string.Equals(sheet.Name, VoucherImportMetadataWorksheetName, StringComparison.OrdinalIgnoreCase));
            if (metadataSheet == null)
            {
                warnings.Add("File dùng mẫu cũ chưa có metadata phiên bản; hệ thống vẫn kiểm tra đầy đủ cấu trúc trước khi cho xem trước.");
            }
            else
            {
                var templateType = ReadImportText(metadataSheet.Cell(2, 1));
                var templateVersion = ReadImportText(metadataSheet.Cell(2, 2));
                if (!string.Equals(templateType, "VoucherLines", StringComparison.Ordinal)
                    || !string.Equals(templateVersion, VoucherImportTemplateVersion, StringComparison.Ordinal))
                {
                    errors.Add(new ExcelImportError(
                        0,
                        "TemplateVersion",
                        "TEMPLATE_VERSION_UNSUPPORTED",
                        $"Phiên bản mẫu '{templateVersion}' không được hỗ trợ.",
                        "Tải lại file mẫu mới nhất từ màn hình tạo phiếu."));
                }
            }

            for (var column = 1; column <= VoucherImportHeaders.Length; column++)
            {
                var actual = ReadImportText(worksheet.Cell(1, column));
                var expected = VoucherImportHeaders[column - 1];
                if (string.IsNullOrWhiteSpace(actual))
                {
                    errors.Add(new ExcelImportError(
                        1,
                        expected,
                        "HEADER_MISSING",
                        $"Thiếu cột bắt buộc '{expected}'.",
                        "Giữ nguyên hàng tiêu đề của file mẫu."));
                }
                else if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(new ExcelImportError(
                        1,
                        expected,
                        "HEADER_INVALID",
                        $"Cột {column} phải là '{expected}', hiện đang là '{actual}'.",
                        "Đổi lại đúng tên và thứ tự cột theo file mẫu."));
                }
            }

            var lastUsedColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
            if (lastUsedColumn > VoucherImportHeaders.Length)
            {
                errors.Add(new ExcelImportError(
                    1,
                    "K+",
                    "HEADER_EXTRA",
                    $"File có {lastUsedColumn} cột, vượt quá {VoucherImportHeaders.Length} cột được hỗ trợ.",
                    "Xóa các cột ngoài cấu trúc file mẫu."));
            }

            if (errors.Count > 0)
                return ExcelImportValidationFailure(errors, warnings);

            var requestedWarehouseId = warehouseId.GetValueOrDefault() > 0 ? warehouseId : null;
            var scopedWarehouseId = GetScopedWarehouseId();
            if (scopedWarehouseId.HasValue
                && requestedWarehouseId.HasValue
                && scopedWarehouseId.Value != requestedWarehouseId.Value)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    error = "Kho được gửi lên nằm ngoài phạm vi được phân quyền.",
                    code = "WAREHOUSE_SCOPE_FORBIDDEN"
                });
            }

            var effectiveWarehouseId = scopedWarehouseId ?? requestedWarehouseId;
            if (!effectiveWarehouseId.HasValue)
            {
                errors.Add(new ExcelImportError(
                    0,
                    "WarehouseId",
                    "WAREHOUSE_REQUIRED",
                    "Chưa xác định kho áp dụng cho file nhập.",
                    "Chọn kho trên phiếu trước khi tải file Excel."));
                return ExcelImportValidationFailure(errors, warnings);
            }

            var warehouseExists = await _db.Warehouses
                .AsNoTracking()
                .AnyAsync(
                    warehouse => warehouse.WarehouseId == effectiveWarehouseId.Value && warehouse.IsActive,
                    HttpContext.RequestAborted);
            if (!warehouseExists)
            {
                errors.Add(new ExcelImportError(
                    0,
                    "WarehouseId",
                    "WAREHOUSE_INVALID",
                    "Kho không tồn tại hoặc đã ngừng hoạt động.",
                    "Chọn một kho đang hoạt động rồi nhập lại file."));
                return ExcelImportValidationFailure(errors, warnings);
            }

            var allowedOwnerIds = GetOwnerScopeClaimIds();
            if (allowedOwnerIds.Count > 0
                && (!ownerPartnerId.HasValue || !allowedOwnerIds.Contains(ownerPartnerId.Value)))
            {
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    error = "Chủ hàng nằm ngoài phạm vi được phân quyền.",
                    code = "OWNER_SCOPE_FORBIDDEN"
                });
            }

            if (ownerPartnerId.HasValue)
            {
                var ownerExists = await _db.Partners
                    .AsNoTracking()
                    .AnyAsync(
                        partner => partner.PartnerId == ownerPartnerId.Value
                            && partner.IsThreePlClient
                            && partner.IsActive,
                        HttpContext.RequestAborted);
                if (!ownerExists)
                {
                    errors.Add(new ExcelImportError(
                        0,
                        "OwnerPartnerId",
                        "OWNER_INVALID",
                        "Chủ hàng không tồn tại, không thuộc danh sách chủ hàng được quản lý hoặc đã ngừng hoạt động.",
                        "Chọn lại chủ hàng đang hoạt động trên phiếu."));
                    return ExcelImportValidationFailure(errors, warnings);
                }
            }

            var items = await _db.Items
                .AsNoTracking()
                .Where(item => item.IsActive
                    && (!item.OwnerPartnerId.HasValue || item.OwnerPartnerId == ownerPartnerId))
                .ToListAsync(HttpContext.RequestAborted);
            var itemByCode = items
                .GroupBy(item => NormalizeImportKey(item.ItemCode), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
            var itemByName = items
                .GroupBy(item => NormalizeImportKey(item.ItemName), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

            var units = await _db.UnitsOfMeasure
                .AsNoTracking()
                .Where(unit => unit.IsActive)
                .ToListAsync(HttpContext.RequestAborted);
            var conversions = await _db.UnitConversions
                .AsNoTracking()
                .Where(conversion => conversion.IsActive && conversion.ConversionRate > 0m)
                .ToListAsync(HttpContext.RequestAborted);

            var locations = await _db.Locations
                .AsNoTracking()
                .Include(location => location.Zone)
                .Where(location => location.IsActive
                    && location.Zone.WarehouseId == effectiveWarehouseId.Value)
                .ToListAsync(HttpContext.RequestAborted);
            var locationByCode = locations
                .GroupBy(location => NormalizeImportKey(location.LocationCode), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

            var lastUsedRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
            if (lastUsedRow - 1 > MaxVoucherImportRows)
            {
                errors.Add(new ExcelImportError(
                    0,
                    "Rows",
                    "ROW_LIMIT_EXCEEDED",
                    $"File có {lastUsedRow - 1} dòng dữ liệu, vượt giới hạn {MaxVoucherImportRows} dòng.",
                    $"Chia file thành các phần không quá {MaxVoucherImportRows} dòng."));
                return ExcelImportValidationFailure(errors, warnings);
            }

            var mappedItems = new List<object>();
            var duplicateKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var isInbound = voucherType.HasValue && IsInboundVoucherType(voucherType.Value);
            var canSeeFinancial = CanSeeFinancial();

            for (var row = 2; row <= lastUsedRow; row++)
            {
                var cells = Enumerable.Range(1, VoucherImportHeaders.Length)
                    .Select(column => worksheet.Cell(row, column))
                    .ToArray();
                var hasContent = cells.Any(cell => cell.HasFormula || !string.IsNullOrWhiteSpace(ReadImportText(cell)));
                if (!hasContent)
                    continue;

                var rowErrorCount = errors.Count;
                if (cells.Any(cell => cell.HasFormula))
                {
                    errors.Add(new ExcelImportError(
                        row,
                        "A:J",
                        "FORMULA_NOT_ALLOWED",
                        "Dòng dữ liệu không được chứa công thức.",
                        "Dán giá trị tĩnh vào file mẫu trước khi nhập."));
                    continue;
                }

                var itemCode = ReadImportText(cells[0]);
                var itemName = ReadImportText(cells[1]);
                var unitName = ReadImportText(cells[4]);
                var locationCode = ReadImportText(cells[5]);
                var lotNumber = ReadImportText(cells[7]).ToUpperInvariant();
                var notes = ReadImportText(cells[9]);

                Item? item = null;
                if (!string.IsNullOrWhiteSpace(itemCode))
                {
                    if (itemByCode.TryGetValue(NormalizeImportKey(itemCode), out var codeMatches) && codeMatches.Count == 1)
                        item = codeMatches[0];
                    else if (codeMatches is { Count: > 1 })
                        errors.Add(new ExcelImportError(row, "ItemCode", "ITEM_AMBIGUOUS", $"Mã vật tư '{itemCode}' bị trùng trong dữ liệu nền.", "Liên hệ quản trị viên để chuẩn hóa mã vật tư."));
                    else
                        errors.Add(new ExcelImportError(row, "ItemCode", "ITEM_NOT_FOUND_OR_OUT_OF_SCOPE", $"Không tìm thấy vật tư '{itemCode}' trong phạm vi chủ hàng hiện tại.", "Chọn mã vật tư đang hoạt động hoặc tạo dữ liệu nền trước khi nhập."));
                }
                else if (!string.IsNullOrWhiteSpace(itemName))
                {
                    if (itemByName.TryGetValue(NormalizeImportKey(itemName), out var nameMatches) && nameMatches.Count == 1)
                        item = nameMatches[0];
                    else if (nameMatches is { Count: > 1 })
                        errors.Add(new ExcelImportError(row, "ItemName", "ITEM_AMBIGUOUS", $"Tên vật tư '{itemName}' khớp nhiều mã.", "Điền ItemCode để xác định chính xác vật tư."));
                    else
                        errors.Add(new ExcelImportError(row, "ItemName", "ITEM_NOT_FOUND_OR_OUT_OF_SCOPE", $"Không tìm thấy vật tư '{itemName}' trong phạm vi chủ hàng hiện tại.", "Điền ItemCode của vật tư đang hoạt động."));
                }
                else
                {
                    errors.Add(new ExcelImportError(row, "ItemCode", "ITEM_REQUIRED", "Dòng chưa có mã hoặc tên vật tư.", "Điền ItemCode; chỉ dùng ItemName khi tên là duy nhất."));
                }

                if (item != null
                    && !string.IsNullOrWhiteSpace(itemName)
                    && !string.Equals(item.ItemName.Trim(), itemName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add($"Dòng {row}: tên trong file khác dữ liệu nền; hệ thống dùng tên chuẩn '{item.ItemName}' theo ItemCode.");
                }

                decimal quantity = 0m;
                if (!TryReadImportDecimal(cells[2], out quantity))
                    errors.Add(new ExcelImportError(row, "Quantity", "QUANTITY_INVALID", "Số lượng không phải số hợp lệ.", "Nhập số dương, tối đa 4 chữ số thập phân; không dùng ký hiệu khoa học hoặc dấu phân tách hàng nghìn."));
                else if (quantity <= 0m || quantity > MaxVoucherImportDecimal || GetDecimalScale(quantity) > 4)
                    errors.Add(new ExcelImportError(row, "Quantity", "QUANTITY_OUT_OF_RANGE", "Số lượng phải lớn hơn 0 và phù hợp decimal(18,4).", "Nhập số dương không quá 99.999.999.999.999,9999."));

                decimal unitPrice = 0m;
                if (!cells[3].IsEmpty())
                {
                    if (!TryReadImportDecimal(cells[3], out unitPrice)
                        || unitPrice < 0m
                        || unitPrice > MaxVoucherImportDecimal
                        || GetDecimalScale(unitPrice) > 4)
                    {
                        errors.Add(new ExcelImportError(row, "UnitPrice", "UNIT_PRICE_INVALID", "Đơn giá không hợp lệ.", "Nhập số không âm, tối đa 4 chữ số thập phân."));
                    }
                    else if (!canSeeFinancial && unitPrice != 0m)
                    {
                        errors.Add(new ExcelImportError(row, "UnitPrice", "FINANCIAL_SCOPE_FORBIDDEN", "Tài khoản hiện tại không được nhập dữ liệu đơn giá.", "Để trống/0 hoặc nhờ người có quyền tài chính thực hiện."));
                    }
                }

                decimal defectQuantity = 0m;
                if (!cells[8].IsEmpty())
                {
                    if (!TryReadImportDecimal(cells[8], out defectQuantity)
                        || defectQuantity < 0m
                        || defectQuantity > MaxVoucherImportDecimal
                        || GetDecimalScale(defectQuantity) > 4)
                    {
                        errors.Add(new ExcelImportError(row, "DefectQty", "DEFECT_QUANTITY_INVALID", "Số lượng lỗi không hợp lệ.", "Nhập số từ 0 đến số lượng của dòng, tối đa 4 chữ số thập phân."));
                    }
                    else if (quantity > 0m && defectQuantity > quantity)
                    {
                        errors.Add(new ExcelImportError(row, "DefectQty", "DEFECT_QUANTITY_EXCEEDS_QUANTITY", "Số lượng lỗi lớn hơn số lượng nhập.", "Giảm số lượng lỗi để không vượt số lượng của dòng."));
                    }
                }

                DateTime? expiryDate = null;
                if (!TryReadImportDate(cells[6], out expiryDate))
                {
                    errors.Add(new ExcelImportError(row, "ExpiryDate", "EXPIRY_DATE_INVALID", "Hạn sử dụng không đúng định dạng.", "Dùng ô ngày Excel hoặc định dạng yyyy-MM-dd / dd/MM/yyyy."));
                }

                if (lotNumber.Length > 50)
                    errors.Add(new ExcelImportError(row, "LotNumber", "LOT_TOO_LONG", "Số lô vượt quá 50 ký tự.", "Rút gọn số lô theo mã truy xuất được doanh nghiệp quy định."));
                if (notes.Length > 300)
                    errors.Add(new ExcelImportError(row, "Notes", "NOTES_TOO_LONG", "Ghi chú vượt quá 300 ký tự.", "Rút gọn ghi chú của dòng."));

                int? locationId = null;
                if (!string.IsNullOrWhiteSpace(locationCode))
                {
                    if (locationByCode.TryGetValue(NormalizeImportKey(locationCode), out var locationMatches) && locationMatches.Count == 1)
                        locationId = locationMatches[0].LocationId;
                    else if (locationMatches is { Count: > 1 })
                        errors.Add(new ExcelImportError(row, "LocationCode", "LOCATION_AMBIGUOUS", $"Vị trí '{locationCode}' không duy nhất trong kho.", "Liên hệ quản trị viên để chuẩn hóa mã vị trí."));
                    else
                        errors.Add(new ExcelImportError(row, "LocationCode", "LOCATION_NOT_FOUND_OR_OUT_OF_SCOPE", $"Vị trí '{locationCode}' không tồn tại trong kho đã chọn.", "Chọn vị trí đang hoạt động thuộc đúng kho hoặc để trống để gợi ý cất hàng."));
                }

                var transactionUomId = item?.BaseUomId ?? 0;
                decimal conversionRate = 1m;
                if (item != null && !string.IsNullOrWhiteSpace(unitName))
                {
                    var unitMatches = units
                        .Where(unit => string.Equals(unit.UomCode.Trim(), unitName.Trim(), StringComparison.OrdinalIgnoreCase)
                            || string.Equals(unit.UomName.Trim(), unitName.Trim(), StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (unitMatches.Count == 0)
                    {
                        errors.Add(new ExcelImportError(row, "UnitName", "UOM_NOT_FOUND", $"Đơn vị '{unitName}' không tồn tại hoặc đã ngừng hoạt động.", "Dùng đúng mã hoặc tên đơn vị tính trong Danh mục."));
                    }
                    else if (unitMatches.Count > 1)
                    {
                        errors.Add(new ExcelImportError(row, "UnitName", "UOM_AMBIGUOUS", $"Đơn vị '{unitName}' khớp nhiều dữ liệu.", "Dùng mã đơn vị tính để xác định chính xác."));
                    }
                    else
                    {
                        transactionUomId = unitMatches[0].UomId;
                        try
                        {
                            var resolvedRate = _voucherSharedRuleService.ResolveConversionRate(
                                conversions,
                                item.ItemId,
                                transactionUomId,
                                item.BaseUomId);
                            if (!resolvedRate.HasValue || resolvedRate.Value <= 0m)
                            {
                                errors.Add(new ExcelImportError(row, "UnitName", "UOM_CONVERSION_MISSING", $"Chưa có quy đổi từ '{unitMatches[0].UomCode}' về đơn vị tồn kho.", "Khai báo quy đổi đơn vị cho vật tư trước khi nhập."));
                            }
                            else
                            {
                                conversionRate = resolvedRate.Value;
                            }
                        }
                        catch (BusinessRuleException)
                        {
                            errors.Add(new ExcelImportError(row, "UnitName", "UOM_CONVERSION_AMBIGUOUS", "Có nhiều quy tắc quy đổi đơn vị xung đột.", "Chuẩn hóa quy tắc quy đổi trong Danh mục trước khi nhập."));
                        }
                    }
                }

                if (item != null && isInbound)
                {
                    if (item.TrackLot && string.IsNullOrWhiteSpace(lotNumber))
                        errors.Add(new ExcelImportError(row, "LotNumber", "LOT_REQUIRED", $"Vật tư '{item.ItemCode}' bắt buộc quản lý theo lô.", "Điền số lô trước khi áp dụng file."));
                    if (item.TrackExpiry && !expiryDate.HasValue)
                        errors.Add(new ExcelImportError(row, "ExpiryDate", "EXPIRY_REQUIRED", $"Vật tư '{item.ItemCode}' bắt buộc quản lý hạn sử dụng.", "Điền hạn sử dụng trước khi áp dụng file."));
                    if (item.TrackSerial)
                        warnings.Add($"Dòng {row}: vật tư '{item.ItemCode}' quản lý số sê-ri; nhân viên phải ghi nhận đủ số sê-ri ở bước tiếp nhận hàng.");
                }

                if (errors.Count > rowErrorCount || item == null)
                    continue;

                var duplicateKey = string.Join(
                    "|",
                    item.ItemId,
                    transactionUomId,
                    locationId?.ToString(CultureInfo.InvariantCulture) ?? "",
                    lotNumber,
                    expiryDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "");
                if (!duplicateKeys.Add(duplicateKey))
                {
                    errors.Add(new ExcelImportError(
                        row,
                        "ItemCode",
                        "DUPLICATE_ROW",
                        "Dòng trùng vật tư, đơn vị, vị trí, lô và hạn sử dụng với một dòng trước đó.",
                        "Gộp số lượng vào một dòng hoặc phân biệt đúng lô/vị trí."));
                    continue;
                }

                mappedItems.Add(new
                {
                    ItemId = item.ItemId,
                    ItemCode = item.ItemCode,
                    ItemName = item.ItemName,
                    Quantity = quantity,
                    UnitPrice = canSeeFinancial ? unitPrice : 0m,
                    BaseUomId = item.BaseUomId,
                    TransactionUomId = transactionUomId,
                    ConversionRate = conversionRate,
                    IsNew = false,
                    LocationId = locationId,
                    ExpiryDate = expiryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    LotNumber = string.IsNullOrWhiteSpace(lotNumber) ? null : lotNumber,
                    DefectQty = defectQuantity,
                    Notes = string.IsNullOrWhiteSpace(notes) ? null : notes,
                    item.TrackLot,
                    item.TrackExpiry,
                    item.TrackSerial
                });
            }

            if (mappedItems.Count == 0 && errors.Count == 0)
            {
                errors.Add(new ExcelImportError(
                    0,
                    "Rows",
                    "NO_DATA_ROWS",
                    "File không có dòng dữ liệu để xem trước.",
                    "Điền ít nhất một dòng dưới hàng tiêu đề."));
            }

            if (errors.Count > 0)
                return ExcelImportValidationFailure(errors, warnings);

            return Ok(new
            {
                data = JsonSerializer.Serialize(mappedItems),
                mode = "Preview",
                policy = "AllOrNothing",
                templateVersion = VoucherImportTemplateVersion,
                rowCount = mappedItems.Count,
                fileHashSha256 = fileHash,
                warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Excel import preview failed for {FileName}", Path.GetFileName(file.FileName));
            return BadRequest(new
            {
                error = "Không thể đọc file Excel. Vui lòng kiểm tra file không bị hỏng và đúng mẫu rồi thử lại.",
                code = "EXCEL_WORKBOOK_INVALID"
            });
        }
    }

    private BadRequestObjectResult ExcelImportValidationFailure(
        IReadOnlyCollection<ExcelImportError> errors,
        IReadOnlyCollection<string> warnings)
        => BadRequest(new
        {
            error = $"File có {errors.Count} lỗi nên chưa có dòng nào được áp dụng.",
            code = "EXCEL_IMPORT_VALIDATION_FAILED",
            mode = "Preview",
            policy = "AllOrNothing",
            errors,
            warnings
        });

    private static string ReadImportText(IXLCell cell)
        => cell.GetString().Trim();

    private static string NormalizeImportKey(string? value)
        => (value ?? "").Trim().ToUpperInvariant();

    private static bool TryReadImportDecimal(IXLCell cell, out decimal value)
    {
        value = 0m;
        if (cell.IsEmpty())
            return false;

        if (cell.DataType == XLDataType.Number && cell.TryGetValue<decimal>(out value))
            return true;

        var raw = cell.GetString().Trim();
        if (string.IsNullOrWhiteSpace(raw)
            || raw.Contains('e', StringComparison.OrdinalIgnoreCase)
            || raw.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var dotCount = raw.Count(character => character == '.');
        var commaCount = raw.Count(character => character == ',');
        if (dotCount > 1 || commaCount > 1 || (dotCount == 1 && commaCount == 1))
            return false;

        if (commaCount == 1)
            raw = raw.Replace(',', '.');

        return decimal.TryParse(
            raw,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static bool TryReadImportDate(IXLCell cell, out DateTime? value)
    {
        value = null;
        if (cell.IsEmpty())
            return true;

        if (cell.TryGetValue<DateTime>(out var cellDate))
        {
            value = cellDate.Date;
            return true;
        }

        var raw = cell.GetString().Trim();
        var formats = new[] { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy" };
        if (DateTime.TryParseExact(
            raw,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed))
        {
            value = parsed.Date;
            return true;
        }

        return false;
    }

    private static int GetDecimalScale(decimal value)
        => (decimal.GetBits(value)[3] >> 16) & 0x7F;

    private sealed record ExcelImportError(
        int Row,
        string Column,
        string Code,
        string Message,
        string Remediation);

}
