# PART_BASIC_INFO_API.py 使用说明

`part_basic_info`（部品基本信息表）的**数据登录接口**。
JSON 输入 / JSON 输出的命令行工具（CLI），专供 **AI 或外部程序**调用。

| 项目 | 内容 |
|------|------|
| 脚本位置 | `ツール/PyMuPDF/WindowsFormsApp1/PART_BASIC_INFO_API.py` |
| 默认数据库 | `ツール/図面/db/zumen_data.sqlite`（与 `XY_TO_DB.py` 同一个库） |
| 数据库指定 | `--db <路径>` 或环境变量 `ZUMEN_DB`（默认路径按脚本位置自动解析，不依赖工作目录） |
| 建表 | 库/表不存在时自动创建（表结构与 `XY_TO_DB.py` 完全一致） |
| 退出码 | 0 = 成功，1 = 失败（结果 JSON 中 `success` 字段同步判断） |

## 表字段（part_basic_info）

| 字段 | 类型 | 必填 | 说明 |
|------|------|:---:|------|
| id | INTEGER | - | 自增主键（数据库自动赋值，**请勿传入**） |
| zumen_no | TEXT | ✔ | 図番号 |
| seq_no | INTEGER | | 番号（整数） |
| part_name | TEXT | | 部品名 |
| frame_class | TEXT | | 枠区分（三方枠/下枠） |
| quantity | TEXT | | 員数（数字或 A～G 記号） |
| base_dim | TEXT | | 基本寸法 |
| cut_dim | TEXT | | 切断寸法 |
| part_code | TEXT | | 部品コード |
| remarks | TEXT | | 备注 |

> 随时运行 `python PART_BASIC_INFO_API.py schema` 可获取以上结构的 JSON 版本。

## 调用方式

```
python PART_BASIC_INFO_API.py <命令> [选项]
```

### 命令一览

| 命令 | 说明 | 主要选项 |
|------|------|----------|
| `insert` | 登录数据（1 条对象或多条对象数组均可） | `--data` / `--file`，`--replace` |
| `insert-many` | 批量登录（与 insert 等价） | `--data` / `--file`，`--replace` |
| `upsert` | 登录或更新（按 zumen_no+seq_no 匹配：命中→更新，未命中→插入） | `--data` / `--file` |
| `update` | 更新已有记录（只更新指定字段） | `--id` 或 `--zumen [--seq]`，`--data` / `--file` |
| `delete` | 删除记录 | `--id` 或 `--zumen [--seq]` |
| `query` | 查询记录 | `--id` / `--zumen [--seq]` / `--all`，`--limit N` |
| `count` | 统计行数 | `--zumen`（可省） |
| `clear` | 清空数据 | `--zumen`（省略则清全表并重置自增 id） |
| `schema` | 显示表结构（JSON） | - |

### 数据输入方式（insert / insert-many / upsert / update）

1. `--data '<JSON>'`：直接传 JSON 字符串
2. `--file <路径>`：从 UTF-8 JSON 文件读取（兼容 BOM）
3. 都不指定时：从标准输入 stdin 读取

### 记录 JSON 格式

```json
{
  "zumen_no": "HH-001",
  "seq_no": 1,
  "part_name": "下枠",
  "frame_class": "下枠",
  "quantity": "2",
  "base_dim": "0930",
  "cut_dim": "0930",
  "part_code": "HH-0930-WA",
  "remarks": null
}
```

- `zumen_no` 必填（非空）；其余字段均可省略（省略 → NULL）
- `seq_no` 接受整数或数字字符串（如 `1` 或 `"1"`），自动转整数
- 传入未知字段会报错并在 `detail` 中列出全部可写字段（便于自我纠正）

## 输出格式（stdout，均为 JSON）

成功（退出码 0）：

```json
{
  "success": true,
  "command": "insert",
  "inserted": 2,
  "ids": [1, 2],
  "total_rows": 2,
  "db": "D:\\...\\zumen_data.sqlite"
}
```

失败（退出码 1）：

```json
{
  "success": false,
  "error": "存在无法识别的字段: zumen",
  "detail": "可写字段: zumen_no, seq_no, part_name, ..."
}
```

## 示例

```bash
# 查看表结构
python PART_BASIC_INFO_API.py schema

# 登录一条数据
python PART_BASIC_INFO_API.py insert \
  --data '{"zumen_no":"HH-001","seq_no":1,"part_name":"下枠","quantity":"2","part_code":"HH-0930"}'

# 批量登录（同一図番号先删旧数据 → 可重复执行、幂等）
python PART_BASIC_INFO_API.py insert-many --file parts.json --replace

# 登录或更新（zumen_no+seq_no 已存在则更新，否则插入）
python PART_BASIC_INFO_API.py upsert \
  --data '[{"zumen_no":"HH-001","seq_no":1,"quantity":"3"},{"zumen_no":"HH-001","seq_no":2,"part_name":"三方枠"}]'

# 更新 id=5 的員数
python PART_BASIC_INFO_API.py update --id 5 --data '{"quantity":"4"}'

# 更新図番号 HH-001 番号 2 的部品名
python PART_BASIC_INFO_API.py update --zumen HH-001 --seq 2 --data '{"part_name":"三方枠"}'

# 查询
python PART_BASIC_INFO_API.py query --zumen HH-001
python PART_BASIC_INFO_API.py query --zumen HH-001 --seq 1
python PART_BASIC_INFO_API.py query --id 5
python PART_BASIC_INFO_API.py query --all --limit 100

# 统计
python PART_BASIC_INFO_API.py count
python PART_BASIC_INFO_API.py count --zumen HH-001

# 删除
python PART_BASIC_INFO_API.py delete --id 5
python PART_BASIC_INFO_API.py delete --zumen HH-001 --seq 1
python PART_BASIC_INFO_API.py delete --zumen HH-001

# 清空（全表 + 重置自增 id / 或只清指定図番号）
python PART_BASIC_INFO_API.py clear
python PART_BASIC_INFO_API.py clear --zumen HH-001
```

## 注意事项

- **insert 默认追加**；同一図番号重复登录会累积数据。需要「重跑覆盖」时加 `--replace`（登录前先删除该図番号的全部旧数据）。
- **upsert** 按 `zumen_no + seq_no` 匹配；`seq_no` 省略时按 NULL 匹配/写入。
- `update` / `delete` 的定位：`--id` 与 `--zumen [--seq]` 二选一（同时指定时 `--id` 优先）。
- 本接口与 `XY_TO_DB.py` 写同一张表，可混用；`XY_TO_DB.py` 登录某図番号时会先删除该図番号的旧数据（含本接口写入的数据）。
- Windows cmd 下 `--data` 的 JSON 引号转义较麻烦，建议：用 Git Bash 执行（单引号包裹）、用 `--file` 传 JSON 文件、或走 stdin 管道。
