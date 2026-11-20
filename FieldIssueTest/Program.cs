using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);

// Enable static files
builder.Services.AddDirectoryBrowser();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// In-Memory Sample Equipment Issues (현장 설비 이슈 3건)
var sampleIssues = new List<EquipmentIssue>
{
    new(
        Id: "ISSUE-001",
        EquipmentCode: "CONV-02",
        EquipmentName: "2호기 이송 컨베이어 라인",
        IssueTitle: "E-402 구동축 진동 이상 알람",
        Symptom: "구동 모터 측 비정상 고주파 진동 및 E-402 진동 센서 경보(임계치 4.5mm/s 초과) 발생",
        Resolution: "1단계: 구동 모터 베어링의 윤활유(그리스) 급유 상태 및 누유 여부를 즉시 점검하십시오.\n" +
                    "2단계: 컨베이어 롤러 축 정렬 불량 및 벨트 하부 이물질 끼임 상태를 육안 점검하십시오.\n" +
                    "3단계: 진동이 지속될 경우 인버터 주파수를 30Hz로 감속 운전하고, 보전팀(내선 102)에 긴급 점검을 요청하십시오.",
        SafetyPrecautions: "점검 전 반드시 메인 인버터 제어반 전원을 차단(LOTO)하고, 회전체에 손이나 장갑이 말려 들어가지 않도록 1m 이상 안전거리를 유지하십시오.",
        Keywords: ["CONV", "CONV-02", "컨베이어", "컨베이", "E-402", "E402", "진동", "알람", "베어링", "모터", "롤러", "벨트", "떨림", "흔들림"]
    ),
    new(
        Id: "ISSUE-002",
        EquipmentCode: "PRESS-01",
        EquipmentName: "1호기 500톤 유압 프레스 성형기",
        IssueTitle: "하부 매니폴드 유압 누유 및 압력 강하",
        Symptom: "하부 매니폴드 블록 밸브 피팅부 오일 누유 및 시스템 유압 게이지 15% 급격한 강하 현상",
        Resolution: "1단계: 메인 유압 펌프 공급 밸브(HV-01)를 잠그고 비상 정지(E-Stop) 상태를 유지하십시오.\n" +
                    "2단계: 고압 호스 커플링 및 O-링 체결 상태를 점검하여 규정 토크(45N·m)로 재체결하십시오.\n" +
                    "3단계: 누유 바닥 구역에 오일 흡착포를 전면 도포하고, 리저버 탱크 잔여 유량을 확인 후 보충하십시오.",
        SafetyPrecautions: "고압 유압유 미세 분사로 인한 화상 및 피부 침투 위험이 있으므로 보안경과 내유성 보호장갑을 필히 착용하고 바닥 미끄러짐에 주의하십시오.",
        Keywords: ["PRESS", "PRESS-01", "프레스", "유압", "누유", "기름", "오일", "압력", "밸브", "호스", "새", "새는", "새고", "리크"]
    ),
    new(
        Id: "ISSUE-003",
        EquipmentCode: "ROBOT-03",
        EquipmentName: "3호기 차체 용접 6축 다관절 로봇",
        IssueTitle: "J2 축 서보 과부하(Overload) 정지",
        Symptom: "J2 축 서보 모터 발열 경고 및 AL-105 서보 과부하(Overload) 인터록 트립 발생",
        Resolution: "1단계: 로봇 가동 반경 내 그리퍼 지그 간섭, 케이블 트랙 꼬임 및 기구적 걸림 여부를 확인하십시오.\n" +
                    "2단계: 티칭 펜던트 진단 메뉴에서 J2 축 모터 부하율(정상치 80% 이하 / 현재 과부하치)을 확인하십시오.\n" +
                    "3단계: 기구적 이상이 없으면 10분간 자연 냉각 후 알람을 리셋하고 저속(10%) 수동 조작(Jog)으로 원점 복귀하십시오.",
        SafetyPrecautions: "안전 펜스 내부 진입 전 안전 플러그를 반드시 소지하여 도어 인터록을 활성화하고, 티칭 펜던트의 데드맨 스위치를 잡은 상태로 진입하십시오.",
        Keywords: ["ROBOT", "ROBOT-03", "로봇", "서보", "과부하", "오버로드", "AL-105", "AL105", "J2", "모터", "감속기", "티칭", "에러", "정지"]
    )
};

// GET: 샘플 이슈 목록 반환 (화면에서 빠른 테스트/가이드용)
app.MapGet("/api/issues", () => Results.Ok(sampleIssues));

// POST: 사진과 음성 텍스트를 multipart/form-data로 받아 유사 이슈 매칭 및 조치 방법 반환
app.MapPost("/api/issues/analyze", async (HttpRequest request) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "multipart/form-data 형식의 요청이어야 합니다." });
    }

    var form = await request.ReadFormAsync();
    var voiceText = form["voiceText"].ToString().Trim();
    var photo = form.Files.GetFile("photo");

    var photoFileName = photo?.FileName;
    var photoLength = photo?.Length ?? 0;

    if (string.IsNullOrWhiteSpace(voiceText))
    {
        return Results.BadRequest(new { error = "음성 텍스트(voiceText)가 전달되지 않았습니다." });
    }

    // 키워드 매칭 로직
    var normalizedVoice = voiceText.ToLowerInvariant();
    // 한글 및 영문 단어 단위 분리
    var tokens = Regex.Split(normalizedVoice, @"[\s,.\-_/]+").Where(t => t.Length > 0).ToHashSet();

    var scoredIssues = sampleIssues.Select(issue =>
    {
        var matchedKeywords = new List<string>();
        int score = 0;

        foreach (var kw in issue.Keywords)
        {
            var lowerKw = kw.ToLowerInvariant();
            if (tokens.Contains(lowerKw))
            {
                matchedKeywords.Add(kw);
                score += 30; // 정확 토큰 일치
            }
            else if (normalizedVoice.Contains(lowerKw))
            {
                matchedKeywords.Add(kw);
                score += 15; // 부분 문자열 일치
            }
        }

        // 설비 코드(예: CONV-02) 직접 언급 시 높은 가중치
        if (normalizedVoice.Contains(issue.EquipmentCode.ToLowerInvariant()))
        {
            score += 40;
        }

        return new
        {
            Issue = issue,
            Score = score,
            MatchedKeywords = matchedKeywords.Distinct().ToList()
        };
    })
    .OrderByDescending(x => x.Score)
    .ToList();

    var bestMatch = scoredIssues.FirstOrDefault();

    // 일치 점수가 일정 기준(15점 이상) 이상이면 매칭 성공
    if (bestMatch != null && bestMatch.Score >= 15)
    {
        var issue = bestMatch.Issue;
        var confidence = Math.Min(98, Math.Max(60, bestMatch.Score));

        return Results.Ok(new
        {
            matched = true,
            equipmentCode = issue.EquipmentCode,
            equipmentName = issue.EquipmentName,
            issueTitle = issue.IssueTitle,
            symptom = issue.Symptom,
            resolution = issue.Resolution,
            safetyPrecautions = issue.SafetyPrecautions,
            matchedKeywords = bestMatch.MatchedKeywords,
            confidenceScore = confidence,
            photoReceived = photoLength > 0,
            photoFileName = photoFileName,
            photoSizeKb = Math.Round(photoLength / 1024.0, 1),
            receivedVoiceText = voiceText,
            ttsSummary = $"{issue.EquipmentName} {issue.IssueTitle} 조치 방법입니다. {issue.Resolution} 안전 수칙: {issue.SafetyPrecautions}",
            analyzedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        });
    }

    // 매칭 실패 시 기본 응답 및 안내
    return Results.Ok(new
    {
        matched = false,
        receivedVoiceText = voiceText,
        photoReceived = photoLength > 0,
        photoFileName = photoFileName,
        resolution = "입력된 음성 및 사진에서 일치하는 설비 이슈를 찾지 못했습니다.\n" +
                     "설비 번호(CONV-02, PRESS-01, ROBOT-03)나 이상 증상(진동, 누유, 과부하 등)을 포함하여 다시 말씀해 주십시오.\n" +
                     "긴급 상황 발생 시 현장 안전관리자(내선 119)에 즉시 유선 연락 바랍니다.",
        safetyPrecautions = "확인되지 않은 설비는 임의 조작을 금지하며, 이상 징후 발생 구역에는 안전 라인을 설치하고 접근을 통제하십시오.",
        ttsSummary = "일치하는 설비 이슈를 찾지 못했습니다. 설비 번호나 고장 증상을 다시 말씀해 주시거나 현장 관리자에게 문의하십시오.",
        analyzedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
    });
});

app.Run();

// 설비 이슈 모델 레코드
public record EquipmentIssue(
    string Id,
    string EquipmentCode,
    string EquipmentName,
    string IssueTitle,
    string Symptom,
    string Resolution,
    string SafetyPrecautions,
    string[] Keywords
);
