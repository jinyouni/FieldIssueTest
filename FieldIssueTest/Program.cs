using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// 정적 파일 및 HttpClient 등록
builder.Services.AddDirectoryBrowser();
builder.Services.AddHttpClient("GeminiClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// ---------------------------------------------------
// 1. 메모리 내 기존 설비 이슈 이력 데이터 (초기 3건)
// ---------------------------------------------------
var issueHistory = new List<EquipmentIssueRecord>
{
    new(
        Id: "ISSUE-001",
        EquipmentCode: "CONV-02",
        EquipmentName: "2호기 이송 컨베이어 라인",
        ErrorCode: "E-402",
        IssueTitle: "E-402 구동축 진동 이상 알람",
        Symptom: "구동 모터 측 비정상 고주파 진동 및 E-402 센서 경보(임계치 4.5mm/s 초과) 발생",
        ActionGuide: "1단계: 구동 모터 베어링의 윤활유(그리스) 급유 상태 및 누유 여부를 즉시 점검하십시오. " +
                    "2단계: 컨베이어 롤러 축 정렬 불량 및 벨트 하부 이물질 끼임 상태를 육안 점검하십시오. " +
                    "3단계: 진동이 지속될 경우 인버터 주파수를 30Hz로 감속 운전하고, 보전팀(내선 102)에 긴급 점검을 요청하십시오.",
        RegisteredAt: DateTime.Now.AddDays(-3).ToString("yyyy-MM-dd HH:mm")
    ),
    new(
        Id: "ISSUE-002",
        EquipmentCode: "PRESS-01",
        EquipmentName: "1호기 500톤 유압 프레스 성형기",
        ErrorCode: "E-105",
        IssueTitle: "E-105 매니폴드 유압 누유 및 압력 강하",
        Symptom: "하부 매니폴드 블록 밸브 피팅부 오일 누유 및 시스템 유압 게이지 15% 급격한 강하",
        ActionGuide: "1단계: 메인 유압 공급 밸브(HV-01)를 잠그고 긴급 비상 정지(E-Stop) 상태를 유지하십시오. " +
                    "2단계: 고압 호스 커플링 및 O-링 체결 상태를 점검하여 규정 토크(45N·m)로 재체결하십시오. " +
                    "3단계: 누유 바닥 구역에 오일 흡착포를 전면 도포하고, 리저버 탱크 잔여 유량을 확인 후 보충하십시오.",
        RegisteredAt: DateTime.Now.AddDays(-2).ToString("yyyy-MM-dd HH:mm")
    ),
    new(
        Id: "ISSUE-003",
        EquipmentCode: "CNC-04",
        EquipmentName: "4호기 고정밀 5축 CNC 가공기",
        ErrorCode: "AL-21",
        IssueTitle: "AL-21 스핀들 냉각수 온도 과열 경고",
        Symptom: "스핀들 쿨러 냉각수 순환 온도 센서 48도 초과 감지 및 AL-21 알람 발생으로 자동 가공 정지",
        ActionGuide: "1단계: 가공 작업을 즉시 중지하고 냉각 칠러 유닛의 순환 펌프 가동 여부 및 냉각수 수위를 확인하십시오. " +
                    "2단계: 칠러 흡입구 에어 필터의 분진 막힘 여부를 점검하여 압축공기로 청소하십시오. " +
                    "3단계: 칠러 냉각팬 작동 여부를 확인하고 스핀들 온도가 30도 이하로 냉각된 후 가공을 재개하십시오.",
        RegisteredAt: DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd HH:mm")
    )
};

// GET: 이슈 목록 조회 (디버그/이력 확인용)
app.MapGet("/api/issues", () => Results.Ok(issueHistory));

// POST: 사진(image)과 음성(audio)을 받아 Gemini 3.5 Flash 멀티모달 분석
app.MapPost("/api/issues/analyze", async (HttpRequest request, IConfiguration config, IHttpClientFactory httpClientFactory) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "multipart/form-data 형식의 요청이어야 합니다." });
    }

    var form = await request.ReadFormAsync();
    var imageFile = form.Files.GetFile("image");
    var audioFile = form.Files.GetFile("audio");

    if (imageFile == null && audioFile == null)
    {
        return Results.BadRequest(new { error = "사진(image) 또는 음성(audio) 파일이 최소 1개 이상 필요합니다." });
    }

    // 1. 이미지 및 음성 파일을 Base64 문자열로 변환
    string? imageBase64 = null;
    string imageMimeType = "image/jpeg";
    if (imageFile != null && imageFile.Length > 0)
    {
        imageMimeType = string.IsNullOrWhiteSpace(imageFile.ContentType) ? "image/jpeg" : imageFile.ContentType;
        using var ms = new MemoryStream();
        await imageFile.CopyToAsync(ms);
        imageBase64 = Convert.ToBase64String(ms.ToArray());
    }

    string? audioBase64 = null;
    string audioMimeType = "audio/webm";
    if (audioFile != null && audioFile.Length > 0)
    {
        audioMimeType = string.IsNullOrWhiteSpace(audioFile.ContentType) ? "audio/webm" : audioFile.ContentType;
        using var ms = new MemoryStream();
        await audioFile.CopyToAsync(ms);
        audioBase64 = Convert.ToBase64String(ms.ToArray());
    }

    var apiKey = config["Gemini:ApiKey"] ?? "";
    var model = config["Gemini:Model"] ?? "gemini-3.5-flash";

    // 2. API Key 미입력 시 스마트 시뮬레이션 모드로 동작 안내
    if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "YOUR_GEMINI_API_KEY")
    {
        var mockResult = new GeminiAnalysisResult(
            EquipmentCode: "CONV-02",
            ErrorCode: "E-402",
            TranscribedVoice: "2호기 컨베이어 모터 구동축에서 진동 소음과 함께 알람이 발생했습니다.",
            SymptomSummary: "[시뮬레이션 모드] 컨베이어 모터 부위 이상 진동 및 E-402 경보 감지. (appsettings.json에 실제 Gemini API Key를 입력하면 실시간 AI 분석으로 전환됩니다)",
            MatchedFromHistory: true,
            ActionGuide: "1단계: 구동 모터 베어링의 윤활유 그리스 상태를 점검하십시오. 2단계: 롤러 축 정렬과 벨트 이물질을 점검하십시오. 3단계: 인버터를 30Hz로 감속 운전하고 보전팀으로 보고하십시오."
        );
        return Results.Ok(mockResult);
    }

    // 3. Google Gemini 3.5 Flash REST API 요청 페이로드 구성
    var partsList = new List<object>();

    // 프롬프트 및 기존 이력 데이터 JSON 전달
    var historyJson = JsonSerializer.Serialize(issueHistory, new JsonSerializerOptions { WriteIndented = true });
    var systemPrompt = 
        "당신은 스마트 팩토리 현장 설비 보전 최고 전문가 AI입니다.\n" +
        "현장 작업자가 현장에서 촬영한 '설비 이상 사진(image)'과 직접 말한 '음성 녹음(audio)'을 종합 분석하여 표준 조치 가이드를 제공해야 합니다.\n\n" +
        "[기존 과거 설비 이슈 이력 데이터]:\n" + historyJson + "\n\n" +
        "[작업 지침]:\n" +
        "1. 전달된 오디오에서 작업자의 한국어 음성을 정확하게 받아쓰기(transcribedVoice)하십시오.\n" +
        "2. 사진과 음성을 결합하여 설비 코드(예: CONV-02, PRESS-01, CNC-04 등)와 에러 코드(예: E-402, E-105, AL-21 등)를 식별하십시오. 식별 불가 시 각각 'UNKNOWN', 'NONE'으로 표시하십시오.\n" +
        "3. 증상을 간결하고 명확하게 한국어로 요약(symptomSummary)하십시오.\n" +
        "4. 기존 이력 데이터와의 연관성을 판단하여 matchedFromHistory(true/false)를 판정하십시오.\n" +
        "5. 현장 작업자가 즉시 스마트폰 화면을 보거나 음성(TTS)으로 듣고 바로 따라 할 수 있도록, 친절하고 명확한 1단계/2단계/3단계 실행 조치 가이드(actionGuide)를 작성하십시오.";

    partsList.Add(new { text = systemPrompt });

    if (!string.IsNullOrEmpty(imageBase64))
    {
        partsList.Add(new
        {
            inline_data = new
            {
                mime_type = imageMimeType,
                data = imageBase64
            }
        });
    }

    if (!string.IsNullOrEmpty(audioBase64))
    {
        partsList.Add(new
        {
            inline_data = new
            {
                mime_type = audioMimeType,
                data = audioBase64
            }
        });
    }

    var requestPayload = new
    {
        contents = new object[]
        {
            new { parts = partsList }
        },
        generationConfig = new
        {
            temperature = 0.2,
            responseMimeType = "application/json",
            responseSchema = new
            {
                type = "OBJECT",
                properties = new Dictionary<string, object>
                {
                    { "equipmentCode", new { type = "STRING", description = "식별된 설비 코드 (식별 불가 시 UNKNOWN)" } },
                    { "errorCode", new { type = "STRING", description = "식별된 에러 코드 (없으면 NONE)" } },
                    { "transcribedVoice", new { type = "STRING", description = "작업자 한국어 음성 받아쓰기" } },
                    { "symptomSummary", new { type = "STRING", description = "사진과 음성을 종합한 증상 요약" } },
                    { "matchedFromHistory", new { type = "BOOLEAN", description = "기존 과거 이력과의 일치 여부" } },
                    { "actionGuide", new { type = "STRING", description = "현장 작업자 단계별 조치 가이드 (TTS 읽기용)" } }
                },
                required = new[] { "equipmentCode", "errorCode", "transcribedVoice", "symptomSummary", "matchedFromHistory", "actionGuide" }
            }
        }
    };

    var client = httpClientFactory.CreateClient("GeminiClient");
    var apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

    var jsonPayload = JsonSerializer.Serialize(requestPayload);

    try
    {
        // 503/429 자동 재시도 (최대 3회, 지수 백오프)
        HttpResponseMessage response = null!;
        string responseBody = "";
        const int maxRetries = 3;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            var jsonContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            response = await client.PostAsync(apiUrl, jsonContent);
            responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
                break;

            // 503 (과부하) 또는 429 (Rate Limit) → 재시도
            if ((response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
                 response.StatusCode == System.Net.HttpStatusCode.TooManyRequests) &&
                attempt < maxRetries)
            {
                await Task.Delay(attempt * 3000); // 3초, 6초 대기
                continue;
            }

            return Results.Json(new
            {
                error = "Google Gemini API 호출 실패",
                statusCode = (int)response.StatusCode,
                details = responseBody
            }, statusCode: (int)response.StatusCode);
        }

        using var doc = JsonDocument.Parse(responseBody);
        var candidates = doc.RootElement.GetProperty("candidates");
        if (candidates.GetArrayLength() == 0)
        {
            return Results.BadRequest(new { error = "Gemini 응답 생성 실패 (candidates가 비어있음)" });
        }

        var textContent = candidates[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        if (string.IsNullOrWhiteSpace(textContent))
        {
            return Results.BadRequest(new { error = "Gemini 응답 텍스트가 비어 있습니다." });
        }

        var result = JsonSerializer.Deserialize<GeminiAnalysisResult>(textContent, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (result == null)
        {
            return Results.BadRequest(new { error = "Gemini 분석 결과 JSON 파싱 실패" });
        }

        // 4. 새로운 이슈 분석 결과인 경우 메모리 List에 자동 누적 추가
        if (!result.MatchedFromHistory && result.EquipmentCode != "UNKNOWN")
        {
            var newRecord = new EquipmentIssueRecord(
                Id: $"ISSUE-{issueHistory.Count + 1:D3}",
                EquipmentCode: result.EquipmentCode,
                EquipmentName: $"{result.EquipmentCode} 현장 설비",
                ErrorCode: result.ErrorCode,
                IssueTitle: $"{result.EquipmentCode} {result.ErrorCode} 이상 이슈",
                Symptom: result.SymptomSummary,
                ActionGuide: result.ActionGuide,
                RegisteredAt: DateTime.Now.ToString("yyyy-MM-dd HH:mm")
            );
            issueHistory.Add(newRecord);
        }

        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, title: "Gemini API 처리 중 예외 발생");
    }
});

app.Run();

// DTO 레코드 정의
public record EquipmentIssueRecord(
    string Id,
    string EquipmentCode,
    string EquipmentName,
    string ErrorCode,
    string IssueTitle,
    string Symptom,
    string ActionGuide,
    string RegisteredAt
);

public record GeminiAnalysisResult(
    [property: JsonPropertyName("equipmentCode")] string EquipmentCode,
    [property: JsonPropertyName("errorCode")] string ErrorCode,
    [property: JsonPropertyName("transcribedVoice")] string TranscribedVoice,
    [property: JsonPropertyName("symptomSummary")] string SymptomSummary,
    [property: JsonPropertyName("matchedFromHistory")] bool MatchedFromHistory,
    [property: JsonPropertyName("actionGuide")] string ActionGuide
);
