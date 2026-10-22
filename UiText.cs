using System;
using System.Collections.Generic;
using System.IO;

namespace ScreenShift
{
    public static class L
    {
        public static readonly string[] Codes = { "en", "zh", "ru", "ja", "ko" };
        static readonly string[] TranslationCodes = { "en", "zh", "ja", "ko", "ru" };
        public static readonly string[] Names = { "English", "简体中文", "Русский", "日本語", "한국어" };
        public static string Language = Normalize(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        public static bool Russian { get { return Language == "ru"; } set { Language = value ? "ru" : "en"; } }
        public static string Caption { get { return "ScreenShift"; } }
        public static string LanguageFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenShift", "language.txt");
        public static string Normalize(string code) { return Array.IndexOf(Codes, code) >= 0 ? code : "en"; }
        public static void LoadLanguage()
        {
            try { if (File.Exists(LanguageFile)) Language = Normalize(File.ReadAllText(LanguageFile).Trim()); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        public static void SaveLanguage()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LanguageFile));
            File.WriteAllText(LanguageFile, Language);
        }
        static readonly Dictionary<string, string[]> Texts = new Dictionary<string, string[]> {
            { "ultrawide", new[] { " (ultrawide test; may letterbox)", "（超宽测试；可能留边）", "（ウルトラワイド試験・余白あり）", " (울트라와이드 테스트; 여백 가능)", " (сверхширокий тест; возможны поля)" } },
            { ": error code ", new[] { ": error code ", "：错误码 ", "：エラーコード ", ": 오류 코드 ", ": код ошибки " } },
            { "aspect", new[] { "Keep original aspect ratio (A; Fill only)", "保持原始比例（A；仅 Fill）", "元の縦横比を維持（A・Fill のみ）", "원래 화면 비율 유지 (A; Fill 전용)", "Сохранять пропорции (A; только Fill)" } },
            { "area", new[] { "Match desktop to selected area (D)", "按选区比例设置分辨率（D）", "選択領域に解像度を合わせる（D）", "선택 영역에 해상도 맞추기 (D)", "Разрешение по области (D)" } },
            { "instructions", new[] {
                "Yellow: boundaries. Red: excluded area.\nLeft/Right: left edge; Up/Down: bottom edge\nCtrl + arrows: right/top edges; Shift: 20 px; Alt: 1 px\nClick the background to adjust edges. D: area resolution; F6: resolution list.\n\nLeft {0}  Top {1}  Right {2}  Bottom {3} px\nTab: mode — {4}\n\nEnter: apply   R: reset   Esc: cancel",
                "黄线是显示边界，红色区域将留黑。\n← → 调整左边；↑ ↓ 调整下边\nCtrl + 方向键调整右边／上边；Shift：20 像素；Alt：1 像素\n点击背景后调整边界。D：选区分辨率；F6：选择分辨率。\n\n左 {0}  上 {1}  右 {2}  下 {3} 像素\nTab 切换模式：{4}\n\nEnter 应用   R 重置   Esc 取消",
                "黄色は表示境界、赤色は除外領域です。\n← →：左端、↑ ↓：下端\nCtrl + 矢印：右端／上端、Shift：20 px、Alt：1 px\n背景をクリックして境界を調整。D：領域の解像度、F6：解像度一覧。\n\n左 {0}  上 {1}  右 {2}  下 {3} px\nTab：モード — {4}\n\nEnter：適用   R：リセット   Esc：キャンセル",
                "노란색은 표시 경계, 빨간색은 제외 영역입니다.\n← →: 왼쪽, ↑ ↓: 아래쪽\nCtrl + 방향키: 오른쪽/위쪽, Shift: 20 px, Alt: 1 px\n배경을 클릭하여 경계를 조정하세요. D: 영역 해상도, F6: 해상도 목록.\n\n왼쪽 {0}  위 {1}  오른쪽 {2}  아래 {3} px\nTab: 모드 — {4}\n\nEnter: 적용   R: 초기화   Esc: 취소",
                "Жёлтые линии — границы. Красное станет чёрным.\n← →: левая; ↑ ↓: нижняя\nCtrl + стрелки: правая/верхняя; Shift: 20 px; Alt: 1 px\nЩёлкните фон для настройки. D: разрешение по области; F6: список.\n\nСлева {0}  Сверху {1}  Справа {2}  Снизу {3} px\nTab: режим — {4}\n\nEnter: применить   R: сброс   Esc: отмена" } },
            { "native", new[] { " (1:1, auto)", "（1:1，自动）", "（1:1、自動）", " (1:1, 자동)", " (1:1, авто)" } },
            { "driver", new[] { " (driver; may letterbox)", "（驱动模式；可能留边）", "（ドライバー・余白あり）", " (드라이버; 여백 가능)", " (драйвер; возможны поля)" } },
            { "custom", new[] { " (custom)", "（自定义）", "（カスタム）", " (사용자 지정)", " (свой режим)" } },
            { "crisp", new[] { "Crisp: 1:1, {0} × {1}", "清晰：1:1，{0} × {1}", "鮮明：1:1、{0} × {1}", "선명: 1:1, {0} × {1}", "Чётко: 1:1, {0} × {1}" } },
            { "fill", new[] { "Fill: preserve proportions", "Fill：等比例缩放", "Fill：縦横比を維持", "Fill: 비율 유지", "Fill: сохранить пропорции" } },
            { "stretch", new[] { "Fill: stretch (distorts proportions)", "Fill：拉伸铺满（比例会变形）", "Fill：引き伸ばし（比率が変化）", "Fill: 늘리기 (비율 왜곡)", "Fill: растянуть (искажение пропорций)" } },
            { "desktop", new[] { "Desktop {0} × {1} (F6: choose)", "桌面 {0} × {1}（F6 选择）", "デスクトップ {0} × {1}（F6 選択）", "바탕 화면 {0} × {1} (F6 선택)", "Рабочий стол {0} × {1} (F6: выбор)" } },
            { "invalid", new[] { "Resolution does not fit. Reduce cuts or choose another mode.", "分辨率不适用，请减小裁切或选择其他模式。", "解像度が合いません。切り取りを減らすか別のモードを選択してください。", "해상도가 맞지 않습니다. 잘라내기를 줄이거나 다른 모드를 선택하세요.", "Разрешение не подходит. Уменьшите полосы или выберите другой режим." } },
            { "Keep", new[] { "Keep", "保留", "維持", "유지", "Оставить" } },
            { "Revert", new[] { "Revert", "恢复", "元に戻す", "되돌리기", "Вернуть как было" } },
            { "confirm", new[] { "Can you see the whole desktop?\nReverting in {0} seconds unless you choose Keep.", "能看到完整桌面吗？\n请点击“保留”，否则 {0} 秒后恢复。", "デスクトップ全体が見えますか？\n「維持」を選ばなければ {0} 秒後に元に戻ります。", "바탕 화면 전체가 보이나요?\n유지를 선택하지 않으면 {0}초 후 복원됩니다.", "Весь рабочий стол виден?\nБез подтверждения всё вернётся через {0} с." } },
            { "fallback", new[] {
                "The driver rejected {0} × {1} (code {2}).\nA validated alternative is {3} × {4}. It preserves proportions but may leave margins. Try it?",
                "驱动拒绝了 {0} × {1}（错误码 {2}）。\n另一个已通过验证的模式为 {3} × {4}，保持比例但可能留边。是否尝试？",
                "ドライバーが {0} × {1} を拒否しました（コード {2}）。\n検証済みの代替は {3} × {4} です。縦横比を維持しますが余白が出る場合があります。試しますか？",
                "드라이버가 {0} × {1}을 거부했습니다 (코드 {2}).\n검증된 대안은 {3} × {4}입니다. 비율을 유지하지만 여백이 생길 수 있습니다. 시도할까요?",
                "Драйвер отклонил {0} × {1} (код {2}).\nПроверенный вариант: {3} × {4}. Пропорции сохранятся, возможны поля. Попробовать?" } },
            { "rejected", new[] {
                "Windows rejected the display configuration.\nStage: {0}; code: {1}\nDesktop: {2} × {3}; region: {4}\n{5}\nTry a driver mode or turn off area resolution.\nDiagnostic file: {6}",
                "Windows 拒绝了显示配置。\n阶段：{0}；错误码：{1}\n桌面：{2} × {3}；选区：{4}\n{5}\n请尝试驱动模式或取消选区分辨率。\n诊断文件：{6}",
                "Windows が表示設定を拒否しました。\n段階：{0}、コード：{1}\nデスクトップ：{2} × {3}、領域：{4}\n{5}\nドライバーモードを選ぶか領域解像度を無効にしてください。\n診断ファイル：{6}",
                "Windows에서 디스플레이 설정을 거부했습니다.\n단계: {0}, 코드: {1}\n바탕 화면: {2} × {3}, 영역: {4}\n{5}\n드라이버 모드를 선택하거나 영역 해상도를 끄세요.\n진단 파일: {6}",
                "Windows отклонила конфигурацию.\nЭтап: {0}; код: {1}\nРабочий стол: {2} × {3}; область: {4}\n{5}\nВыберите режим драйвера или отключите разрешение по области.\nДиагностика: {6}" } },
            { "The shift is removed, the picture is full screen again.", new[] { "The shift is removed, the picture is full screen again.", "已重置，恢复全屏显示。", "リセットして全画面に戻しました。", "초기화하여 전체 화면으로 복원했습니다.", "Сдвиг убран, картинка снова на весь экран." } },
            { "Could not apply the shift", new[] { "Could not apply the shift", "无法应用显示设置", "表示設定を適用できません", "표시 설정을 적용할 수 없습니다", "Не удалось применить сдвиг" } },
            { "Could not save the shift", new[] { "Could not save the shift", "无法保存显示设置", "表示設定を保存できません", "표시 설정을 저장할 수 없습니다", "Не удалось сохранить сдвиг" } },
            { "Could not remove the shift", new[] { "Could not remove the shift", "无法恢复全屏", "全画面に戻せません", "전체 화면으로 복원할 수 없습니다", "Не удалось убрать сдвиг" } },
            { "Could not temporarily remove the shift", new[] { "Could not temporarily remove the shift", "无法临时恢复全屏", "一時的に全画面に戻せません", "일시적으로 전체 화면으로 복원할 수 없습니다", "Не удалось временно убрать сдвиг" } },
            { "single", new[] { "Exactly one active display is required (active paths: {0}).", "需要且仅支持一个活动显示器（当前路径数：{0}）。", "有効なディスプレイは1台のみ対応します（現在：{0}）。", "활성 디스플레이는 하나만 지원됩니다 (현재: {0}).", "Нужен один активный экран (путей: {0})." } }
        };
        public static string Get(string key) { string[] values; return Texts.TryGetValue(key, out values) ? values[Math.Max(0, Array.IndexOf(TranslationCodes, Language))] : key; }
        public static string F(string key, params object[] args) { return string.Format(Get(key), args); }
        public static string T(string ru, string en) { return Texts.ContainsKey(en) ? Get(en) : Russian ? ru : en; }
    }
}
