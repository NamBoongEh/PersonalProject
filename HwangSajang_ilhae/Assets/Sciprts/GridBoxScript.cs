using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public class GridBoxScript : MonoBehaviour
{
    // ═══════════════════════════════════════════════════════
    //  Inspector 필드
    // ═══════════════════════════════════════════════════════

    [Header("GridBox (VerticalLayoutGroup 부모)")]
    public RectTransform gridBoxRect;

    [Header("Child Boxes")]
    public RectTransform textBox;
    public RectTransform countBox;
    public RectTransform clockBox;

    [Header("RawImage Indicators")]
    public RawImage textBoxIndicator;
    public RawImage countBoxIndicator;
    public RawImage clockBoxIndicator;

    [Header("CountBox UI")]
    public InputField countMinutesInput;   // 분 입력 (제한 없음)
    public InputField countSecondsInput;   // 초 입력 (0~60)
    public Text       countDisplayText;    // 카운트 표시 (MM:SS)
    public Button     countDownButton;     // 카운트 다운 버튼
    public Button     countUpButton;       // 카운트 업 버튼
    public Button     pauseButton;         // 일시정지 / 재개 버튼

    [Header("RecordBox")]
    public GameObject  recordBox;           // Record 버튼으로 on/off
    public Transform   recordContent;       // ScrollView 안 Content Transform
    public Text        tableTitleText;      // CSV 첫 줄에 들어갈 Table Title
    public Button      resetButton;         // Reset 버튼 (Row 생성)
    public Button      csvButton;           // CSV 내보내기 버튼
    public Button      deleteAllButton;     // 전체 Row 삭제 버튼
    public float       rowHeight = 60f;     // Row 프리팹 한 줄 높이 (Inspector에서 맞춤)

    [Header("TextBox InputField (저장 대상)")]
    public InputField textBoxInputField;    // TextBox 내부 InputField

    [Header("ClockBox UI")]
    public Text clockText;                  // HH:mm:ss 표시
    public Text dateText;                   // YYYY. MM. DD (요일) 표시

    [Header("Size Mode Settings")]
    public float minBoxHeight      = 100f;
    public float scrollSensitivity = 300f;
    public float blinkInterval     = 0.4f;

    // ═══════════════════════════════════════════════════════
    //  PlayerPrefs 키
    // ═══════════════════════════════════════════════════════

    private const string PREF_TB_COUNT  = "TextBox_InputCount";
    private const string PREF_TB_PREFIX = "TextBox_Input_";
    private const string PREF_ROW_COUNT = "Record_RowCount";
    private const string PREF_ROW_MAX   = "Record_RowMax";   // rowCount 최댓값 복원용
    private const string PREF_ROW_PREFIX= "Record_Row_";

    // ── Row 저장용 직렬화 클래스 ──
    [Serializable]
    private class RowData
    {
        public string number;
        public string startTime;
        public string endTime;
        public string elapsedTime;
        public string settingTime;
        public string overTime;
    }

    // ═══════════════════════════════════════════════════════
    //  내부 변수
    // ═══════════════════════════════════════════════════════

    private Canvas              parentCanvas;
    private VerticalLayoutGroup layoutGroup;
    private float               cachedTotalHeight = -1f;

    // Size Mode
    public  bool          isSizeMode = false;
    private RectTransform selectedBox;
    private Coroutine     blinkCoroutine;
    private bool          isPinching = false;
    private float         initialPinchDist;
    private float         initialTargetH;
    private float         initialOther0H;
    private float         initialOther1H;
    private RectTransform pinchOther0;
    private RectTransform pinchOther1;

    // Count
    private int       totalSeconds      = 0;
    private bool      isCountRunning    = false;
    private bool      isCountDown       = false;
    private bool      isPaused          = false;
    private Coroutine countCoroutine;

    // Session (Reset 시 Row 생성에 사용)
    private bool      sessionActive     = false;
    private DateTime  sessionStartTime;
    private int       sessionSettingSec = 0;
    private bool      sessionWasDown    = false;
    private int       rowCount          = 0;

    // Clock
    private Coroutine clockCoroutine;

    private static readonly string[] KoreanDays = { "일", "월", "화", "수", "목", "금", "토" };

    // ═══════════════════════════════════════════════════════
    //  초기화
    // ═══════════════════════════════════════════════════════

    void Start()
    {
        if (textBox != null)
            parentCanvas = textBox.GetComponentInParent<Canvas>();

        if (gridBoxRect != null)
        {
            layoutGroup = gridBoxRect.GetComponent<VerticalLayoutGroup>();
            if (layoutGroup != null)
            {
                layoutGroup.childControlHeight     = false;
                layoutGroup.childForceExpandHeight = false;
            }
        }

        StartCoroutine(CacheTotalHeight());

        // Seconds 0~60 검증
        if (countSecondsInput != null)
            countSecondsInput.onEndEdit.AddListener(OnSecondsEndEdit);

        // 입력 변경 시 Count Down 버튼 활성 여부 갱신
        if (countMinutesInput != null)
            countMinutesInput.onValueChanged.AddListener(_ => RefreshCountDownButton());
        if (countSecondsInput != null)
            countSecondsInput.onValueChanged.AddListener(_ => RefreshCountDownButton());

        // TextBox InputField 변경 시 자동 저장
        if (textBoxInputField != null)
            textBoxInputField.onValueChanged.AddListener(_ => SaveData());

        // 초기 버튼 상태
        RefreshCountDownButton();
        if (countUpButton  != null) countUpButton.interactable  = true;
        if (pauseButton    != null) pauseButton.interactable    = false;

        // 카운트 표시 초기화
        SetCountDisplay(0, false);

        // RecordBox 초기 비활성
        if (recordBox != null) recordBox.SetActive(false);

        // CSV · 전체삭제 버튼 초기 비활성
        RefreshRecordButtons();

        // Clock + Date 시작
        clockCoroutine = StartCoroutine(ClockRoutine());

        // 저장 데이터 복원 (레이아웃 안정 후)
        StartCoroutine(LoadDataAfterLayout());
    }

    IEnumerator CacheTotalHeight()
    {
        yield return new WaitForEndOfFrame();
        if (gridBoxRect != null && gridBoxRect.rect.height > 0f)
            cachedTotalHeight = gridBoxRect.rect.height;
    }

    // ═══════════════════════════════════════════════════════
    //  Update
    // ═══════════════════════════════════════════════════════

    void Update()
    {
        if (!isSizeMode) return;
        HandleInput();
    }

    // ═══════════════════════════════════════════════════════
    //  Size 버튼
    // ═══════════════════════════════════════════════════════

    public void ToggleSizeMode()
    {
        isSizeMode = !isSizeMode;
        if (!isSizeMode) DeselectBox();
    }

    // ═══════════════════════════════════════════════════════
    //  CountBox InputField 검증 & 버튼 상태
    // ═══════════════════════════════════════════════════════

    private void OnSecondsEndEdit(string value)
    {
        if (!int.TryParse(value, out int sec))
        {
            countSecondsInput.text = "";
        }
        else
        {
            sec = Mathf.Clamp(sec, 0, 60);
            countSecondsInput.text = sec.ToString();
        }
        RefreshCountDownButton();
    }

    // Count Down 버튼: 둘 중 하나라도 입력 있고, 카운트업 실행 중이 아닐 때 활성화
    private void RefreshCountDownButton()
    {
        if (countDownButton == null) return;
        bool hasValue  = HasAnyInput();
        bool upRunning = isCountRunning && !isCountDown;
        countDownButton.interactable = hasValue && !upRunning;
    }

    private bool HasAnyInput()
    {
        bool hasMin = countMinutesInput != null && !string.IsNullOrEmpty(countMinutesInput.text);
        bool hasSec = countSecondsInput != null && !string.IsNullOrEmpty(countSecondsInput.text);
        return hasMin || hasSec;
    }

    private bool TryGetInputSeconds(out int result)
    {
        result = 0;
        int min = 0, sec = 0;
        bool hasMin = countMinutesInput != null &&
                      !string.IsNullOrEmpty(countMinutesInput.text) &&
                      int.TryParse(countMinutesInput.text, out min);
        bool hasSec = countSecondsInput != null &&
                      !string.IsNullOrEmpty(countSecondsInput.text) &&
                      int.TryParse(countSecondsInput.text, out sec);

        if (!hasMin && !hasSec) return false;

        result = Mathf.Max(0, min) * 60 + Mathf.Clamp(sec, 0, 60);
        return true;
    }

    // ═══════════════════════════════════════════════════════
    //  Count Down 버튼 (OnClick에 연결)
    // ═══════════════════════════════════════════════════════

    public void StartCountDown()
    {
        if (!TryGetInputSeconds(out int initSec)) return;

        StopCount();
        totalSeconds       = initSec;
        isCountDown        = true;
        isCountRunning     = true;
        isPaused           = false;
        sessionActive      = true;
        sessionStartTime   = DateTime.UtcNow.AddHours(9);
        sessionSettingSec  = initSec;
        sessionWasDown     = true;

        if (countUpButton   != null) countUpButton.interactable   = false;
        if (countDownButton != null) countDownButton.interactable = false;
        if (pauseButton     != null) pauseButton.interactable     = true;

        SetCountDisplay(totalSeconds, false);
        countCoroutine = StartCoroutine(CountRoutine());
    }

    // ═══════════════════════════════════════════════════════
    //  Count Up 버튼 (OnClick에 연결)
    // ═══════════════════════════════════════════════════════

    public void StartCountUp()
    {
        StopCount();
        totalSeconds      = 0;
        isCountDown       = false;
        isCountRunning    = true;
        isPaused          = false;
        sessionActive     = true;
        sessionStartTime  = DateTime.UtcNow.AddHours(9);
        sessionSettingSec = 0;
        sessionWasDown    = false;

        if (countDownButton != null) countDownButton.interactable = false;
        if (countUpButton   != null) countUpButton.interactable   = false;
        if (pauseButton     != null) pauseButton.interactable     = true;

        SetCountDisplay(0, false);
        countCoroutine = StartCoroutine(CountRoutine());
    }

    // ── Pause Button (OnClick에 연결) ──
    public void TogglePause()
    {
        if (!isCountRunning) return;

        isPaused = !isPaused;

        if (isPaused)
        {
            if (countCoroutine != null) StopCoroutine(countCoroutine);
            countCoroutine = null;
        }
        else
        {
            countCoroutine = StartCoroutine(CountRoutine());
        }
    }

    // ── Record Button (OnClick에 연결) ──
    public void ToggleRecordBox()
    {
        if (recordBox != null)
            recordBox.SetActive(!recordBox.activeSelf);
    }

    // ── Reset Button (OnClick에 연결) ──
    public void ResetCount()
    {
        // 세션이 활성(실행 중 or 일시정지)이면 Row 생성
        if (sessionActive)
        {
            DateTime endTime = DateTime.UtcNow.AddHours(9);
            SpawnRow(sessionStartTime, endTime);
        }

        StopCount();
        totalSeconds  = 0;
        sessionActive = false;
        SetCountDisplay(0, false);
        // 배경색(Indicator)은 건드리지 않음
    }

    // ═══════════════════════════════════════════════════════
    //  Row 관련
    // ═══════════════════════════════════════════════════════

    // ── Row 프리팹 동적 생성 (세션 데이터 기반) ──
    private void SpawnRow(DateTime startTime, DateTime endTime)
    {
        if (recordContent == null) return;

        rowCount++;
        int overSec = sessionWasDown ? -totalSeconds : 0;

        var rd = new RowData
        {
            number      = rowCount.ToString(),
            startTime   = startTime.ToString("yyyy.MM.dd HH:mm:ss"),
            endTime     = endTime.ToString("yyyy.MM.dd HH:mm:ss"),
            elapsedTime = FormatMmSs(Mathf.Max(0, (int)(endTime - startTime).TotalSeconds)),
            settingTime = sessionWasDown ? FormatMmSs(sessionSettingSec) : "-",
            overTime    = (sessionWasDown && overSec > 0)
                            ? string.Format("+{0:00}:{1:00}", overSec / 60, overSec % 60)
                            : "-"
        };

        SpawnRowFromData(rd);
        UpdateContentHeight();
        RefreshRecordButtons();
        SaveData();
    }

    // ── RowData로 Row 생성 (저장 복원 및 실시간 생성 공통) ──
    private void SpawnRowFromData(RowData rd)
    {
        if (recordContent == null) return;

        GameObject prefab = Resources.Load<GameObject>("Prefabs/Row");
        if (prefab == null) { Debug.LogError("Prefabs/Row를 찾을 수 없습니다."); return; }

        GameObject row = Instantiate(prefab, recordContent);

        SetRowChildText(row, "#",            rd.number);
        SetRowChildText(row, "Start Time",   rd.startTime);
        SetRowChildText(row, "End Time",     rd.endTime);
        SetRowChildText(row, "Elapsed Time", rd.elapsedTime);
        SetRowChildText(row, "Setting Time", rd.settingTime);
        SetRowChildText(row, "Over Time",    rd.overTime);

        Button deleteBtn = row.transform.Find("Delete Button")?.GetComponent<Button>();
        if (deleteBtn != null)
            deleteBtn.onClick.AddListener(() =>
            {
                Destroy(row);
                StartCoroutine(AfterRowDelete());
            });
    }

    IEnumerator AfterRowDelete()
    {
        yield return null; // Destroy 처리 대기
        RenumberRows();
        rowCount = recordContent != null ? recordContent.childCount : 0;
        UpdateContentHeight();
        RefreshRecordButtons();
        SaveData();
    }

    // ── 삭제 후 # 번호 재정렬 ──
    private void RenumberRows()
    {
        if (recordContent == null) return;
        for (int i = 0; i < recordContent.childCount; i++)
            SetRowChildText(recordContent.GetChild(i).gameObject, "#", (i + 1).ToString());
    }

    // ── Content 높이 = rowHeight × 자식 수 ──
    private void UpdateContentHeight()
    {
        if (recordContent == null) return;
        RectTransform ct = recordContent.GetComponent<RectTransform>();
        if (ct == null) return;
        ct.sizeDelta = new Vector2(ct.sizeDelta.x, rowHeight * recordContent.childCount);
    }

    // ── 전체 Row 삭제 (Delete All Button OnClick에 연결) ──
    public void DeleteAllRows()
    {
        if (recordContent == null) return;
        for (int i = recordContent.childCount - 1; i >= 0; i--)
            Destroy(recordContent.GetChild(i).gameObject);
        rowCount = 0;
        StartCoroutine(AfterRowDelete());
    }

    // ── 자식 Text 설정 헬퍼 ──
    private void SetRowChildText(GameObject row, string childName, string value)
    {
        Transform child = row.transform.Find(childName);
        if (child == null) return;
        Text txt = child.GetComponent<Text>();
        if (txt != null) txt.text = value;
    }

    // ── Row 자식 Text 읽기 헬퍼 ──
    private string GetRowChildText(GameObject row, string childName)
    {
        Transform child = row.transform.Find(childName);
        if (child == null) return "";
        Text txt = child.GetComponent<Text>();
        return txt != null ? txt.text : "";
    }

    // ── CSV · 전체삭제 버튼 interactable 갱신 ──
    private void RefreshRecordButtons()
    {
        bool hasRows = recordContent != null && recordContent.childCount > 0;
        if (csvButton       != null) csvButton.interactable       = hasRows;
        if (deleteAllButton != null) deleteAllButton.interactable = hasRows;
    }

    // ── CSV 내보내기 (CSV Button OnClick에 연결) ──
    public void ExportCSV()
    {
        if (recordContent == null || recordContent.childCount == 0) return;

        var sb = new StringBuilder();

        // 첫 줄: Table Title
        string title = tableTitleText != null ? tableTitleText.text : "Record";
        sb.AppendLine(EscapeCSV(title));

        // 헤더
        sb.AppendLine("#,시작 시간,종료 시간,경과 시간,설정 시간,초과 시간");

        // 각 Row 데이터
        foreach (Transform child in recordContent)
        {
            GameObject row = child.gameObject;
            sb.AppendLine(string.Join(",",
                EscapeCSV(GetRowChildText(row, "#")),
                EscapeCSV(GetRowChildText(row, "Start Time")),
                EscapeCSV(GetRowChildText(row, "End Time")),
                EscapeCSV(GetRowChildText(row, "Elapsed Time")),
                EscapeCSV(GetRowChildText(row, "Setting Time")),
                EscapeCSV(GetRowChildText(row, "Over Time"))));
        }

        string fileName = "Record_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv";
        string path = Path.Combine(Application.persistentDataPath, fileName);
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));

#if UNITY_STANDALONE || UNITY_EDITOR
        Application.OpenURL("file:///" + path.Replace("\\", "/"));
#endif
    }

    // ── CSV 필드 이스케이프 ──
    private string EscapeCSV(string value)
    {
        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    // ── MM:SS 포맷 ──
    private string FormatMmSs(int totalSec)
    {
        int sec = Mathf.Abs(totalSec);
        return string.Format("{0:00}:{1:00}", sec / 60, sec % 60);
    }

    // ═══════════════════════════════════════════════════════
    //  데이터 저장 / 복원 (PlayerPrefs)
    // ═══════════════════════════════════════════════════════

    private void SaveData()
    {
        // TextBox InputField
        if (textBoxInputField != null)
            PlayerPrefs.SetString(PREF_TB_PREFIX + "0", textBoxInputField.text);

        // Record Rows
        if (recordContent != null)
        {
            int cnt = recordContent.childCount;
            PlayerPrefs.SetInt(PREF_ROW_COUNT, cnt);
            PlayerPrefs.SetInt(PREF_ROW_MAX,   rowCount);
            for (int i = 0; i < cnt; i++)
            {
                GameObject row = recordContent.GetChild(i).gameObject;
                var rd = new RowData
                {
                    number      = GetRowChildText(row, "#"),
                    startTime   = GetRowChildText(row, "Start Time"),
                    endTime     = GetRowChildText(row, "End Time"),
                    elapsedTime = GetRowChildText(row, "Elapsed Time"),
                    settingTime = GetRowChildText(row, "Setting Time"),
                    overTime    = GetRowChildText(row, "Over Time")
                };
                PlayerPrefs.SetString(PREF_ROW_PREFIX + i, JsonUtility.ToJson(rd));
            }
        }

        PlayerPrefs.Save();
    }

    private void LoadData()
    {
        // TextBox InputField
        if (textBoxInputField != null)
            textBoxInputField.SetTextWithoutNotify(
                PlayerPrefs.GetString(PREF_TB_PREFIX + "0", ""));

        // Record Rows
        int savedRows = PlayerPrefs.GetInt(PREF_ROW_COUNT, 0);
        rowCount = PlayerPrefs.GetInt(PREF_ROW_MAX, 0);

        for (int i = 0; i < savedRows; i++)
        {
            string json = PlayerPrefs.GetString(PREF_ROW_PREFIX + i, "");
            if (string.IsNullOrEmpty(json)) continue;
            RowData rd = JsonUtility.FromJson<RowData>(json);
            if (rd != null) SpawnRowFromData(rd);
        }
    }

    // 레이아웃 초기화 완료 후 로드 (WaitForEndOfFrame 이후)
    IEnumerator LoadDataAfterLayout()
    {
        yield return new WaitForEndOfFrame();
        LoadData();
        UpdateContentHeight();
        RefreshRecordButtons();
    }

    // ═══════════════════════════════════════════════════════
    //  StopCount
    // ═══════════════════════════════════════════════════════

    private void StopCount()
    {
        if (countCoroutine != null)
        {
            StopCoroutine(countCoroutine);
            countCoroutine = null;
        }
        isCountRunning = false;
        isPaused       = false;

        if (pauseButton   != null) pauseButton.interactable   = false;
        if (countUpButton != null) countUpButton.interactable  = true;
        RefreshCountDownButton();
    }

    // ═══════════════════════════════════════════════════════
    //  Count 루틴
    // ═══════════════════════════════════════════════════════

    IEnumerator CountRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(1f);

            if (isCountDown)
                totalSeconds--;
            else
                totalSeconds++;

            bool overrun = isCountDown && totalSeconds < 0;
            SetCountDisplay(totalSeconds, overrun);
            // 배경색(countBoxIndicator)은 변경하지 않음
        }
    }

    // ─── Count Display Text 갱신 ───
    // overrun=true  → "+MM:SS" 빨간색
    // overrun=false → "MM:SS"  기본색
    private void SetCountDisplay(int seconds, bool overrun)
    {
        if (countDisplayText == null) return;

        int display = Mathf.Abs(seconds);
        int m = display / 60;
        int s = display % 60;

        countDisplayText.text  = overrun
            ? string.Format("+{0:00}:{1:00}", m, s)
            : string.Format("{0:00}:{1:00}", m, s);

        countDisplayText.color = overrun ? Color.red : Color.white;
    }

    // ═══════════════════════════════════════════════════════
    //  Clock / Date 루틴 (KST = UTC+9)
    // ═══════════════════════════════════════════════════════

    IEnumerator ClockRoutine()
    {
        while (true)
        {
            DateTime kst = DateTime.UtcNow.AddHours(9);

            if (clockText != null)
                clockText.text = kst.ToString("HH:mm:ss");

            if (dateText != null)
            {
                string day = KoreanDays[(int)kst.DayOfWeek];
                dateText.text = string.Format("{0:yyyy}. {0:MM}. {0:dd} ({1})", kst, day);
            }

            yield return new WaitForSeconds(1f);
        }
    }

    // ═══════════════════════════════════════════════════════
    //  통합 입력 처리 (Size Mode)
    // ═══════════════════════════════════════════════════════

    private void HandleInput()
    {
        bool    tapped = false;
        Vector2 tapPos = Vector2.zero;

        if (Input.touchCount == 1 && Input.GetTouch(0).phase == TouchPhase.Began)
        {
            tapped = true;
            tapPos = Input.GetTouch(0).position;
        }
        else if (Input.GetMouseButtonDown(0))
        {
            tapped = true;
            tapPos = Input.mousePosition;
        }

        if (tapped)
        {
            RectTransform hit = GetBoxAtScreenPoint(tapPos);
            if (hit != null) SelectBox(hit);
            else             DeselectBox();
        }

        if (selectedBox == null) return;

        // 스크롤휠 (PC / Mac)
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (!Mathf.Approximately(scroll, 0f))
        {
            GetOtherBoxes(selectedBox, out RectTransform o0, out RectTransform o1);
            ApplyHeightChange(selectedBox, o0, o1,
                              o0.sizeDelta.y, o1.sizeDelta.y,
                              selectedBox.sizeDelta.y + scroll * scrollSensitivity);
        }

        // 핀치 줌 (iOS / Android)
        if (Input.touchCount >= 2)
        {
            Touch t0 = Input.GetTouch(0);
            Touch t1 = Input.GetTouch(1);

            bool began = t0.phase == TouchPhase.Began    || t1.phase == TouchPhase.Began;
            bool ended = t0.phase == TouchPhase.Ended    || t1.phase == TouchPhase.Ended
                      || t0.phase == TouchPhase.Canceled || t1.phase == TouchPhase.Canceled;

            if (began)
            {
                initialPinchDist = Vector2.Distance(t0.position, t1.position);
                initialTargetH   = selectedBox.sizeDelta.y;
                GetOtherBoxes(selectedBox, out pinchOther0, out pinchOther1);
                initialOther0H   = pinchOther0.sizeDelta.y;
                initialOther1H   = pinchOther1.sizeDelta.y;
                isPinching       = true;
            }
            else if (ended)
            {
                isPinching = false;
            }
            else if (isPinching && initialPinchDist > 0f)
            {
                float scale = Vector2.Distance(t0.position, t1.position) / initialPinchDist;
                ApplyHeightChange(selectedBox, pinchOther0, pinchOther1,
                                  initialOther0H, initialOther1H, initialTargetH * scale);
            }
        }
        else
        {
            isPinching = false;
        }
    }

    // ═══════════════════════════════════════════════════════
    //  박스 선택 / 해제 / 깜빡임
    // ═══════════════════════════════════════════════════════

    private void SelectBox(RectTransform box)
    {
        if (selectedBox == box) return;
        DeselectBox();
        selectedBox = box;

        RawImage ind = GetIndicator(box);
        if (ind != null)
            blinkCoroutine = StartCoroutine(BlinkRoutine(ind));
    }

    private void DeselectBox()
    {
        if (blinkCoroutine != null)
        {
            StopCoroutine(blinkCoroutine);
            blinkCoroutine = null;
        }
        RestoreIndicatorAlpha(textBoxIndicator);
        RestoreIndicatorAlpha(clockBoxIndicator);
        RestoreIndicatorAlpha(countBoxIndicator);

        selectedBox = null;
        isPinching  = false;
    }

    private void RestoreIndicatorAlpha(RawImage img)
    {
        if (img == null) return;
        Color c = img.color;
        c.a = 1f;
        img.color = c;
    }

    private IEnumerator BlinkRoutine(RawImage img)
    {
        while (true)
        {
            SetAlpha(img, 0f);
            yield return new WaitForSeconds(blinkInterval);
            SetAlpha(img, 1f);
            yield return new WaitForSeconds(blinkInterval);
        }
    }

    private void SetAlpha(RawImage img, float a)
    {
        Color c = img.color;
        c.a = a;
        img.color = c;
    }

    // ═══════════════════════════════════════════════════════
    //  높이 적용
    // ═══════════════════════════════════════════════════════

    private void ApplyHeightChange(
        RectTransform target,
        RectTransform other0, RectTransform other1,
        float other0CurH, float other1CurH,
        float newTargetH)
    {
        if (gridBoxRect == null || other0 == null || other1 == null) return;

        float totalH = cachedTotalHeight > 0f
            ? cachedTotalHeight
            : gridBoxRect.rect.height;

        if (totalH <= 0f)
            totalH = target.sizeDelta.y + other0CurH + other1CurH;
        if (totalH <= minBoxHeight * 3f) return;

        float minOthers = minBoxHeight * 2f;
        newTargetH = Mathf.Clamp(newTargetH, minBoxHeight, totalH - minOthers);

        float remaining   = totalH - newTargetH;
        float othersTotal = other0CurH + other1CurH;
        float ratio0      = othersTotal > 0f ? other0CurH / othersTotal : 0.5f;

        float h0 = Mathf.Clamp(remaining * ratio0, minBoxHeight, remaining - minBoxHeight);
        float h1 = remaining - h0;

        // 부동소수점 오차 보정
        h1 -= (newTargetH + h0 + h1) - totalH;

        target.sizeDelta = new Vector2(target.sizeDelta.x, newTargetH);
        other0.sizeDelta = new Vector2(other0.sizeDelta.x, h0);
        other1.sizeDelta = new Vector2(other1.sizeDelta.x, h1);

        LayoutRebuilder.ForceRebuildLayoutImmediate(gridBoxRect);
    }

    // ═══════════════════════════════════════════════════════
    //  유틸
    // ═══════════════════════════════════════════════════════

    private Camera GetEventCamera()
    {
        if (parentCanvas == null) return null;
        return parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : parentCanvas.worldCamera;
    }

    private RectTransform GetBoxAtScreenPoint(Vector2 screenPoint)
    {
        Camera cam = GetEventCamera();
        foreach (var box in new[] { textBox, countBox, clockBox })
        {
            if (box != null &&
                RectTransformUtility.RectangleContainsScreenPoint(box, screenPoint, cam))
                return box;
        }
        return null;
    }

    private void GetOtherBoxes(RectTransform target,
                                out RectTransform other0, out RectTransform other1)
    {
        var list = new List<RectTransform>();
        foreach (var box in new[] { textBox, countBox, clockBox })
            if (box != null && box != target) list.Add(box);

        other0 = list.Count > 0 ? list[0] : null;
        other1 = list.Count > 1 ? list[1] : null;
    }

    private RawImage GetIndicator(RectTransform box)
    {
        if (box == textBox)  return textBoxIndicator;
        if (box == countBox) return countBoxIndicator;
        if (box == clockBox) return clockBoxIndicator;
        return null;
    }
}
