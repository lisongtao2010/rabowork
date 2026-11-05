using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Text;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// 部品基本信息表的 SQLite 仓储。
    /// 数据库文件默认位于 &lt;程序目录&gt;\Data\MSIF_Parts.db，
    /// 可用 App.config 的 PartDatabasePath 覆盖（相对路径按程序目录解析）。
    /// </summary>
    internal sealed class PartBasicInfoRepository : IDisposable
    {
        /// <summary>数据库默认文件名。</summary>
        public const string DefaultDatabaseFileName = "MSIF_Parts.db";

        /// <summary>数据库默认子文件夹名。</summary>
        public const string DefaultDatabaseFolderName = "Data";

        /// <summary>部品基本信息表名。</summary>
        public const string TableName = "部品基本信息";

        /// <summary>App.config 中指定数据库路径的键名。</summary>
        public const string DatabasePathSettingKey = "PartDatabasePath";

        /// <summary>唯一索引名：図番 + 页码 + 番号 + 部品コード（同一番号でも部品コードが違えば別行）。</summary>
        public const string UniqueIndexName = "ux_部品基本信息_図番_页码_番号_部品コード";

        /// <summary>旧版唯一索引名（図番 + 页码 + 番号）。打开数据库时删除并升级成 <see cref="UniqueIndexName"/>。</summary>
        public const string LegacyUniqueIndexName = "ux_部品基本信息_図番_页码_番号";

        private static readonly string[] ColumnNames = new string[]
        {
            "図番", "页码", "番号", "部品名", "部品図番", "枠区分", "員数",
            "基本寸法", "切断寸法", "部品コード", "校验状态", "备注", "数据来源", "来源标识",
            "来源表格文件", "来源PDF", "登录时间"
        };

        /// <summary>「数据来源」列名（后加的可选列，打开旧数据库时自动补上）。</summary>
        public const string SourceKindColumnName = "数据来源";

        /// <summary>「来源标识」列名（后加的可选列，打开旧数据库时自动补上）。</summary>
        public const string SourceTagColumnName = "来源标识";

        /// <summary>插入语句（列名与参数名共用 <see cref="ColumnNames"/>，避免两处不一致）。</summary>
        private static readonly string InsertSql = BuildInsertSql();


        private readonly string _databasePath;
        private readonly SQLiteConnection _connection;

        /// <summary>
        /// 打开（不存在时创建）数据库并确保表结构存在。
        /// </summary>
        /// <param name="databasePath">数据库文件完整路径；为空时使用 <see cref="ResolveDatabasePath"/>。</param>
        public PartBasicInfoRepository(string databasePath)
        {
            _databasePath = string.IsNullOrWhiteSpace(databasePath)
                ? ResolveDatabasePath()
                : databasePath.Trim();

            string folder = Path.GetDirectoryName(_databasePath);

            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            SQLiteConnectionStringBuilder builder = new SQLiteConnectionStringBuilder();
            builder.DataSource = _databasePath;
            builder.FailIfMissing = false;
            builder.ForeignKeys = true;

            _connection = new SQLiteConnection(builder.ToString());
            _connection.Open();

            EnsureSchema();
        }

        /// <summary>数据库文件的完整路径。</summary>
        public string DatabasePath
        {
            get { return _databasePath; }
        }

        /// <summary>
        /// 解析数据库路径：App.config 的 PartDatabasePath（相对路径按程序目录）→ 默认 &lt;程序目录&gt;\Data\MSIF_Parts.db。
        /// </summary>
        public static string ResolveDatabasePath()
        {
            string configured = null;

            try
            {
                configured = ConfigurationManager.AppSettings[DatabasePathSettingKey];
            }
            catch (ConfigurationErrorsException)
            {
                configured = null;
            }

            if (!string.IsNullOrWhiteSpace(configured))
            {
                string value = configured.Trim();

                return Path.IsPathRooted(value)
                    ? value
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, value);
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DefaultDatabaseFolderName,
                DefaultDatabaseFileName);
        }

        /// <summary>
        /// 建表（已存在则跳过）并创建索引。
        ///
        /// 唯一键 = 図番 + 页码 + 番号 + 部品コード：
        /// 部品表里「下表参照」的行会被展开成多条同一番号、不同部品コード的明细
        /// （见 CadPartSplitter 的 01_アッセンブリ図_部品機能情報.csv，旧名 LT_STEP1.csv），
        /// 只用「図番 + 页码 + 番号」做唯一键会把它们互相顶掉。
        /// 旧版本留下的索引 <see cref="LegacyUniqueIndexName"/> 在这里删除并换成 <see cref="UniqueIndexName"/>，
        /// 已有数据库打开时自动迁移（旧索引保证旧数据在新索引下同样唯一）。
        /// 后加的「数据来源」「来源标识」列用 <see cref="EnsureColumn"/> 补（老数据库自动升级）。
        /// </summary>
        private void EnsureSchema()
        {
            string createTable =
                "CREATE TABLE IF NOT EXISTS [" + TableName + "] (" +
                "[id] INTEGER PRIMARY KEY AUTOINCREMENT, " +
                "[図番] TEXT NOT NULL, " +
                "[页码] INTEGER NOT NULL DEFAULT 0, " +
                "[番号] INTEGER NOT NULL, " +
                "[部品名] TEXT, " +
                "[部品図番] TEXT, " +
                "[枠区分] TEXT, " +
                "[員数] TEXT, " +
                "[基本寸法] TEXT, " +
                "[切断寸法] TEXT, " +
                "[部品コード] TEXT, " +
                "[校验状态] TEXT, " +
                "[备注] TEXT, " +
                "[" + SourceKindColumnName + "] TEXT, " +
                "[" + SourceTagColumnName + "] TEXT, " +
                "[来源表格文件] TEXT, " +
                "[来源PDF] TEXT, " +
                "[登录时间] TEXT NOT NULL" +
                ");";

            string dropLegacyIndex = "DROP INDEX IF EXISTS [" + LegacyUniqueIndexName + "];";

            string createIndex =
                "CREATE UNIQUE INDEX IF NOT EXISTS [" + UniqueIndexName + "] ON [" + TableName +
                "] ([図番], [页码], [番号], [部品コード]);";

            using (SQLiteTransaction transaction = _connection.BeginTransaction())
            {
                Execute(createTable, transaction);

                bool sourceKindAdded = EnsureColumn(transaction, SourceKindColumnName, "TEXT");
                bool sourceTagAdded = EnsureColumn(transaction, SourceTagColumnName, "TEXT");

                if (sourceKindAdded)
                {
                    // 刚补的列：按「来源表格文件」把老数据的来源回填一次
                    BackfillSourceKind(transaction);
                }

                if (sourceTagAdded)
                {
                    // 刚补的列：先按「数据来源」换算，再按「来源表格文件」兜底
                    BackfillSourceTag(transaction);
                }

                Execute(dropLegacyIndex, transaction);
                Execute(createIndex, transaction);
                transaction.Commit();
            }
        }

        /// <summary>
        /// 给已存在的表补一个可空列（旧数据库升级用）：列已存在时什么都不做。
        /// 「数据来源」「来源标识」是后加的列，老数据库（本版本之前建的表）打开时在这里补上，
        /// 老数据该列保持 NULL，随后由 <see cref="BackfillSourceKind"/> / <see cref="BackfillSourceTag"/>
        /// 按来源文件（或已填好的「数据来源」）推断填值。
        /// </summary>
        /// <returns>true = 本次新加了列。</returns>
        private bool EnsureColumn(SQLiteTransaction transaction, string columnName, string columnType)
        {
            if (ColumnExists(transaction, columnName))
            {
                return false;
            }

            Execute("ALTER TABLE [" + TableName + "] ADD COLUMN [" + columnName + "] " + columnType + ";",
                transaction);

            return true;
        }

        /// <summary>判断表里是否已有指定列（用 PRAGMA table_info 取列清单）。</summary>
        private bool ColumnExists(SQLiteTransaction transaction, string columnName)
        {
            using (SQLiteCommand command = new SQLiteCommand(
                "PRAGMA table_info([" + TableName + "]);", _connection, transaction))
            {
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        // PRAGMA table_info 的第 2 列（ordinal 1）是列名
                        if (!reader.IsDBNull(1) &&
                            string.Equals(reader.GetString(1), columnName, StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 补上「数据来源」列后，按「来源表格文件」把老数据的来源回填一次（只填空值）。
        /// 规则与写入时的取值一致：<c>02_要領図_部品機能情報.csv</c>（旧名 <c>LT_STEP2.csv</c>）→ 要領図；
        /// <c>01_アッセンブリ図_部品機能情報.csv</c>（旧名 <c>LT_STEP1.csv</c>）→ アッセンブリ図；
        /// <c>plu_table.txt</c> → AI 分析。认不出来的（手工录入等）保持空。
        /// </summary>
        private void BackfillSourceKind(SQLiteTransaction transaction)
        {
            FillColumnBySourceFile(transaction, SourceKindColumnName, PartSourceKind.CmdYoryo,
                CmdInstructionPartCollector.YoryoPartFileName,
                CmdInstructionPartCollector.LegacyYoryoPartFileName);

            FillColumnBySourceFile(transaction, SourceKindColumnName, PartSourceKind.CmdAssembly,
                CmdPartAnalyzer.LtStep1FileName,
                CmdPartAnalyzer.LegacyLtStep1FileName);

            FillColumnBySourceFile(transaction, SourceKindColumnName, PartSourceKind.AiAnalysis,
                "plu_table.txt");
        }

        /// <summary>
        /// 补上「来源标识」列后回填老数据（只填空值）：先按已有的「数据来源」换算（最准确），
        /// 仍是空的行再按「来源表格文件」推断（与 <see cref="BackfillSourceKind"/> 同一套规则）。
        /// </summary>
        private void BackfillSourceTag(SQLiteTransaction transaction)
        {
            FillColumnBySourceKind(transaction, SourceTagColumnName, PartSourceTag.CmdYoryo,
                PartSourceKind.CmdYoryo);

            FillColumnBySourceKind(transaction, SourceTagColumnName, PartSourceTag.CmdAssembly,
                PartSourceKind.CmdAssembly);

            FillColumnBySourceKind(transaction, SourceTagColumnName, PartSourceTag.AiAnalysis,
                PartSourceKind.AiAnalysis);

            FillColumnBySourceKind(transaction, SourceTagColumnName, PartSourceTag.Manual,
                PartSourceKind.Manual);

            FillColumnBySourceFile(transaction, SourceTagColumnName, PartSourceTag.CmdYoryo,
                CmdInstructionPartCollector.YoryoPartFileName,
                CmdInstructionPartCollector.LegacyYoryoPartFileName);

            FillColumnBySourceFile(transaction, SourceTagColumnName, PartSourceTag.CmdAssembly,
                CmdPartAnalyzer.LtStep1FileName,
                CmdPartAnalyzer.LegacyLtStep1FileName);

            FillColumnBySourceFile(transaction, SourceTagColumnName, PartSourceTag.AiAnalysis,
                "plu_table.txt");
        }

        /// <summary>
        /// 把「来源表格文件」路径里含任一文件名的行，其空的 <paramref name="columnName"/> 填成
        /// <paramref name="value"/>（只填空值，可重复执行）。
        /// </summary>
        private void FillColumnBySourceFile(SQLiteTransaction transaction, string columnName, string value,
            params string[] fileNames)
        {
            StringBuilder sql = new StringBuilder();

            sql.Append("UPDATE [").Append(TableName).Append("] SET [").Append(columnName)
               .Append("] = @value WHERE [").Append(columnName).Append("] IS NULL AND (");

            for (int index = 0; index < fileNames.Length; index++)
            {
                if (index > 0)
                {
                    sql.Append(" OR ");
                }

                sql.Append("[来源表格文件] LIKE @name").Append(index.ToString(CultureInfo.InvariantCulture));
            }

            sql.Append(");");

            using (SQLiteCommand command = new SQLiteCommand(sql.ToString(), _connection, transaction))
            {
                command.Parameters.Add(new SQLiteParameter("@value", value));

                for (int index = 0; index < fileNames.Length; index++)
                {
                    // 用参数（而不是拼接）传文件名，避免 LIKE 通配符被文件名影响
                    command.Parameters.Add(new SQLiteParameter(
                        "@name" + index.ToString(CultureInfo.InvariantCulture),
                        "%" + fileNames[index] + "%"));
                }

                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 把「数据来源」等于 <paramref name="sourceKind"/> 的行，其空的 <paramref name="columnName"/>
        /// 填成 <paramref name="value"/>（只填空值，可重复执行）。
        /// </summary>
        private void FillColumnBySourceKind(SQLiteTransaction transaction, string columnName, string value,
            string sourceKind)
        {
            string sql = "UPDATE [" + TableName + "] SET [" + columnName + "] = @value " +
                "WHERE [" + columnName + "] IS NULL AND [" + SourceKindColumnName + "] = @sourceKind;";

            using (SQLiteCommand command = new SQLiteCommand(sql, _connection, transaction))
            {
                command.Parameters.Add(new SQLiteParameter("@value", value));
                command.Parameters.Add(new SQLiteParameter("@sourceKind", sourceKind));
                command.ExecuteNonQuery();
            }
        }

        private static string BuildInsertSql()
        {
            StringBuilder builder = new StringBuilder();

            builder.Append("INSERT OR REPLACE INTO [").Append(TableName).Append("] (");

            for (int index = 0; index < ColumnNames.Length; index++)
            {
                if (index > 0)
                {
                    builder.Append(", ");
                }

                builder.Append('[').Append(ColumnNames[index]).Append(']');
            }

            builder.Append(") VALUES (");

            for (int index = 0; index < ColumnNames.Length; index++)
            {
                if (index > 0)
                {
                    builder.Append(", ");
                }

                builder.Append('@').Append(ColumnNames[index]);
            }

            builder.Append(");");

            return builder.ToString();
        }

        private void Execute(string sql, SQLiteTransaction transaction)

        {
            using (SQLiteCommand command = new SQLiteCommand(sql, _connection, transaction))
            {
                command.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 登录一批部品基本信息（同一 図番 + 页码 + 番号 覆盖旧值），返回实际写入条数。
        /// </summary>
        /// <param name="drawingNo">図番（PDF 文件名去扩展名）。</param>
        /// <param name="parts">要写入的部品基本信息。</param>
        public int Save(string drawingNo, IList<PartBasicInfo> parts)
        {
            if (string.IsNullOrWhiteSpace(drawingNo))
            {
                throw new ArgumentException("図番不能为空。", nameof(drawingNo));
            }

            if (parts == null || parts.Count == 0)
            {
                return 0;
            }

            string loginTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            string key = drawingNo.Trim();
            int written = 0;

            using (SQLiteTransaction transaction = _connection.BeginTransaction())
            {
                // 同一図番先清空，保证本次结果完整覆盖上次结果（避免残留旧行）
                using (SQLiteCommand deleteCommand = new SQLiteCommand(
                    "DELETE FROM [" + TableName + "] WHERE [図番] = @図番;", _connection, transaction))
                {
                    deleteCommand.Parameters.Add(new SQLiteParameter("@図番", key));
                    deleteCommand.ExecuteNonQuery();
                }

                using (SQLiteCommand command = new SQLiteCommand(InsertSql, _connection, transaction))
                {
                    foreach (string column in ColumnNames)
                    {
                        command.Parameters.Add(new SQLiteParameter("@" + column));
                    }

                    foreach (PartBasicInfo part in parts)
                    {
                        command.Parameters["@図番"].Value = key;
                        command.Parameters["@页码"].Value = part.Page;
                        command.Parameters["@番号"].Value = part.Number;
                        command.Parameters["@部品名"].Value = part.PartName;
                        command.Parameters["@部品図番"].Value = part.PartDrawingNo;
                        command.Parameters["@枠区分"].Value = part.FrameKind;
                        command.Parameters["@員数"].Value = part.Quantity;
                        command.Parameters["@基本寸法"].Value = part.BasicSize;
                        command.Parameters["@切断寸法"].Value = part.CutSize;
                        command.Parameters["@部品コード"].Value = part.PartCode;
                        command.Parameters["@校验状态"].Value = part.VerifyStatus;
                        command.Parameters["@备注"].Value = part.Remark;
                        command.Parameters["@数据来源"].Value = part.SourceKind ?? string.Empty;
                        command.Parameters["@来源标识"].Value = part.SourceTag ?? string.Empty;
                        command.Parameters["@来源表格文件"].Value = part.SourceFile;
                        command.Parameters["@来源PDF"].Value = part.PdfFile;
                        command.Parameters["@登录时间"].Value = loginTime;

                        written += command.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
            }

            return written;
        }

        /// <summary>
        /// 用给定内容整体替换某図番的部品明细（供画面手工编辑后保存）。
        /// 与 <see cref="Save"/> 的区别：本次未修改的行保留原「登录时间」，新增行才写入当前时间；
        /// <paramref name="parts"/> 为空表示清空该図番的全部明细。
        /// </summary>
        /// <param name="drawingNo">図番（PDF 文件名去扩展名）。</param>
        /// <param name="parts">画面上当前的部品明细（全部行）。</param>
        /// <returns>实际写入的条数。</returns>
        public int ReplaceByDrawing(string drawingNo, IList<PartBasicInfo> parts)
        {
            if (string.IsNullOrWhiteSpace(drawingNo))
            {
                throw new ArgumentException("図番不能为空。", nameof(drawingNo));
            }

            string key = drawingNo.Trim();
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            int written = 0;

            using (SQLiteTransaction transaction = _connection.BeginTransaction())
            {
                // 先清空该図番，保证画面上的删除操作真正生效（避免残留旧行）
                using (SQLiteCommand deleteCommand = new SQLiteCommand(
                    "DELETE FROM [" + TableName + "] WHERE [図番] = @図番;", _connection, transaction))
                {
                    deleteCommand.Parameters.Add(new SQLiteParameter("@図番", key));
                    deleteCommand.ExecuteNonQuery();
                }

                if (parts != null && parts.Count > 0)
                {
                    using (SQLiteCommand command = new SQLiteCommand(InsertSql, _connection, transaction))
                    {
                        foreach (string column in ColumnNames)
                        {
                            command.Parameters.Add(new SQLiteParameter("@" + column));
                        }

                        foreach (PartBasicInfo part in parts)
                        {
                            if (part == null)
                            {
                                continue;
                            }

                            string loginTime = string.IsNullOrWhiteSpace(part.LoginTime)
                                ? now
                                : part.LoginTime.Trim();

                            command.Parameters["@図番"].Value = key;
                            command.Parameters["@页码"].Value = part.Page;
                            command.Parameters["@番号"].Value = part.Number;
                            command.Parameters["@部品名"].Value = part.PartName;
                            command.Parameters["@部品図番"].Value = part.PartDrawingNo;
                            command.Parameters["@枠区分"].Value = part.FrameKind;
                            command.Parameters["@員数"].Value = part.Quantity;
                            command.Parameters["@基本寸法"].Value = part.BasicSize;
                            command.Parameters["@切断寸法"].Value = part.CutSize;
                            command.Parameters["@部品コード"].Value = part.PartCode;
                            command.Parameters["@校验状态"].Value = part.VerifyStatus;
                            command.Parameters["@备注"].Value = part.Remark;
                            command.Parameters["@数据来源"].Value = part.SourceKind ?? string.Empty;
                            command.Parameters["@来源标识"].Value = part.SourceTag ?? string.Empty;
                            command.Parameters["@来源表格文件"].Value = part.SourceFile;
                            command.Parameters["@来源PDF"].Value = part.PdfFile;
                            command.Parameters["@登录时间"].Value = loginTime;

                            written += command.ExecuteNonQuery();
                        }
                    }
                }

                transaction.Commit();
            }

            return written;
        }

        /// <summary>统计指定図番已经登录的条数。</summary>
        public int Count(string drawingNo)
        {
            string sql = "SELECT COUNT(1) FROM [" + TableName + "] WHERE [図番] = @図番;";

            using (SQLiteCommand command = new SQLiteCommand(sql, _connection))
            {
                command.Parameters.Add(new SQLiteParameter("@図番", (drawingNo ?? string.Empty).Trim()));

                object result = command.ExecuteScalar();

                return result == null || result == DBNull.Value
                    ? 0
                    : Convert.ToInt32(result, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>统计表内全部条数。</summary>
        public int CountAll()
        {
            string sql = "SELECT COUNT(1) FROM [" + TableName + "];";

            using (SQLiteCommand command = new SQLiteCommand(sql, _connection))
            {
                object result = command.ExecuteScalar();

                return result == null || result == DBNull.Value
                    ? 0
                    : Convert.ToInt32(result, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// 读取指定図番已经登录的全部部品基本信息（按页码、番号排序）。
        /// </summary>
        /// <param name="drawingNo">図番（PDF 文件名去扩展名）。</param>
        public IList<PartBasicInfo> Query(string drawingNo)
        {
            List<PartBasicInfo> parts = new List<PartBasicInfo>();

            string sql =
                "SELECT [図番], [页码], [番号], [部品名], [部品図番], [枠区分], [員数], [基本寸法], " +
                "[切断寸法], [部品コード], [校验状态], [备注], [" + SourceKindColumnName + "], " +
                "[" + SourceTagColumnName + "], [来源表格文件], [来源PDF], [登录时间] " +
                "FROM [" + TableName + "] WHERE [図番] = @図番 " +
                "ORDER BY [页码], [番号];";

            using (SQLiteCommand command = new SQLiteCommand(sql, _connection))
            {
                command.Parameters.Add(new SQLiteParameter("@図番", (drawingNo ?? string.Empty).Trim()));

                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        PartBasicInfo part = new PartBasicInfo(
                            ReadText(reader, 0),
                            reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                            reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                            ReadText(reader, 3),
                            ReadText(reader, 4),
                            ReadText(reader, 5),
                            ReadText(reader, 6),
                            ReadText(reader, 7),
                            ReadText(reader, 8),
                            ReadText(reader, 9));

                        part.VerifyStatus = ReadText(reader, 10);
                        part.Remark = ReadText(reader, 11);
                        part.SourceKind = ReadText(reader, 12);
                        part.SourceTag = ReadText(reader, 13);
                        part.SourceFile = ReadText(reader, 14);
                        part.PdfFile = ReadText(reader, 15);
                        part.LoginTime = ReadText(reader, 16);

                        parts.Add(part);
                    }
                }
            }

            return parts;
        }

        /// <summary>从 PDF 路径解析図番（文件名去扩展名，与 AI 分析登录时一致）。</summary>
        public static string ResolveDrawingNo(string pdfFilePath)
        {
            return string.IsNullOrWhiteSpace(pdfFilePath)
                ? string.Empty
                : Path.GetFileNameWithoutExtension(pdfFilePath.Trim());
        }

        /// <summary>读取可空文本列（NULL 视为空字符串）。</summary>
        private static string ReadText(SQLiteDataReader reader, int ordinal)
        {
            return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
        }

        public void Dispose()
        {
            if (_connection != null)
            {
                if (_connection.State == System.Data.ConnectionState.Open)
                {
                    _connection.Close();
                }

                _connection.Dispose();
            }
        }
    }
}
