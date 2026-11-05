using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

// カメラsheet の列レイアウト（UsedRange開始列 B = 0-indexed 1）
// B(1): 枠区分ヘッダー  例: "① 三方枠"
// C(2): 追加部品セット  例: "追加部品セット ： 片引戸用部品セット（ MZA674 ）"
// D(3): 品種（左）      E(4): 追加対応特注寸法（左）  F(5): 条件（左）
// H(7): 品種（右）      I(8): 追加対応特注寸法（右）  J(9): 条件（右）

string xlsPath  = args.Length > 0 ? args[0] : "H310Z10004.XLS";
string outPath  = args.Length > 1 ? args[1] : "tz.json";

var records = new List<TzRecord>();

using (var fs = new FileStream(xlsPath, FileMode.Open, FileAccess.Read))
{
    var wb    = new HSSFWorkbook(fs);
    var sheet = wb.GetSheet("カメラ") ?? throw new Exception("シート 'カメラ' が見つかりません");

    string enkuKubun  = "";
    string buhinMei   = "";
    string buhinCode  = "";
    bool   inData     = false;

    for (int r = 0; r <= sheet.LastRowNum; r++)
    {
        var row = sheet.GetRow(r);
        if (row == null) continue;

        string c1 = Cell(row, 1); // B
        string c2 = Cell(row, 2); // C
        string c3 = Cell(row, 3); // D 品種（左）
        string c4 = Cell(row, 4); // E 追加対応特注寸法（左）
        string c5 = Cell(row, 5); // F 条件（左）
        string c7 = Cell(row, 7); // H 品種（右）
        string c8 = Cell(row, 8); // I 追加対応特注寸法（右）
        string c9 = Cell(row, 9); // J 条件（右）

        // ① ② ③ などの枠区分ヘッダー
        if (Regex.IsMatch(c1, @"^[①②③④⑤⑥⑦⑧⑨⑩]"))
        {
            enkuKubun = c1;
            inData    = false;
            continue;
        }

        // 追加部品セット行 → 部品名・部品コードを抽出
        if (c2.Contains("追加部品セット"))
        {
            var m = Regex.Match(c2, @"追加部品セット\s*[：:]\s*(.+)");
            if (m.Success)
            {
                buhinMei  = m.Groups[1].Value.Trim();
                var cm    = Regex.Match(buhinMei, @"[（(]\s*([^\s）)]+)\s*[）)]");
                buhinCode = cm.Success ? cm.Groups[1].Value.Trim() : "";
            }
            inData = false;
            continue;
        }

        // 品種ヘッダー行 → 以降がデータ
        if (c3 == "品種" && c4.Contains("追加対応特注寸法"))
        {
            inData = true;
            continue;
        }

        if (!inData) continue;

        // 注記行はスキップ
        if (c3.StartsWith("※") || c1.StartsWith("注")) continue;

        // 左列データ
        if (!string.IsNullOrWhiteSpace(c3))
        {
            string sunpo = string.IsNullOrWhiteSpace(c5) ? c4 : c4 + c5;
            records.Add(new TzRecord(c3, buhinMei, enkuKubun, buhinCode, sunpo));
        }

        // 右列データ
        if (!string.IsNullOrWhiteSpace(c7))
        {
            string sunpo = string.IsNullOrWhiteSpace(c9) ? c8 : c8 + c9;
            records.Add(new TzRecord(c7, buhinMei, enkuKubun, buhinCode, sunpo));
        }
    }
}

var opts = new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder       = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};
File.WriteAllText(outPath, JsonSerializer.Serialize(records, opts));
Console.WriteLine($"出力完了: {records.Count} 件 → {outPath}");

// ---- ヘルパー ----
static string Cell(IRow row, int idx)
{
    var cell = row.GetCell(idx);
    if (cell == null) return "";
    return cell.CellType switch
    {
        CellType.String  => cell.StringCellValue.Trim(),
        CellType.Numeric => cell.NumericCellValue.ToString("0"),
        CellType.Formula => cell.ToString()?.Trim() ?? "",
        _                => ""
    };
}

// ---- モデル ----
record TzRecord(
    [property: JsonPropertyName("品種")]           string 品種,
    [property: JsonPropertyName("部品名")]          string 部品名,
    [property: JsonPropertyName("枠区分")]          string 枠区分,
    [property: JsonPropertyName("部品コード")]      string 部品コード,
    [property: JsonPropertyName("追加対応特注寸法")] string 追加対応特注寸法
);
